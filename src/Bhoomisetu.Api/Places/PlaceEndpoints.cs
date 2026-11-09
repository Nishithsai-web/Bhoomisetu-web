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

        // Fixed SQL text with every value as a parameter; the key match follows the search rules
        // (short keys whole-name only, longer keys may be contained).
        var keyClause = key.Length >= 5
            ? "p.village_key LIKE {1} OR p.mandal_key LIKE {1} OR p.district_key LIKE {1}"
            : "p.village_key = {2} OR p.mandal_key = {2} OR p.district_key = {2}";
        var sql = FormattableStringFactory.Create(
            """
            SELECT p.village AS "Name", p.mandal || ', ' || p.district AS "Detail",
                   avg(ST_Y(p.centroid::geometry)) AS "Latitude", avg(ST_X(p.centroid::geometry)) AS "Longitude", count(*)::int AS "Listings"
            FROM parcels p
            WHERE p.centroid IS NOT NULL AND NOT p.is_hidden_duplicate
              AND (p.village ILIKE {0} OR p.mandal ILIKE {0} OR p.district ILIKE {0} OR
            """ + keyClause + """
            )
            GROUP BY p.village, p.mandal, p.district
            ORDER BY count(*) DESC, p.village
            LIMIT {3}
            """, pattern, "%" + key + "%", key.Length > 0 ? key : "-", MaxOwn);
        var own = (await db.Database.SqlQuery<PlaceRow>(sql).ToListAsync(ct))
            .Select(r => new PlaceResult(r.Name, r.Detail, Math.Round(r.Latitude, 4), Math.Round(r.Longitude, 4), r.Listings, "bhoomisetu"))
            .ToList();

        var external = own.Count >= MaxOwn ? [] : await geocoder.SearchAsync(query, language, ct);
        return Results.Ok(own.Concat(external.Take(MaxOwn - own.Count)).ToList());
    }
}
