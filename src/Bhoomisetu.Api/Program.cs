using System.Threading.RateLimiting;
using Bhoomisetu.Api;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Endpoints;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Identity;
using Bhoomisetu.Modules.Offers;
using Bhoomisetu.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- configuration, validated at start-up
var connectionString = config.GetConnectionString("Bhoomisetu")
    ?? throw new InvalidOperationException("ConnectionStrings:Bhoomisetu is not configured.");

var authOptions = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var signingKey = authOptions.CreateKey(); // throws when missing or too short

var pepperBase64 = config["Otp:Pepper"];
var otpOptions = new OtpOptions
{
    Pepper = string.IsNullOrWhiteSpace(pepperBase64) ? [] : Convert.FromBase64String(pepperBase64),
    ResendCooldown = TimeSpan.FromSeconds(config.GetValue("Otp:ResendCooldownSeconds", 60)),
    MaxRequestsPerHour = config.GetValue("Otp:MaxRequestsPerHour", 5),
};
otpOptions.Validate();

var smsProvider = config["Sms:Provider"] ?? "development";
if (!string.Equals(smsProvider, "development", StringComparison.OrdinalIgnoreCase))
{
    // Real providers are added behind IOtpSender once DLT registration is done (decision TDR-14).
    throw new InvalidOperationException($"SMS provider '{smsProvider}' is not implemented yet.");
}

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "Refusing to start: the development OTP sender writes codes to the log. Configure a DLT-registered SMS provider before running outside Development.");
}

var otpRequestsPerIpPerMinute = config.GetValue("RateLimits:OtpPerIpPerMinute", 10);
var requestsPerIpPerMinute = config.GetValue("RateLimits:RequestsPerIpPerMinute", 300);

var rankingOptions = RankingOptions.Default;
rankingOptions.Validate();

// ---------------------------------------------------------------- services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton(otpOptions);
builder.Services.AddSingleton(rankingOptions);
builder.Services.AddSingleton<BestValueRanker>();
builder.Services.AddSingleton<IOtpSender, DevelopmentOtpSender>();

builder.Services.AddDbContext<BhoomisetuDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IOtpChallengeStore, EfOtpChallengeStore>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AuditWriter>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = AuthOptions.RoleClaimType,
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminEndpoints.TrustReviewerPolicy, policy => policy.RequireRole(AuthOptions.TrustReviewerRole));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Otp, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = otpRequestsPerIpPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerIpPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var webOrigins = config.GetSection("Cors:WebOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(webOrigins).AllowAnyHeader().WithMethods("GET", "POST", "DELETE")));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Behind the AWS load balancer the client IP arrives in X-Forwarded-For. Known proxies are set per environment.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// ---------------------------------------------------------------- migrations
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || config.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    var runner = new MigrationRunner(connectionString, Path.Combine(AppContext.BaseDirectory, "migrations"),
        app.Services.GetRequiredService<ILogger<MigrationRunner>>());
    await runner.ApplyAsync(CancellationToken.None);
    if (migrateOnly)
    {
        return;
    }
}

// ---------------------------------------------------------------- pipeline
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.CacheControl = "no-store";
    await next();
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapUnitsEndpoints();
app.MapParcelEndpoints();
app.MapOfferEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program
{
}

namespace Bhoomisetu.Api
{
    public static class RateLimitPolicies
    {
        public const string Otp = "otp";
    }

    internal sealed class DatabaseHealthCheck(BhoomisetuDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is not reachable.");
    }
}
