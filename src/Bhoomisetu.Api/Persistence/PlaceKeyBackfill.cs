using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Persistence;

/// <summary>
/// Fills the sound-alike search keys (migration 0011) for land listed before they existed. Runs once at start-up
/// in small batches; new and edited land gets its keys on save, so after the first run there is nothing to do.
/// </summary>
public sealed class PlaceKeyBackfill(IServiceScopeFactory scopes, ILogger<PlaceKeyBackfill> logger) : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var total = 0;
            var after = Guid.Empty;
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BhoomisetuDbContext>();

                // Pages forward by id, so a row that somehow keeps a null key can never make this loop forever.
                var batch = await db.Parcels.Where(p => p.VillageKey == null && p.Id.CompareTo(after) > 0).OrderBy(p => p.Id).Take(BatchSize).ToListAsync(stoppingToken);
                if (batch.Count == 0)
                {
                    break;
                }

                // Only the keys change; UpdatedAt stays as it was, since nothing a person sees has changed.
                foreach (var parcel in batch)
                {
                    BhoomisetuDbContext.SetPlaceKeys(parcel);
                }

                await db.SaveChangesAsync(stoppingToken);
                total += batch.Count;
                after = batch[^1].Id;
            }

            if (total > 0)
            {
                logger.LogInformation("Filled search keys for {Count} parcels.", total);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Search still works by name without keys, so a failure here is logged, not fatal.
            logger.LogWarning(ex, "Search key backfill stopped; it will run again at the next start.");
        }
    }
}
