using System.Runtime.CompilerServices;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.Modules.Offers;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

/// <summary>One pinned piece of land on the map. DistanceKm is set for "land nearby".</summary>
public sealed record MapParcelResponse(Guid Id, double Latitude, double Longitude, string Village, string Mandal, string District, string SurveyNumber,
    string LandClass, decimal AreaSquareYards, int ActiveOffers, decimal? LowestPriceRupees, double? DistanceKm, DateTimeOffset CreatedAt);

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

    public decimal? RoadWidthM { get; set; }

    public int? DistanceToMainRoadM { get; set; }

    public bool OwnerRecorded { get; set; }

    public int ReviewedDocuments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One piece of land in a "compare with land nearby" table, with prices per square yard and per gunta.</summary>
public sealed record ComparedLandResponse(Guid Id, string Village, string Mandal, string District, string SurveyNumber, string LandClass,
    decimal AreaSquareYards, decimal AreaGuntas, int ActiveOffers, decimal? LowestPriceRupees, decimal? PricePerSquareYard, decimal? PricePerGunta,
    decimal? RoadWidthM, int? DistanceToMainRoadM, bool OwnerRecorded, int ReviewedDocuments, double? DistanceKm, IReadOnlyList<ComparisonPointResponse> Points);

public sealed record ComparisonPointResponse(string Code, string Kind, IReadOnlyDictionary<string, string> Values);

public sealed record LandComparisonResponse(ComparedLandResponse Subject, IReadOnlyList<ComparedLandResponse> Items);

/// <summary>
/// Map search: every pinned piece of land inside the visible area, and land near one parcel. Uses the PostGIS
/// point (parcels.centroid) and its GiST index; hidden duplicates never appear.
/// </summary>
public static class MapEndpoints
{
    internal const int MaxPins = 500;

    /// <summary>"No date filter": earlier than any listing.</summary>
    internal static readonly DateTimeOffset AllTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal const double NearbyRadiusKm = 10;
    internal const int MaxNearby = 8;

