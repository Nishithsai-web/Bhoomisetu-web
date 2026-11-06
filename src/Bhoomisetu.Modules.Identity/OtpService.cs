using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Identity;

/// <summary>
/// Issues and checks one-time codes for phone sign-in. A verified code proves the person controls the
/// number at that moment; it says nothing about who owns a property.
/// </summary>
public sealed class OtpService
{
    private readonly IOtpChallengeStore _store;
    private readonly IOtpSender _sender;
    private readonly IClock _clock;
    private readonly OtpOptions _options;

    public OtpService(IOtpChallengeStore store, IOtpSender sender, IClock clock, OtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options;
    }

    public async Task<OtpRequestOutcome> RequestAsync(IndianMobileNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);
        var now = _clock.UtcNow;

        var latest = await _store.GetLatestAsync(phone.E164, cancellationToken).ConfigureAwait(false);
        if (latest is not null && now - latest.CreatedAt < _options.ResendCooldown)
        {
            return new OtpRequestOutcome(OtpRequestStatus.TooSoon, _options.ResendCooldown - (now - latest.CreatedAt));
        }

        var recent = await _store.CountCreatedSinceAsync(phone.E164, now - TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
        if (recent >= _options.MaxRequestsPerHour)
        {
            return new OtpRequestOutcome(OtpRequestStatus.RateLimited, TimeSpan.FromHours(1));
        }

        var code = GenerateCode();
        var challenge = new OtpChallenge
        {
            Id = Guid.CreateVersion7(),
            PhoneE164 = phone.E164,
            CodeHash = Hash(phone.E164, code),
            CreatedAt = now,
            ExpiresAt = now + _options.Lifetime,
        };

        await _store.AddAsync(challenge, cancellationToken).ConfigureAwait(false);
        await _sender.SendAsync(phone, code, _options.Lifetime, cancellationToken).ConfigureAwait(false);
        return new OtpRequestOutcome(OtpRequestStatus.Sent, null);
    }

    public async Task<OtpVerifyStatus> VerifyAsync(IndianMobileNumber phone, string? code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);
        var now = _clock.UtcNow;
        var challenge = await _store.GetLatestAsync(phone.E164, cancellationToken).ConfigureAwait(false);
        if (challenge is null || challenge.ConsumedAt is not null)
        {
            return OtpVerifyStatus.NoActiveChallenge;
        }

        if (now >= challenge.ExpiresAt)
        {
            return OtpVerifyStatus.Expired;
        }

        if (challenge.Attempts >= _options.MaxVerifyAttempts)
        {
            return OtpVerifyStatus.TooManyAttempts;
        }

        // Record the attempt atomically before comparing, so parallel or crashed requests never get free guesses.
        if (!await _store.TryRecordAttemptAsync(challenge.Id, _options.MaxVerifyAttempts, cancellationToken).ConfigureAwait(false))
        {
            return OtpVerifyStatus.TooManyAttempts;
        }

        var normalized = IndianDigits.ToAscii(code ?? string.Empty).Trim();
        var matches = normalized.Length == _options.CodeLength
            && normalized.All(char.IsAsciiDigit)
            && CryptographicOperations.FixedTimeEquals(Hash(phone.E164, normalized), challenge.CodeHash);

        if (matches)
        {
            return await _store.TryConsumeAsync(challenge.Id, now, cancellationToken).ConfigureAwait(false)
                ? OtpVerifyStatus.Verified
                : OtpVerifyStatus.NoActiveChallenge;
        }

        return challenge.Attempts + 1 >= _options.MaxVerifyAttempts ? OtpVerifyStatus.TooManyAttempts : OtpVerifyStatus.Invalid;
    }

    private string GenerateCode()
    {
        var max = (int)Math.Pow(10, _options.CodeLength);
        return RandomNumberGenerator.GetInt32(0, max).ToString(CultureInfo.InvariantCulture).PadLeft(_options.CodeLength, '0');
    }

    private byte[] Hash(string phoneE164, string code) =>
        HMACSHA256.HashData(_options.Pepper, Encoding.UTF8.GetBytes($"{phoneE164}:{code}"));
}
