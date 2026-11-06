using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

public sealed record SetOwnerOfRecordRequest(Guid UserId, string? EvidenceNote);

public static class AdminEndpoints
{
    public const string TrustReviewerPolicy = "TrustReviewer";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization(TrustReviewerPolicy);
        group.MapPost("/parcels/{parcelId:guid}/owner-of-record", SetOwnerOfRecordAsync);
    }

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
        await db.SaveChangesAsync(ct);

        db.ParcelParties.Add(new ParcelPartyRecord
        {
            ParcelId = parcelId,
            UserId = request.UserId,
            Role = Vocabulary.OwnerOfRecord,
            ConfirmedAt = clock.UtcNow,
            ConfirmedBy = reviewerId,
        });
        audit.Add(reviewerId, "parcel.owner_of_record_set", "parcel", parcelId.ToString(),
            new { ownerUserId = request.UserId, previous = current.Select(c => c.UserId).ToArray(), evidence = request.EvidenceNote.Trim() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
