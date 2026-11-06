using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

public sealed record MyOfferResponse(
    Guid OfferId, Guid ParcelId, string Village, string Mandal, string District, string SurveyNumber, decimal AreaSquareYards,
    string ListerKind, decimal AskingPriceRupees, decimal? BuyerBrokeragePercent, string Status,
    DateTimeOffset AvailabilityConfirmedAt, int DaysSinceConfirmed, bool NeedsConfirmation);

/// <summary>
/// "My listings": everything a lister needs to keep their offers honest in one place, with one-tap actions.
/// Confirming availability keeps an offer's freshness score; stale offers drop in the comparison.
/// </summary>
public static class MyOffersEndpoints
{
    /// <summary>After this many days the page asks the lister to confirm the land is still available.</summary>
    private const int ConfirmAfterDays = 7;

    public static void MapMyOffersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/offers", MineAsync).RequireAuthorization().WithTags("My listings");
        var mine = app.MapGroup("/api/v1/parcels/{parcelId:guid}/offers/mine").WithTags("My listings").RequireAuthorization();
        mine.MapPost("/still-available", StillAvailableAsync);
        mine.MapPost("/sold", SoldAsync);
    }

    private static async Task<IResult> MineAsync(HttpContext http, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var squareYard = Area.FactorToSquareMetres(AreaUnit.SquareYard);
        var rows = await db.Offers.AsNoTracking()
            .Where(o => o.ListerUserId == userId)
            .Join(db.Parcels.AsNoTracking(), o => o.ParcelId, p => p.Id, (o, p) => new { o, p })
            .OrderBy(x => x.o.Status == "active" ? 0 : 1)
            .ThenByDescending(x => x.o.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var result = rows.Select(x =>
        {
            var days = (int)Math.Floor((now - x.o.AvailabilityConfirmedAt).TotalDays);
            return new MyOfferResponse(
                x.o.Id, x.p.Id, x.p.Village, x.p.Mandal, x.p.District, x.p.SurveyNumber,
                Math.Round(x.p.RecordedAreaSqm / squareYard, 2, MidpointRounding.AwayFromZero),
                x.o.ListerKind, x.o.AskingPricePaise / 100m, x.o.BuyerBrokeragePercent, x.o.Status,
                x.o.AvailabilityConfirmedAt, Math.Max(0, days), x.o.Status == "active" && days >= ConfirmAfterDays);
        }).ToList();
        return Results.Ok(result);
    }

    private static Task<IResult> StillAvailableAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct) =>
        UpdateMineAsync(parcelId, http, db, clock, audit, "offer.availability_confirmed", (offer, now) => offer.AvailabilityConfirmedAt = now, ct);

    private static Task<IResult> SoldAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct) =>
        UpdateMineAsync(parcelId, http, db, clock, audit, "offer.marked_sold", (offer, _) => offer.Status = "sold", ct);

    private static async Task<IResult> UpdateMineAsync(
        Guid parcelId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, string action,
        Action<OfferRecord, DateTimeOffset> change, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var offer = await db.Offers.FirstOrDefaultAsync(o => o.ParcelId == parcelId && o.ListerUserId == userId && o.Status == "active", ct);
        if (offer is null)
        {
            return ApiResults.NotFound("Your active offer");
        }

        var now = clock.UtcNow;
        change(offer, now);
        offer.UpdatedAt = now;
        audit.Add(userId, action, "offer", offer.Id.ToString(), new { parcelId });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