    public static void MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parcels").WithTags("Map");
        group.MapGet("/map", InAreaAsync).RequireRateLimiting(RateLimitPolicies.Map);
        group.MapGet("/{id:guid}/nearby", NearbyAsync);
    }

    private static async Task<IResult> InAreaAsync(double? south, double? west, double? north, double? east, string? landClass, int? newDays, BhoomisetuDbContext db, BestValueRanker ranker, IClock clock, CancellationToken ct)
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

        // A listing's date is its newest active price (a fresh offer on old land is new), else when the land was added.
        // Fixed SQL text; the optional filters only add fixed clauses, and every value is a parameter.
        var since = newDays is >= 1 and <= 90 ? clock.UtcNow.AddDays(-newDays.Value) : AllTime;
        var sql = FormattableStringFactory.Create(
            """
            SELECT p.id AS "Id", ST_Y(p.centroid::geometry) AS "Latitude", ST_X(p.centroid::geometry) AS "Longitude",
                   p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                   p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                   (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                   NULL::double precision AS "DistanceKm", p.road_width_m AS "RoadWidthM", p.distance_to_main_road_m AS "DistanceToMainRoadM",
                   false AS "OwnerRecorded", 0 AS "ReviewedDocuments", COALESCE((SELECT max(o.created_at) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active'), p.created_at) AS "CreatedAt"
            FROM parcels p
            WHERE p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate
              AND p.centroid && ST_MakeEnvelope({0}, {1}, {2}, {3}, 4326)::geography
              AND COALESCE((SELECT max(o.created_at) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active'), p.created_at) >= {5}
            """ + (landClass is null ? string.Empty : " AND p.land_class = {4}") + """

            ORDER BY "CreatedAt" DESC, p.id
            LIMIT {6}
            """, w, s, e, n, landClass ?? string.Empty, since, MaxPins + 1);
        var rows = await db.Database.SqlQuery<MapRow>(sql).ToListAsync(ct);

        var truncated = rows.Count > MaxPins;
        return Results.Ok(new MapResponse(await ToResponseAsync(rows.Take(MaxPins).ToList(), db, ranker, ct), truncated));
    }

    /// <summary>
    /// "Compare with land nearby": this land and up to 8 pinned pieces within 10 km, with price per square yard and
    /// per gunta, and pros and cons from <see cref="NearbyComparison"/>. Empty items when this land has no pin.
    /// </summary>
    private static async Task<IResult> NearbyAsync(Guid id, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        var subject = await db.Database.SqlQuery<MapRow>(
            $"""
            SELECT p.id AS "Id", COALESCE(ST_Y(p.centroid::geometry), 0) AS "Latitude", COALESCE(ST_X(p.centroid::geometry), 0) AS "Longitude",
                   p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                   p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                   (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                   NULL::double precision AS "DistanceKm", p.road_width_m AS "RoadWidthM", p.distance_to_main_road_m AS "DistanceToMainRoadM",
                   EXISTS (SELECT 1 FROM parcel_parties pp WHERE pp.parcel_id = p.id AND pp.role = 'owner_of_record') AS "OwnerRecorded",
                   (SELECT count(*) FROM parcel_documents d WHERE d.parcel_id = p.id AND d.status = 'approved')::int AS "ReviewedDocuments",
                   COALESCE((SELECT max(o.created_at) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active'), p.created_at) AS "CreatedAt"
            FROM parcels p WHERE p.id = {id}
            """).ToListAsync(ct);
        if (subject.Count == 0)
        {
            return ApiResults.NotFound("Parcel");
        }

        var rows = await db.Database.SqlQuery<MapRow>(
            $"""
            SELECT p.id AS "Id", ST_Y(p.centroid::geometry) AS "Latitude", ST_X(p.centroid::geometry) AS "Longitude",
                   p.village AS "Village", p.mandal AS "Mandal", p.district AS "District", p.survey_number AS "SurveyNumber",
                   p.land_class AS "LandClass", p.recorded_area_sqm AS "RecordedAreaSqm",
                   (SELECT count(*) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')::int AS "ActiveOffers",
                   ST_Distance(p.centroid, me.centroid) / 1000.0 AS "DistanceKm", p.road_width_m AS "RoadWidthM", p.distance_to_main_road_m AS "DistanceToMainRoadM",
                   EXISTS (SELECT 1 FROM parcel_parties pp WHERE pp.parcel_id = p.id AND pp.role = 'owner_of_record') AS "OwnerRecorded",
                   (SELECT count(*) FROM parcel_documents d WHERE d.parcel_id = p.id AND d.status = 'approved')::int AS "ReviewedDocuments",
                   COALESCE((SELECT max(o.created_at) FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active'), p.created_at) AS "CreatedAt"
            FROM parcels me
            JOIN parcels p ON p.id <> me.id AND p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate
                          AND ST_DWithin(p.centroid, me.centroid, {NearbyRadiusKm * 1000})
            WHERE me.id = {id} AND me.centroid IS NOT NULL
            ORDER BY p.centroid <-> me.centroid
            LIMIT {MaxNearby}
            """).ToListAsync(ct);

        var lowest = await ParcelEndpoints.LowestPricesAsync(db, ranker, rows.Select(r => r.Id).Append(id).ToList(), ct);
        var ours = ToCompared(subject[0], lowest, null);
        var ourPlace = Comparable(ours);
        return Results.Ok(new LandComparisonResponse(ours, rows.Select(r => ToCompared(r, lowest, ourPlace)).ToList()));
    }

    private static ComparedLandResponse ToCompared(MapRow r, Dictionary<Guid, long?> lowest, ComparablePlace? subject)
    {
        var squareYards = r.RecordedAreaSqm / Area.FactorToSquareMetres(AreaUnit.SquareYard);
        var guntas = r.RecordedAreaSqm / Area.FactorToSquareMetres(AreaUnit.Gunta);
        decimal? price = lowest.GetValueOrDefault(r.Id) is { } paise ? paise / 100m : null;
        var compared = new ComparedLandResponse(r.Id, r.Village, r.Mandal, r.District, r.SurveyNumber, r.LandClass,
            Math.Round(squareYards, 2, MidpointRounding.AwayFromZero), Math.Round(guntas, 2, MidpointRounding.AwayFromZero), r.ActiveOffers, price,
            price is { } p1 && squareYards > 0 ? Math.Round(p1 / squareYards, 0, MidpointRounding.AwayFromZero) : null,
            price is { } p2 && guntas > 0 ? Math.Round(p2 / guntas, 0, MidpointRounding.AwayFromZero) : null,
            r.RoadWidthM, r.DistanceToMainRoadM, r.OwnerRecorded, r.ReviewedDocuments,
            r.DistanceKm is { } km ? Math.Round(km, 2) : null, []);
        return subject is null ? compared : compared with { Points = Points(NearbyComparison.Compare(subject, Comparable(compared))) };
    }

    private static ComparablePlace Comparable(ComparedLandResponse land) =>
        new(land.PricePerSquareYard, land.AreaSquareYards, land.LandClass, land.RoadWidthM, land.DistanceToMainRoadM, land.OwnerRecorded, land.ReviewedDocuments);

    internal static IReadOnlyList<ComparisonPointResponse> Points(IReadOnlyList<ComparisonPoint> points) =>
        points.Select(p => new ComparisonPointResponse(p.Code, p.Kind.ToString().ToLowerInvariant(), p.Values)).ToList();

    private static async Task<List<MapParcelResponse>> ToResponseAsync(List<MapRow> rows, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        var lowest = await ParcelEndpoints.LowestPricesAsync(db, ranker, rows.Select(r => r.Id).ToList(), ct);
        var squareYard = Area.FactorToSquareMetres(AreaUnit.SquareYard);
        return rows.Select(r => new MapParcelResponse(r.Id, r.Latitude, r.Longitude, r.Village, r.Mandal, r.District, r.SurveyNumber, r.LandClass,
            Math.Round(r.RecordedAreaSqm / squareYard, 2, MidpointRounding.AwayFromZero), r.ActiveOffers,
            lowest.GetValueOrDefault(r.Id) is { } paise ? paise / 100m : null,
            r.DistanceKm is { } km ? Math.Round(km, 2) : null, r.CreatedAt)).ToList();
    }
}
