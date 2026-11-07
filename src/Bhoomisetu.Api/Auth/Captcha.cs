using System.Text.Json.Serialization;

namespace Bhoomisetu.Api.Auth;

/// <summary>
/// Human check for OTP requests. Asked for only when a number already received a code in the last hour, so
/// a normal first sign-in never sees it, while repeated requests (lock-out or SMS-pumping attempts) do.
/// </summary>
public interface ICaptchaVerifier
{
    /// <summary>False when no provider is configured; then no check is asked for.</summary>
    bool Enabled { get; }

    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}

public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    /// <summary>"none" (default) or "turnstile".</summary>
    public string Provider { get; set; } = "none";

    /// <summary>Server-side secret from the provider. From the secret store, never committed.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Codes a number may get in an hour before a human check is required.</summary>
    public int FreeRequestsPerHour { get; set; } = 1;
}

public sealed class NoCaptchaVerifier : ICaptchaVerifier
{
    public bool Enabled => false;

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Cloudflare Turnstile: free, privacy-preserving, and works on low-end phones without image puzzles.</summary>
public sealed class TurnstileVerifier(HttpClient http, CaptchaOptions options, ILogger<TurnstileVerifier> logger) : ICaptchaVerifier
{
    private static readonly Uri Endpoint = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public bool Enabled => true;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = options.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile returned {Status}", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileResult>(cancellationToken);
            return result?.Success == true;
        }
        catch (HttpRequestException ex)
        {
            // Fail closed: if the check cannot run, the repeated request is refused, not let through.
            logger.LogWarning(ex, "Turnstile could not be reached");
            return false;
        }
    }

    private sealed record TurnstileResult([property: JsonPropertyName("success")] bool Success);
}
