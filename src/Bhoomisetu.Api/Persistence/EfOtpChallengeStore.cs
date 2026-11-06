using Bhoomisetu.Modules.Identity;
using Microsoft.EntityFrameworkCore;

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

    public Task<int> CountCreatedSinceAsync(string phoneE164, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.OtpChallenges.CountAsync(c => c.PhoneE164 == phoneE164 && c.CreatedAt >= since, cancellationToken);

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
