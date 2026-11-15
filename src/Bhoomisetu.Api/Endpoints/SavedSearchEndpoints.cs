using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

public sealed record CreateSavedSearchRequest(string? PlaceText, string? LandClass);

/// <summary>A saved place search. NewCount is the land listed there since the buyer last looked.</summary>
public sealed record SavedSearchResponse(Guid Id, string PlaceText, string? LandClass, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, int NewCount);

public static class SavedSearchEndpoints
{
    /// <summary>A buyer keeps a short list; a long one makes the page slow and the count meaningless.</summary>
    private const int MaxPerUser = 10;

    public static void MapSavedSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/saved-searches").WithTags("SavedSearches").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/{id:guid}/seen", SeenAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext http, BhoomisetuDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var searches = await db.SavedSearches.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        var responses = new List<SavedSearchResponse>(searches.Count);
        foreach (var search in searches)
        {
            responses.Add(await ToResponseAsync(db, search, ct));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> CreateAsync(CreateSavedSearchRequest r, HttpContext http, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var text = (r.PlaceText ?? string.Empty).Trim();
        if (text.Length is < 2 or > 80)
        {
            return ApiResults.Invalid("saved_search.place_invalid", "Type a village, mandal or district to save the search.");
        }

        string? landClass = null;
        if (!string.IsNullOrWhiteSpace(r.LandClass))
        {
            if (!Vocabulary.LandClasses.Contains(r.LandClass))
            {
                return ApiResults.Invalid("saved_search.land_class_invalid", "Choose a land class, or leave it empty for all land.");
            }

            landClass = r.LandClass;
        }

        var existing = await db.SavedSearches.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.PlaceText.ToLower() == text.ToLower() && s.LandClass == landClass, ct);
        if (existing is not null)
        {
            return Results.Ok(await ToResponseAsync(db, existing, ct));
        }

        var count = await db.SavedSearches.CountAsync(s => s.UserId == userId, ct);
        if (count >= MaxPerUser)
        {
            return ApiResults.Invalid("saved_search.limit", "You can keep up to 10 saved searches. Remove one to save another.");
        }

        var now = clock.UtcNow;
        var record = new SavedSearchRecord { Id = Guid.NewGuid(), UserId = userId, PlaceText = text, LandClass = landClass, CreatedAt = now, LastSeenAt = now };
        db.SavedSearches.Add(record);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(db, record, ct));
    }

    private static async Task<IResult> SeenAsync(Guid id, HttpContext http, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var search = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (search is null)
        {
            return ApiResults.NotFound("Saved search");
        }

        search.LastSeenAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, BhoomisetuDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var removed = await db.SavedSearches.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
        return removed == 0 ? ApiResults.NotFound("Saved search") : Results.NoContent();
    }

    private static async Task<SavedSearchResponse> ToResponseAsync(BhoomisetuDbContext db, SavedSearchRecord search, CancellationToken ct)
    {
        var newCount = await NewMatchesAsync(db, search, ct);
        return new SavedSearchResponse(search.Id, search.PlaceText, search.LandClass, search.CreatedAt, search.LastSeenAt, newCount);
    }

    /// <summary>Land listed in the place since the buyer last looked. The place matches by name or sound-alike key, as the search does.</summary>
    private static async Task<int> NewMatchesAsync(BhoomisetuDbContext db, SavedSearchRecord search, CancellationToken ct)
    {
        var query = db.Parcels.AsNoTracking().Where(p => !p.IsHiddenDuplicate && p.CreatedAt > search.LastSeenAt);
        query = MatchPlace(query, search.PlaceText);
        if (search.LandClass is not null)
        {
            query = query.Where(p => p.LandClass == search.LandClass);
        }

        return await query.CountAsync(ct);
    }

    private static IQueryable<ParcelRecord> MatchPlace(IQueryable<ParcelRecord> query, string text)
    {
        var pattern = "%" + Escape(text) + "%";
        var key = PlaceKey.From(text);
        var variants = PlaceKey.SearchVariants(key).Select(k => k.Length >= 5 ? "%" + Escape(k) + "%" : k.Length > 0 ? k : "-").ToArray();
        var (v0, v1, v2) = (variants[0], variants[1], variants[2]);
        return query.Where(p => EF.Functions.ILike(p.Village, pattern) || EF.Functions.ILike(p.Mandal, pattern) || EF.Functions.ILike(p.District, pattern)
            || EF.Functions.Like(p.VillageKey!, v0) || EF.Functions.Like(p.MandalKey!, v0) || EF.Functions.Like(p.DistrictKey!, v0)
            || EF.Functions.Like(p.VillageKey!, v1) || EF.Functions.Like(p.MandalKey!, v1) || EF.Functions.Like(p.DistrictKey!, v1)
            || EF.Functions.Like(p.VillageKey!, v2) || EF.Functions.Like(p.MandalKey!, v2) || EF.Functions.Like(p.DistrictKey!, v2));
    }

    /// <summary>Escapes the user's own % and _ so they match literally inside a LIKE pattern.</summary>
    private static string Escape(string term) =>
        term.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
