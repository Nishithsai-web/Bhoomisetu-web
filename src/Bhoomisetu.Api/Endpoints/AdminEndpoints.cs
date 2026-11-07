using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record SetOwnerOfRecordRequest(Guid UserId, string? EvidenceNote);

public static class AdminEndpoints
{
    public const string TrustReviewerPolicy = "TrustReviewer";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization(TrustReviewerPolicy)
            .AddEndpointFilter(RequireCurrentReviewerAsync);
        group.MapPost("/parcels/{parcelId:guid}/owner-of-record", SetOwnerOfRecordAsync);
        group.MapPost("/duplicates/{parcelId:guid}/merge", MergeDuplicateAsync);
    }

    /// <summary>
    /// The role claim lives in a token that stays valid for up to its lifetime, so the database is checked on
    /// every admin call: removing someone's reviewer flag takes effect immediately.
    /// </summary>
    internal static async ValueTask<object?> RequireCurrentReviewerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<BhoomisetuDbContext>();
        var userId = http.User.UserId();
        var stillReviewer = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsTrustReviewer, http.RequestAborted);
        return stillReviewer
            ? await next(context)
            : ApiResults.Forbidden("admin.not_reviewer", "You are no longer a trust reviewer.");
    }

    /// <summary>
    /// Merges a record that migration 0003 set aside as a duplicate into the record kept for that land. The
    /// duplicate record itself stays, hidden from search, so its history is not lost.
    /// <list type="bullet">
    /// <item>Owner of record: moves across when the kept record has none; if both have different owners the merge
    /// is refused for a reviewer to settle with documents.</item>
    /// <item>Agent confirmations move across (the kept record's own confirmations win on overlap).</item>
    /// <item>Offers: a lister with active offers on both keeps the most recent one; the older is withdrawn.</item>
    /// </list>
    /// </summary>
    private static async Task<IResult> MergeDuplicateAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var reviewerId = http.User.UserId();
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var kept = await db.Database
            .SqlQuery<Guid>($"SELECT kept_parcel_id AS \"Value\" FROM parcel_duplicate_candidates WHERE parcel_id = {parcelId} AND resolved_at IS NULL FOR UPDATE")
            .ToListAsync(ct);
        if (kept.Count == 0)
        {
            return ApiResults.NotFound("Unresolved duplicate");
        }

        var keptId = kept[0];
        Guid[] both = [parcelId, keptId];

        // Same rule as correction decisions: a reviewer with a stake in either record leaves it to another reviewer.
        var involved = await db.Parcels.AnyAsync(p => both.Contains(p.Id) && p.CreatedBy == reviewerId, ct)
            || await db.Offers.AnyAsync(o => both.Contains(o.ParcelId) && o.ListerUserId == reviewerId, ct)
            || await db.ParcelParties.AnyAsync(p => both.Contains(p.ParcelId) && p.UserId == reviewerId, ct);
        if (involved)
        {
            return ApiResults.Forbidden("merge.reviewer_involved", "You are involved with this land, so another reviewer must merge it.");
        }

        var owners = await db.ParcelParties
            .Where(p => both.Contains(p.ParcelId) && p.Role == Vocabulary.OwnerOfRecord)
            .Select(p => new { p.ParcelId, p.UserId })
            .ToListAsync(ct);
        var duplicateOwner = owners.FirstOrDefault(o => o.ParcelId == parcelId)?.UserId;
        var keptOwner = owners.FirstOrDefault(o => o.ParcelId == keptId)?.UserId;
        if (duplicateOwner is { } d && keptOwner is { } k && d != k)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "merge.owner_conflict",
                "The two records have different owners. Check the documents and record the right owner on the kept record first.");
        }

        try
        {
            // Owner and agents: copy what the kept record lacks, then remove them from the duplicate.
            var movedParties = await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO parcel_parties (parcel_id, user_id, role, confirmed_at, confirmed_by)
                SELECT {keptId}, user_id, role, confirmed_at, confirmed_by FROM parcel_parties WHERE parcel_id = {parcelId}
                ON CONFLICT DO NOTHING
                """, ct);
            await db.Database.ExecuteSqlAsync($"DELETE FROM parcel_parties WHERE parcel_id = {parcelId}", ct);

            // Offers: per lister, only the most recent active offer across both records survives.
            var withdrawn = await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE offers o SET status = 'withdrawn', updated_at = {now}
                WHERE o.parcel_id IN ({parcelId}, {keptId}) AND o.status = 'active'
                  AND EXISTS (
                      SELECT 1 FROM offers n
                      WHERE n.parcel_id IN ({parcelId}, {keptId}) AND n.status = 'active'
                        AND n.lister_user_id = o.lister_user_id AND n.id <> o.id
                        AND (n.created_at, n.id) > (o.created_at, o.id))
                """, ct);
            var moved = await db.Database.ExecuteSqlAsync(
                $"UPDATE offers SET parcel_id = {keptId}, updated_at = {now} WHERE parcel_id = {parcelId} AND status = 'active'", ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE parcel_duplicate_candidates SET resolved_at = {now} WHERE parcel_id = {parcelId}", ct);

            audit.Add(reviewerId, "parcel.duplicate_merged", "parcel", parcelId.ToString(),
                new { keptParcelId = keptId, moved, withdrawn, movedParties, ownerMoved = keptOwner is null && duplicateOwner is not null });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { keptParcelId = keptId, movedOffers = moved, withdrawnOffers = withdrawn });
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Someone added an offer or owner on the kept record at the same moment; nothing was changed.
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "This land changed while merging. Please reload and try again.");
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Records who owns the land after a trust reviewer has checked the documents. Replaces any earlier owner,
    /// which also voids the agent confirmations that owner made (see OfferEndpoints.AuthorityFor).
    /// </summary>
    private static async Task<IResult> SetOwnerOfRecordAsync(Guid parcelId, SetOwnerOfRecordRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var reviewerId = http.User.UserId();
        if (request.UserId == reviewerId)
        {
            return ApiResults.Forbidden("admin.self_review", "A reviewer cannot record themselves as the owner.");
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceNote) || request.EvidenceNote.Length > 1000)
        {
            return ApiResults.Invalid("admin.evidence_required", "Describe the documents checked (up to 1,000 characters).");
        }

        if (!await db.Parcels.AnyAsync(p => p.Id == parcelId, ct))
        {
            return ApiResults.NotFound("Parcel");
        }

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
        {
            return ApiResults.NotFound("User");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ParcelParties.Where(p => p.ParcelId == parcelId && p.Role == Vocabulary.OwnerOfRecord).ToListAsync(ct);
        db.ParcelParties.RemoveRange(current);

        var newOwner = new ParcelPartyRecord
        {
            ParcelId = parcelId,
            UserId = request.UserId,
            Role = Vocabulary.OwnerOfRecord,
            ConfirmedAt = clock.UtcNow,
            ConfirmedBy = reviewerId,
        };
        audit.Add(reviewerId, "parcel.owner_of_record_set", "parcel", parcelId.ToString(),
            new { ownerUserId = request.UserId, previous = current.Select(c => c.UserId).ToArray(), evidence = request.EvidenceNote.Trim() });
        try
        {
            // Remove the old owner first so the one-owner index never sees two; both steps share the transaction.
            await db.SaveChangesAsync(ct);
            db.ParcelParties.Add(newOwner);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "Another reviewer changed the owner at the same moment. Please reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "Another reviewer changed the owner at the same moment. Please reload and try again.");
        }

        return Results.NoContent();
    }
}
