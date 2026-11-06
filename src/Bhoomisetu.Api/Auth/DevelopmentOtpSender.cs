using Bhoomisetu.Modules.Identity;

namespace Bhoomisetu.Api.Auth;

/// <summary>
/// Development only: writes the code to the log instead of sending an SMS. Program.cs refuses to start in
/// any other environment while this sender is configured.
/// </summary>
public sealed class DevelopmentOtpSender(ILogger<DevelopmentOtpSender> logger) : IOtpSender
{
    public Task SendAsync(IndianMobileNumber phone, string code, TimeSpan validFor, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT OTP for {Phone}: {Code} (valid {Minutes} min). Never enable outside Development.",
            phone.Masked, code, validFor.TotalMinutes);
        return Task.CompletedTask;
    }
}
