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
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "Another reviewer changed the owner at the same moment. Please reload and try again.");
        }

        return Results.NoContent();
    }
}
