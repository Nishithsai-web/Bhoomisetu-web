namespace Bhoomisetu.Modules.Identity;

public sealed record OtpOptions
{
    public int CodeLength { get; init; } = 6;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ResendCooldown { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxRequestsPerHour { get; init; } = 5;
    public int MaxVerifyAttempts { get; init; } = 5;

    /// <summary>Server-side secret mixed into the code hash. At least 32 bytes; from the secret store, never from code.</summary>
    public byte[] Pepper { get; init; } = [];

    public void Validate()
    {
        if (Pepper.Length < 32)
        {
            throw new InvalidOperationException("OTP pepper must be at least 32 bytes.");
        }

        if (CodeLength is < 4 or > 8 || Lifetime <= TimeSpan.Zero || ResendCooldown < TimeSpan.Zero || MaxRequestsPerHour < 1 || MaxVerifyAttempts < 1)
        {
            throw new InvalidOperationException("OTP options are out of range.");
        }
    }
}

public sealed class OtpChallenge
{
    public required Guid Id { get; init; }
    public required string PhoneE164 { get; init; }
    public required byte[] CodeHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public int Attempts { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }
}

/// <summary>
/// Persistence for OTP challenges. Attempt counting and consumption must be atomic in the store (a single
/// conditional UPDATE), so parallel verify calls cannot get extra guesses or use one code twice.
/// </summary>
public interface IOtpChallengeStore
{
    Task<OtpChallenge?> GetLatestAsync(string phoneE164, CancellationToken cancellationToken);

    Task<int> CountCreatedSinceAsync(string phoneE164, DateTimeOffset since, CancellationToken cancellationToken);

    Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken);

    /// <summary>Adds one attempt if fewer than <paramref name="maxAttempts"/> were used and the challenge is unconsumed. Returns false otherwise.</summary>
    Task<bool> TryRecordAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Marks the challenge consumed if it is not already. Returns false if another request consumed it first.</summary>
    Task<bool> TryConsumeAsync(Guid challengeId, DateTimeOffset consumedAt, CancellationToken cancellationToken);
}

/// <summary>Sends the OTP text. Production implementations use a DLT-registered template; the code fills a {#numeric#} variable.</summary>
public interface IOtpSender
{
    Task SendAsync(IndianMobileNumber phone, string code, TimeSpan validFor, CancellationToken cancellationToken);
}

public enum OtpRequestStatus
{
    Sent,
    TooSoon,
    RateLimited,
}

public sealed record OtpRequestOutcome(OtpRequestStatus Status, TimeSpan? RetryAfter);

public enum OtpVerifyStatus
{
    Verified,
    Invalid,
    Expired,
    TooManyAttempts,
    NoActiveChallenge,
}
