using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.Modules.Offers;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

/// <summary>One pinned piece of land on the map. DistanceKm is set for "land nearby".</summary>
public sealed record MapParcelResponse(Guid Id, double Latitude, double Longitude, string Village, string Mandal, string District, string SurveyNumber,
    string LandClass, decimal AreaSquareYards, int ActiveOffers, decimal? LowestPriceRupees, double? DistanceKm);

public sealed record MapResponse(IReadOnlyList<MapParcelResponse> Items, bool Truncated);

/// <summary>Row shape for the PostGIS queries.</summary>
internal sealed class MapRow
{
    public Guid Id { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string Village { get; set; } = string.Empty;

    public string Mandal { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    public string SurveyNumber { get; set; } = string.Empty;

    public string LandClass { get; set; } = string.Empty;

    public decimal RecordedAreaSqm { get; set; }

    public int ActiveOffers { get; set; }

    public double? DistanceKm { get; set; }
}

/// <summary>
/// Map search: every pinned piece of land inside the visible area, and land near one parcel. Uses the PostGIS
/// point (parcels.centroid) and its GiST index; hidden duplicates never appear.
/// </summary>
public static class MapEndpoints
{
    internal const int MaxPins = 500;
    internal const double NearbyRadiusKm = 10;
    internal const int MaxNearby = 8;

    public static void MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parcels").WithTags("Map");
        group.MapGet("/map", InAreaAsync);
        group.MapGet("/{id:guid}/nearby", NearbyAsync);
    }

    private static async Task<IResult> InAreaAsync(double? south, double? west, double? north, double? east, string? landClass, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        // A box over India (with a margin); anything else is a mistake or someone scraping.
        if (south is not { } s || west is not { } w || north is not { } n || east is not { } e
            || double.IsNaN(s) || double.IsNaN(w) || double.IsNaN(n) || double.IsNaN(e)
            || s >= n || w >= e || s < 0 || n > 40 || w < 60 || e > 100)
        {
            return ApiResults.Invalid("map.area_invalid", "The map area is not valid.");
        }

        if (landClass is not null && !Vocabulary.LandClasses.Contains(landClass))
        {
            return ApiResults.Invalid("parcel.land_class_invalid", "Land class must be agricultural, non_agricultural, converted or unknown.");
        }

        var rows = landClass is null
            ? await db.Database.SqlQuery<MapRow>(
                $"""
                SELECT p.id AS "Id", ST_Y(p.centroid::geometry) AS "Latitude", ST_X(p.centroid::geometry) AS "Longitude",
                       p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                       p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                       (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                       NULL::double precision AS "DistanceKm"
                FROM parcels p
                WHERE p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate
                  AND p.centroid && ST_MakeEnvelope({w}, {s}, {e}, {n}, 4326)::geography
                ORDER BY p.created_at DESC, p.id
                LIMIT {MaxPins + 1}
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<MapRow>(
                $"""
                SELECT p.id AS "Id", ST_Y(p.centroid::geometry) AS "Latitude", ST_X(p.centroid::geometry) AS "Longitude",
                       p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                       p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                       (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                       NULL::double precision AS "DistanceKm"
                FROM parcels p
                WHERE p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate AND p.land_class = {landClass}
                  AND p.centroid && ST_MakeEnvelope({w}, {s}, {e}, {n}, 4326)::geography
                ORDER BY p.created_at DESC, p.id
                LIMIT {MaxPins + 1}
                """).ToListAsync(ct);

        var truncated = rows.Count > MaxPins;
        return Results.Ok(new MapResponse(await ToResponseAsync(rows.Take(MaxPins).ToList(), db, ranker, ct), truncated));
    }

    private static async Task<IResult> NearbyAsync(Guid id, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        if (!await db.Parcels.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Parcel");
        }

        var rows = await db.Database.SqlQuery<MapRow>(
            $"""
            SELECT p.id AS "Id", ST_Y(p.centroid::geometry) AS "Latitude", ST_X(p.centroid::geometry) AS "Longitude",
                   p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                   p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                   (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                   ST_Distance(p.centroid, me.centroid) / 1000.0 AS "DistanceKm"
            FROM parcels me
            JOIN parcels p ON p.id <> me.id AND p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate
                          AND ST_DWithin(p.centroid, me.centroid, {NearbyRadiusKm * 1000})
            WHERE me.id = {id} AND me.centroid IS NOT NULL
            ORDER BY p.centroid <-> me.centroid
            LIMIT {MaxNearby}
            """).ToListAsync(ct);

        return Results.Ok(await ToResponseAsync(rows, db, ranker, ct));
    }

    private static async Task<List<MapParcelResponse>> ToResponseAsync(List<MapRow> rows, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        var lowest = await ParcelEndpoints.LowestPricesAsync(db, ranker, rows.Select(r => r.Id).ToList(), ct);
        var squareYard = Area.FactorToSquareMetres(AreaUnit.SquareYard);
        return rows.Select(r => new MapParcelResponse(r.Id, r.Latitude, r.Longitude, r.Village, r.Mandal, r.District, r.SurveyNumber, r.LandClass,
            Math.Round(r.RecordedAreaSqm / squareYard, 2, MidpointRounding.AwayFromZero), r.ActiveOffers,
            lowest.GetValueOrDefault(r.Id) is { } paise ? paise / 100m : null,
            r.DistanceKm is { } km ? Math.Round(km, 2) : null)).ToList();
    }
}
