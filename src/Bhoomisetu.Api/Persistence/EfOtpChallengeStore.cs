using Bhoomisetu.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bhoomisetu.Api.Persistence;

public sealed class EfOtpChallengeStore(BhoomisetuDbContext db) : IOtpChallengeStore
{
    public async Task<OtpChallenge?> GetLatestAsync(string phoneE164, CancellationToken cancellationToken)
    {
        var row = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.PhoneE164 == phoneE164)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new OtpChallenge
            {
                Id = row.Id,
                PhoneE164 = row.PhoneE164,
                CodeHash = row.CodeHash,
                CreatedAt = row.CreatedAt,
                ExpiresAt = row.ExpiresAt,
                Attempts = row.Attempts,
                ConsumedAt = row.ConsumedAt,
            };
    }

    public Task<int> CountCreatedSinceAsync(string phoneE164, DateTimeOffset since, string? requesterKey, CancellationToken cancellationToken) =>
        requesterKey is null
            ? db.OtpChallenges.CountAsync(c => c.PhoneE164 == phoneE164 && c.CreatedAt >= since, cancellationToken)
            : db.OtpChallenges.CountAsync(c => c.PhoneE164 == phoneE164 && c.CreatedAt >= since && c.RequesterKey == requesterKey, cancellationToken);

    public Task<int> CountAllCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        db.OtpChallenges.CountAsync(c => c.CreatedAt >= since, cancellationToken);

    /// <summary>A transaction holding a PostgreSQL advisory lock keyed on the number, released at commit or rollback.</summary>
    public async Task<IOtpPhoneLock> LockPhoneAsync(string phoneE164, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"otp:" + phoneE164}))", cancellationToken);
        return new TransactionLock(transaction);
    }

    private sealed class TransactionLock(IDbContextTransaction transaction) : IOtpPhoneLock
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    public async Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken)
    {
        db.OtpChallenges.Add(new OtpChallengeRecord
        {
            Id = challenge.Id,
            PhoneE164 = challenge.PhoneE164,
            CodeHash = challenge.CodeHash,
            CreatedAt = challenge.CreatedAt,
            ExpiresAt = challenge.ExpiresAt,
            Attempts = challenge.Attempts,
            ConsumedAt = challenge.ConsumedAt,
            RequesterKey = challenge.RequesterKey,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryRecordAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken)
    {
        var updated = await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAt == null && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryConsumeAsync(Guid challengeId, DateTimeOffset consumedAt, CancellationToken cancellationToken)
    {
        var updated = await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, consumedAt), cancellationToken);
        return updated == 1;
    }
}
