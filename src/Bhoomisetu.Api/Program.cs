using System.Threading.RateLimiting;
using Bhoomisetu.Api;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Documents;
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
var migrationsDirectory = Path.Combine(AppContext.BaseDirectory, "migrations");

// `dotnet Bhoomisetu.Api.dll --migrate-only` applies the schema and exits. It needs only the connection
// string, so deployment pipelines can migrate in any environment before the new version starts.
if (args.Contains("--migrate-only"))
{
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    await new MigrationRunner(connectionString, migrationsDirectory, loggerFactory.CreateLogger<MigrationRunner>()).ApplyAsync(CancellationToken.None);
    return;
}

var authOptions = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var signingKey = authOptions.CreateKey(); // throws when missing or too short

var pepperBase64 = config["Otp:Pepper"];
var otpOptions = new OtpOptions
{
    Pepper = string.IsNullOrWhiteSpace(pepperBase64) ? [] : Convert.FromBase64String(pepperBase64),
    ResendCooldown = TimeSpan.FromSeconds(config.GetValue("Otp:ResendCooldownSeconds", 60)),
    MaxRequestsPerHour = config.GetValue("Otp:MaxRequestsPerHour", 5),
    MaxRequestsPerHourPerNumber = config.GetValue("Otp:MaxRequestsPerHourPerNumber", 10),
    MaxHumanCheckedRequestsPerHourPerNumber = config.GetValue("Otp:MaxHumanCheckedRequestsPerHourPerNumber", 20),
    MaxVerifyAttempts = config.GetValue("Otp:MaxVerifyAttempts", 3),
    MaxSmsPerHourGlobal = config.GetValue("Otp:MaxSmsPerHourGlobal", 20000),
};
otpOptions.Validate();

