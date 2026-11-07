using System.Globalization;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Identity;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record OtpRequestBody(string? Phone, string? CaptchaToken = null);

public sealed record OtpVerifyBody(string? Phone, string? Code);

public sealed record SignInResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId);

public sealed record MeResponse(Guid Id, string PhoneMasked, string? DisplayName, string PreferredLocale, IReadOnlyList<string> Checks, bool IsTrustReviewer);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/otp/request", async (OtpRequestBody body, OtpService otp, ICaptchaVerifier captcha, CaptchaOptions captchaOptions, BhoomisetuDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var phone = IndianMobileNumber.Parse(body.Phone);
            if (!phone.IsSuccess)
            {
                return ApiResults.Invalid(phone.Error!);
            }

            // A first code is free; asking again for the same number within the hour needs a human check.
            if (captcha.Enabled)
            {
                var e164 = phone.Value.E164;
                var hourAgo = clock.UtcNow - TimeSpan.FromHours(1);
                var recent = await db.OtpChallenges.CountAsync(c => c.PhoneE164 == e164 && c.CreatedAt >= hourAgo, ct);
                if (recent >= captchaOptions.FreeRequestsPerHour)
                {
                    if (string.IsNullOrWhiteSpace(body.CaptchaToken))
                    {
                        return ApiResults.Problem(StatusCodes.Status400BadRequest, "captcha.required", "Please complete the check to get another code.");
                    }

                    if (!await captcha.VerifyAsync(body.CaptchaToken, http.Connection.RemoteIpAddress?.ToString(), ct))
                    {
                        return ApiResults.Problem(StatusCodes.Status400BadRequest, "captcha.invalid", "The check did not pass. Please try again.");
                    }
                }
            }

            var outcome = await otp.RequestAsync(phone.Value, RateLimitPolicies.ClientKey(http), ct);
            if (outcome.Status == OtpRequestStatus.Sent)
            {
                return Results.Accepted();
            }

            if (outcome.Status == OtpRequestStatus.Busy)
            {
                http.RequestServices.GetRequiredService<ILogger<OtpRequestBody>>()
                    .LogWarning("Platform SMS budget reached; OTP refused for {Phone}. Check for SMS-pumping or raise Otp:MaxSmsPerHourGlobal.", phone.Value.Masked);
            }

            var retry = (int)Math.Ceiling((outcome.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds);
            http.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
            var extra = new Dictionary<string, object?> { ["retryAfterSeconds"] = retry };
            return outcome.Status switch
            {
                OtpRequestStatus.TooSoon => ApiResults.Problem(StatusCodes.Status429TooManyRequests, "otp.too_soon", "Please wait before asking for another code.", extra),
                OtpRequestStatus.Busy => ApiResults.Problem(StatusCodes.Status503ServiceUnavailable, "otp.busy", "We are sending too many codes right now. Please try again in a few minutes.", extra),
                _ => ApiResults.Problem(StatusCodes.Status429TooManyRequests, "otp.rate_limited", "Too many codes requested for this number. Try again later.", extra),
            };
        }).RequireRateLimiting(RateLimitPolicies.Otp);

        group.MapPost("/otp/verify", async (OtpVerifyBody body, OtpService otp, BhoomisetuDbContext db, TokenService tokens, IClock clock, AuditWriter audit, CancellationToken ct) =>
        {
            var phone = IndianMobileNumber.Parse(body.Phone);
            if (!phone.IsSuccess)
            {
                return ApiResults.Invalid(phone.Error!);
            }

            var status = await otp.VerifyAsync(phone.Value, body.Code, ct);
            switch (status)
            {
                case OtpVerifyStatus.Verified:
                    break;
                case OtpVerifyStatus.TooManyAttempts:
                    return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "otp.too_many_attempts", "Too many wrong codes. Ask for a new code.");
                case OtpVerifyStatus.Expired:
                    return ApiResults.Invalid("otp.expired", "This code has expired. Ask for a new code.");
                case OtpVerifyStatus.NoActiveChallenge:
                    return ApiResults.Invalid("otp.no_active_code", "No active code for this number. Ask for a new code.");
                default:
                    return ApiResults.Invalid("otp.invalid", "That code is not correct.");
            }

            var now = clock.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var (userId, isReviewer, tokenVersion) = await UpsertVerifiedUserAsync(db, phone.Value.E164, now, ct);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO user_checks (user_id, check_type, verified_at) VALUES ({userId}, 'phone_otp', {now}) ON CONFLICT (user_id, check_type) DO UPDATE SET verified_at = EXCLUDED.verified_at",
                ct);

            audit.Add(userId, "auth.signed_in", "user", userId.ToString(), new { method = "phone_otp" });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var (token, expiresAt) = tokens.Issue(userId, isReviewer, tokenVersion);
            return Results.Ok(new SignInResponse(token, expiresAt, userId));
        }).RequireRateLimiting(RateLimitPolicies.Otp);

        // Ends every session of this user on the server, not just the cookie in one browser.
        app.MapPost("/api/v1/auth/sign-out", async (HttpContext http, BhoomisetuDbContext db, AuditWriter audit, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), ct);
            audit.Add(userId, "auth.signed_out", "user", userId.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Auth");

        app.MapGet("/api/v1/me", async (HttpContext http, BhoomisetuDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                return ApiResults.NotFound("User");
            }

            var checks = await db.UserChecks.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.CheckType).OrderBy(c => c).ToListAsync(ct);
            return Results.Ok(new MeResponse(user.Id, $"+91******{user.PhoneE164[^4..]}", user.DisplayName, user.PreferredLocale, checks, user.IsTrustReviewer));
        }).RequireAuthorization().WithTags("Auth");
    }

    /// <summary>
    /// Insert-or-update in one statement, so two parallel sign-ins for a new number cannot both insert.
    /// Runs inside the caller's EF transaction.
    /// </summary>
    private static async Task<(Guid UserId, bool IsTrustReviewer, int TokenVersion)> UpsertVerifiedUserAsync(BhoomisetuDbContext db, string phoneE164, DateTimeOffset now, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("UpsertVerifiedUserAsync must run inside a transaction.");
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, phone_e164, phone_verified_at, created_at, updated_at)
            VALUES (@id, @phone, @now, @now, @now)
            ON CONFLICT (phone_e164) DO UPDATE SET phone_verified_at = EXCLUDED.phone_verified_at, updated_at = EXCLUDED.updated_at
            RETURNING id, is_trust_reviewer, token_version
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("phone", phoneE164);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("User upsert returned no row.");
        }

        return (reader.GetGuid(0), reader.GetBoolean(1), reader.GetInt32(2));
    }
}
