using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

/// <summary>What the signed-in person may do on one parcel's comparison page.</summary>
public sealed record ParcelViewerResponse(bool IsOwnerOfRecord, Guid? MyOfferId, IReadOnlyList<Guid> ConfirmedOfferIds);

public sealed record AdminUserResponse(Guid Id, string PhoneMasked, string? DisplayName, IReadOnlyList<string> Checks, DateTimeOffset CreatedAt);

public sealed record DuplicateCandidateResponse(Guid ParcelId, Guid KeptParcelId, string Village, string Mandal, string District, string SurveyNumber, int ActiveOffers, DateTimeOffset FoundAt);

/// <summary>
/// Trust workflows that need a screen: the owner confirming agents from the comparison page, and the
/// reviewer console (finding a person by mobile number, the duplicate queue).
/// </summary>
public static class TrustEndpoints
{
    public static void MapTrustEndpoints(this IEndpointRouteBuilder app)
    {
        var parcel = app.MapGroup("/api/v1/parcels/{parcelId:guid}").WithTags("Owner").RequireAuthorization();
        parcel.MapGet("/viewer", ViewerAsync);
        parcel.MapDelete("/offers/{offerId:guid}/confirm-lister", RevokeListerOfOfferAsync);

        var admin = app.MapGroup("/api/v1/admin").WithTags("Admin")
            .RequireAuthorization(AdminEndpoints.TrustReviewerPolicy)
            .AddEndpointFilter(AdminEndpoints.RequireCurrentReviewerAsync);
        admin.MapGet("/users/by-phone", FindUserByPhoneAsync);
        admin.MapGet("/duplicates", DuplicatesAsync);
    }

    private static async Task<IResult> ViewerAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        if (!await db.Parcels.AnyAsync(p => p.Id == parcelId, ct))
        {
            return ApiResults.NotFound("Parcel");
        }

        var isOwner = await db.ParcelParties.AnyAsync(p => p.ParcelId == parcelId && p.UserId == userId && p.Role == Vocabulary.OwnerOfRecord, ct);
        var myOffer = await db.Offers.AsNoTracking()
            .Where(o => o.ParcelId == parcelId && o.ListerUserId == userId && o.Status == "active")
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);

        // Only the owner learns which offers they have confirmed; nobody else needs to see that list.
        IReadOnlyList<Guid> confirmed = [];
        if (isOwner)
        {
            var agents = db.ParcelParties
                .Where(p => p.ParcelId == parcelId && p.Role == Vocabulary.AuthorisedAgent && p.ConfirmedBy == userId)
                .Select(p => p.UserId);
            confirmed = await db.Offers.AsNoTracking()
                .Where(o => o.ParcelId == parcelId && o.Status == "active" && agents.Contains(o.ListerUserId))
                .Select(o => o.Id)
                .ToListAsync(ct);
        }

        return Results.Ok(new ParcelViewerResponse(isOwner, myOffer, confirmed));
    }

    /// <summary>The owner removes confirmation from whoever made an offer, without needing their account id.</summary>
    private static async Task<IResult> RevokeListerOfOfferAsync(Guid parcelId, Guid offerId, HttpContext http, BhoomisetuDbContext db, AuditWriter audit, CancellationToken ct)
    {
        var listerId = await db.Offers.AsNoTracking()
            .Where(o => o.Id == offerId && o.ParcelId == parcelId)
            .Select(o => (Guid?)o.ListerUserId)
            .FirstOrDefaultAsync(ct);
        return listerId is { } id
            ? await OfferEndpoints.RevokeAgentAsync(parcelId, id, http, db, audit, ct)
            : ApiResults.NotFound("Offer");
    }

    /// <summary>
    /// Lets a reviewer find the account behind a mobile number, to record them as owner after checking
    /// documents. Every lookup is audited because it links a phone number to an account.
    /// </summary>
    private static async Task<IResult> FindUserByPhoneAsync(string? phone, HttpContext http, BhoomisetuDbContext db, AuditWriter audit, CancellationToken ct)
    {
        var parsed = IndianMobileNumber.Parse(phone);
        if (!parsed.IsSuccess)
        {
            return ApiResults.Invalid(parsed.Error!);
        }

        var e164 = parsed.Value.E164;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneE164 == e164, ct);
        audit.Add(http.User.UserId(), "admin.user_lookup", "user", user?.Id.ToString() ?? "none", new { phone = parsed.Value.Masked, found = user is not null });
        await db.SaveChangesAsync(ct);
        if (user is null)
        {
            return ApiResults.NotFound("Account for this number");
        }

        var checks = await db.UserChecks.AsNoTracking().Where(c => c.UserId == user.Id).Select(c => c.CheckType).OrderBy(c => c).ToListAsync(ct);
        return Results.Ok(new AdminUserResponse(user.Id, parsed.Value.Masked, user.DisplayName, checks, user.CreatedAt));
    }

    private static async Task<IResult> DuplicatesAsync(BhoomisetuDbContext db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<DuplicateRow>(
            $"""
            SELECT d.parcel_id AS "ParcelId", d.kept_parcel_id AS "KeptParcelId", p.village AS "Village", p.mandal AS "Mandal",
                   p.district AS "District", p.survey_number AS "SurveyNumber", d.found_at AS "FoundAt",
                   (SELECT count(*)::int FROM offers o WHERE o.parcel_id = d.parcel_id AND o.status = 'active') AS "ActiveOffers"
            FROM parcel_duplicate_candidates d JOIN parcels p ON p.id = d.parcel_id
            WHERE d.resolved_at IS NULL
            ORDER BY d.found_at
            LIMIT 200
            """).ToListAsync(ct);
        return Results.Ok(rows.Select(r => new DuplicateCandidateResponse(r.ParcelId, r.KeptParcelId, r.Village, r.Mandal, r.District, r.SurveyNumber, r.ActiveOffers, r.FoundAt)).ToList());
    }

    internal sealed class DuplicateRow
    {
        public Guid ParcelId { get; set; }
        public Guid KeptParcelId { get; set; }
        public string Village { get; set; } = string.Empty;
        public string Mandal { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string SurveyNumber { get; set; } = string.Empty;
        public int ActiveOffers { get; set; }
        public DateTimeOffset FoundAt { get; set; }
    }
}