// Behind a load balancer or the web server, the client IP comes from X-Forwarded-For, which is trusted only
// from these addresses (KnownProxies) or ranges (KnownNetworks, CIDR such as 10.0.0.0/16 for a VPC).
var knownProxies = (config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    .Select(address => System.Net.IPAddress.Parse(address))
    .ToList();
var knownNetworks = (config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    .Select(cidr => System.Net.IPNetwork.Parse(cidr))
    .ToList();
var isLocalEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
if (!isLocalEnvironment && knownProxies.Count == 0 && knownNetworks.Count == 0)
{
    throw new InvalidOperationException(
        "ForwardedHeaders:KnownProxies or KnownNetworks must list the load balancer and web server, or every user shares one rate limit.");
}

var smsProvider = config["Sms:Provider"] ?? "development";
if (!string.Equals(smsProvider, "development", StringComparison.OrdinalIgnoreCase))
{
    // Real providers are added behind IOtpSender once DLT registration is done (decision TDR-14).
    throw new InvalidOperationException($"SMS provider '{smsProvider}' is not implemented yet.");
}

// "Testing" is the automated test environment (WebApplicationFactory); it never serves real users.
if (!isLocalEnvironment)
{
    throw new InvalidOperationException(
        "Refusing to start: the development OTP sender writes codes to the log. Configure a DLT-registered SMS provider before running outside Development.");
}

var otpRequestsPerIpPerMinute = config.GetValue("RateLimits:OtpPerIpPerMinute", 10);
var requestsPerIpPerMinute = config.GetValue("RateLimits:RequestsPerIpPerMinute", 300);

// Weights and thresholds are published to users; change them in configuration and record why in docs/decisions.
var rankingOptions = config.GetSection("Ranking").Get<RankingOptions>() ?? RankingOptions.Default;
rankingOptions.Validate();

// ---------------------------------------------------------------- services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton(otpOptions);
builder.Services.AddSingleton(rankingOptions);
builder.Services.AddSingleton<BestValueRanker>();
builder.Services.AddSingleton<IOtpSender, DevelopmentOtpSender>();

var captchaOptions = config.GetSection(CaptchaOptions.SectionName).Get<CaptchaOptions>() ?? new CaptchaOptions();
builder.Services.AddSingleton(captchaOptions);
switch (captchaOptions.Provider.ToLowerInvariant())
{
    case "none":
        if (builder.Environment.IsProduction() && !captchaOptions.AllowDisabledInProduction)
        {
            throw new InvalidOperationException(
                "Captcha:Provider is none in Production. Configure turnstile, or set Captcha:AllowDisabledInProduction=true to accept the lock-out risk.");
        }

        builder.Services.AddSingleton<ICaptchaVerifier, NoCaptchaVerifier>();
        break;
    case "turnstile":
        if (string.IsNullOrWhiteSpace(captchaOptions.SecretKey))
        {
            throw new InvalidOperationException("Captcha:SecretKey is required when Captcha:Provider is turnstile.");
        }

        builder.Services.AddHttpClient<ICaptchaVerifier, TurnstileVerifier>(client => client.Timeout = TimeSpan.FromSeconds(5));
        break;
    default:
        throw new InvalidOperationException($"Unknown Captcha:Provider '{captchaOptions.Provider}'. Use none or turnstile.");
}

builder.Services.AddDbContext<BhoomisetuDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IOtpChallengeStore, EfOtpChallengeStore>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AuditWriter>();

var documentOptions = config.GetSection(DocumentOptions.SectionName).Get<DocumentOptions>() ?? new DocumentOptions();
builder.Services.AddSingleton(documentOptions);
builder.Services.AddSingleton<IDocumentStorage>(new LocalDocumentStorage(Path.Combine(builder.Environment.ContentRootPath, documentOptions.StoragePath)));
builder.Services.AddSingleton(config.GetSection(SubscriptionOptions.SectionName).Get<SubscriptionOptions>() ?? new SubscriptionOptions());
builder.Services.AddScoped<SubscriptionService>();

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

        // A token is only as good as the user's current token version: signing out bumps it, which ends every
        // session at once instead of waiting up to 24 hours for the token to expire.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var tv = principal?.FindFirst(AuthOptions.TokenVersionClaimType)?.Value;
                if (!Guid.TryParse(sub, out var userId) || !int.TryParse(tv, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version))
                {
                    context.Fail("Token is missing its subject or version.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<BhoomisetuDbContext>();
                var current = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.TokenVersion == version, context.HttpContext.RequestAborted);
                if (!current)
                {
                    context.Fail("Session has ended.");
                }
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminEndpoints.TrustReviewerPolicy, policy => policy.RequireRole(AuthOptions.TrustReviewerRole));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Otp, http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPolicies.ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = otpRequestsPerIpPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Per visitor IP. Off (0) by default: unless the web server passes visitors' IPs (BHOOMISETU_BEHIND_LOAD_BALANCER),
    // every request arrives from the web server's own address and one shared limit would block everyone.
    var contactsPerIpPerDay = config.GetValue("RateLimits:ContactsPerIpPerDay", 0);
    options.AddPolicy(RateLimitPolicies.Contact, http => contactsPerIpPerDay > 0
        ? RateLimitPartition.GetFixedWindowLimiter(RateLimitPolicies.ClientKey(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = contactsPerIpPerDay, Window = TimeSpan.FromDays(1), QueueLimit = 0 })
        : RateLimitPartition.GetNoLimiter("off"));

    // A 429 explains itself like every other error, so the web app can show a plain message.
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { status = 429, code = "rate_limited", title = "Too many requests. Please wait a little and try again." }, token);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPolicies.ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerIpPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var webOrigins = config.GetSection("Cors:WebOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(webOrigins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE")));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Behind the AWS load balancer the client IP arrives in X-Forwarded-For. Known proxies are set per environment.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in knownProxies)
    {
        options.KnownProxies.Add(proxy);
    }

    foreach (var network in knownNetworks)
    {
        options.KnownIPNetworks.Add(network);
    }
});

var app = builder.Build();

// ---------------------------------------------------------------- migrations
if (config.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    var runner = new MigrationRunner(connectionString, migrationsDirectory, app.Services.GetRequiredService<ILogger<MigrationRunner>>());
    await runner.ApplyAsync(CancellationToken.None);
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
app.MapMyOffersEndpoints();
app.MapChangeRequestEndpoints();
app.MapTrustEndpoints();
app.MapDocumentEndpoints();
app.MapDevelopmentEndpoints();

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

        /// <summary>WhatsApp number reveals per IP (or IPv6 /64) per day, on top of the per-account limit.</summary>
        public const string Contact = "contact";

        /// <summary>
        /// Rate-limit key for a client: the IPv4 address, or the /64 prefix for IPv6, because one home or phone
        /// connection usually owns a whole /64 and could otherwise rotate addresses to dodge the limit.
        /// </summary>
        public static string ClientKey(HttpContext http)
        {
            var ip = http.Connection.RemoteIpAddress;
            if (ip is null)
            {
                return "unknown";
            }

            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return ip.ToString();
            }

            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new System.Net.IPAddress(bytes) + "/64";
        }
    }

    internal sealed class DatabaseHealthCheck(BhoomisetuDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is not reachable.");
    }
}
