using System.Text.Json;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Api.Persistence;

/// <summary>
/// Adds an audit row to the current unit of work, so the audit entry and the change it describes are saved
/// in the same transaction or not at all. Never put personal data (phone numbers, document contents) in details.
/// </summary>
public sealed class AuditWriter(BhoomisetuDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Add(Guid? actorUserId, string action, string entityType, string entityId, object? details = null)
    {
        db.AuditLog.Add(new AuditRecord
        {
            OccurredAt = clock.UtcNow,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = JsonSerializer.Serialize(details ?? new { }, JsonOptions),
        });
    }
}
