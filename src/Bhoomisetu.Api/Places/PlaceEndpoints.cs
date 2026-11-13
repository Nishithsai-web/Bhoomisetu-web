using System.Runtime.CompilerServices;
using Bhoomisetu.Api.Endpoints;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Places;

internal sealed class PlaceRow
{
    public string Name { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Listings { get; set; }

    public string Level { get; set; } = string.Empty;

    public double South { get; set; }

    public double North { get; set; }

    public double West { get; set; }

    public double East { get; set; }
}

/// <summary>
/// "Where do you want to look?" on the map. Places with Bhoomisetu listings come first, matched in any script
/// (PlaceKey); other places (towns, landmarks) come from OpenStreetMap, so people can explore anywhere in India.
/// </summary>
public static class PlaceEndpoints
{
    internal const int MaxOwn = 6;

    public static void MapPlaceEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/places", SearchAsync).WithTags("Map").RequireRateLimiting(RateLimitPolicies.Map);

    private static async Task<IResult> SearchAsync(string? q, string? lang, BhoomisetuDbContext db, IExternalGeocoder geocoder, CancellationToken ct)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length is < 2 or > 100)
        {
            return ApiResults.Invalid("place.query_invalid", "Type at least 2 letters of a place name.");
        }

        var language = lang is "te" or "hi" ? lang : "en";
        var pattern = "%" + query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var key = PlaceKey.From(query);

        // Fixed SQL text with every value as a parameter. The typed key and its first-vowel variants (Kurnool /
        // కర్నూలు): a long one may be contained in a stored key, a short one must equal it (LIKE with no wildcard).
        var variants = PlaceKey.SearchVariants(key);
        var v = variants.Select(k => k.Length >= 5 ? "%" + k + "%" : k.Length > 0 ? k : "-").ToArray();
        var exactKeys = variants.Select(k => k.Length > 0 ? k : "-").ToArray();
        // Exact name (or key) matches first, at any level, so "Ranga" does not beat a village called Ranga.
        // Then one row per matching district, mandal and village, in that order, each with the area its pinned land
        // covers, so a mandal search shows the whole mandal and not just its busiest village. The same name
        // matches at each level; the typed text and key variants are the only parameters.
        const string Match = "({0} OR {1} OR {2} OR {3})";
        static string For(string column) =>
            Match.Replace("{0}", $"p.{column} ILIKE {{0}}", StringComparison.Ordinal)
                 .Replace("{1}", $"p.{column}_key LIKE {{1}}", StringComparison.Ordinal)
                 .Replace("{2}", $"p.{column}_key LIKE {{2}}", StringComparison.Ordinal)
                 .Replace("{3}", $"p.{column}_key LIKE {{3}}", StringComparison.Ordinal);
        const string Columns = """
            avg(ST_Y(p.centroid::geometry)) AS "Latitude", avg(ST_X(p.centroid::geometry)) AS "Longitude",
            min(ST_Y(p.centroid::geometry)) AS "South", max(ST_Y(p.centroid::geometry)) AS "North",
            min(ST_X(p.centroid::geometry)) AS "West", max(ST_X(p.centroid::geometry)) AS "East",
            (count(*) FILTER (WHERE EXISTS (SELECT 1 FROM offers o WHERE o.parcel_id = p.id AND o.status = 'active')))::int AS "Listings"
            """;
        const string Pinned = "p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate";
        var sql = FormattableStringFactory.Create(
            "SELECT \"Name\", \"Detail\", \"Level\", \"Latitude\", \"Longitude\", \"South\", \"North\", \"West\", \"East\", \"Listings\" FROM (" +
            // exact: 2 = the typed name or typed key, 1 = a first-vowel variant of it, 0 = neither. The typed spelling
            // always beats a variant, so "Kurnool" does not jump above Kurnool village for a sound-alike district.
            $"SELECT p.district AS \"Name\", p.state_code AS \"Detail\", 'district' AS \"Level\", 0 AS rank, max(CASE WHEN lower(p.district) = lower({{5}}) OR p.district_key = {{6}} THEN 2 WHEN p.district_key IN ({{7}}, {{8}}, {{9}}) THEN 1 ELSE 0 END) AS exact, {Columns} FROM parcels p WHERE {Pinned} AND {For("district")} GROUP BY p.district, p.state_code " +
            "UNION ALL " +
            $"SELECT p.mandal, p.district, 'mandal', 1, max(CASE WHEN lower(p.mandal) = lower({{5}}) OR p.mandal_key = {{6}} THEN 2 WHEN p.mandal_key IN ({{7}}, {{8}}, {{9}}) THEN 1 ELSE 0 END), {Columns} FROM parcels p WHERE {Pinned} AND {For("mandal")} GROUP BY p.mandal, p.district " +
            "UNION ALL " +
            $"SELECT p.village, p.mandal || ', ' || p.district, 'village', 2, max(CASE WHEN lower(p.village) = lower({{5}}) OR p.village_key = {{6}} THEN 2 WHEN p.village_key IN ({{7}}, {{8}}, {{9}}) THEN 1 ELSE 0 END), {Columns} FROM parcels p WHERE {Pinned} AND {For("village")} GROUP BY p.village, p.mandal, p.district" +
            ") places ORDER BY exact DESC, rank, \"Listings\" DESC, \"Name\" LIMIT {4}",
            pattern, v[0], v[1], v[2], MaxOwn, query, key.Length > 0 ? key : "-", exactKeys[0], exactKeys[1], exactKeys[2]);

        var own = (await db.Database.SqlQuery<PlaceRow>(sql).ToListAsync(ct))
            .Select(r => new PlaceResult(r.Name, r.Detail, Math.Round(r.Latitude, 4), Math.Round(r.Longitude, 4), r.Listings, "bhoomisetu",
                [Math.Round(r.South, 4), Math.Round(r.North, 4), Math.Round(r.West, 4), Math.Round(r.East, 4)], r.Level))
            .ToList();

        // OpenStreetMap places we already show (same sound-alike name) are left out.
        // Compared through first-vowel variants too, so Kurnool (OpenStreetMap) and our Kurnool village match.
        var ownKeys = own.SelectMany(o => PlaceKey.SearchVariants(PlaceKey.From(o.Name))).ToHashSet(StringComparer.Ordinal);
        var external = own.Count >= MaxOwn ? [] : (await geocoder.SearchAsync(query, language, ct))
            .Where(x => !PlaceKey.SearchVariants(PlaceKey.From(x.Name.Split(',')[0])).Any(ownKeys.Contains))
            // The same place can come back twice from OpenStreetMap (a node and a relation): one row per name and spot.
            .DistinctBy(x => (PlaceKey.From(x.Name.Split(',')[0]), Math.Round(x.Latitude, 3), Math.Round(x.Longitude, 3)));
        return Results.Ok(own.Concat(external.Take(MaxOwn - own.Count)).ToList());
    }
}
