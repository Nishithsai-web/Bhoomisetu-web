using System.Runtime.CompilerServices;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Documents;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Endpoints;

public sealed record CreateRentalRequest(
    string? ListerKind, string? PropertyType, int? Bedrooms, int? AreaSqft, int? RentRupees, int? DepositRupees, string? Furnishing,
    string? PreferredTenants, bool Parking, DateOnly? AvailableFrom, string? Locality, string? Mandal, string? District, double? Latitude, double? Longitude);

/// <summary>A home for rent. RentPerSqft and DepositMonths are worked out for comparison; Points only in "nearby".</summary>
public sealed record RentalResponse(Guid Id, string ListerKind, string PropertyType, int Bedrooms, int AreaSqft, int RentRupees, int DepositRupees,
    decimal RentPerSqft, decimal DepositMonths, string Furnishing, string PreferredTenants, bool Parking, DateOnly AvailableFrom,
    string Locality, string Mandal, string District, double Latitude, double Longitude, string Status, DateTimeOffset AvailabilityConfirmedAt,
    bool Contactable, bool Mine, double? DistanceKm, IReadOnlyList<ComparisonPointResponse> Points, bool Stale = false, bool Approximate = true);

public sealed record RentalComparisonResponse(RentalResponse Subject, IReadOnlyList<RentalResponse> Items);

public sealed record RentalStatusRequest(string? Action);

internal sealed class RentalRow
{
    public Guid Id { get; set; }

    public Guid ListerUserId { get; set; }

    public string ListerKind { get; set; } = string.Empty;

    public string PropertyType { get; set; } = string.Empty;

    public short Bedrooms { get; set; }

    public int AreaSqft { get; set; }

    public int RentRupees { get; set; }

    public int DepositRupees { get; set; }

    public string Furnishing { get; set; } = string.Empty;

    public string PreferredTenants { get; set; } = string.Empty;

    public bool Parking { get; set; }

    public DateOnly AvailableFrom { get; set; }

    public string Locality { get; set; } = string.Empty;

    public string Mandal { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double ExactLatitude { get; set; }

    public double ExactLongitude { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset AvailabilityConfirmedAt { get; set; }

    public double? DistanceKm { get; set; }
}

/// <summary>
/// Homes for rent: list one with a map point, find them on the map, compare rent per square foot with homes
/// nearby, and contact a subscribed lister on WhatsApp. Same contact and limit rules as land.
/// </summary>
public static class RentalEndpoints
{
    public static readonly IReadOnlySet<string> PropertyTypes = new HashSet<string>(StringComparer.Ordinal) { "apartment", "independent_house", "villa", "portion", "room" };
    public static readonly IReadOnlySet<string> Furnishings = new HashSet<string>(StringComparer.Ordinal) { "unfurnished", "semi", "full" };
    public static readonly IReadOnlySet<string> Tenants = new HashSet<string>(StringComparer.Ordinal) { "any", "family", "bachelors" };

    internal const int MaxPerUserPerDay = 10;
    internal const int MaxPins = 500;
    internal const double NearbyRadiusKm = 5;
    internal const int MaxNearby = 8;

    /// <summary>Homes not confirmed as still available for 30 days are hidden from the map and comparisons.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private const string Columns = """
        r.id AS "Id", r.lister_user_id AS "ListerUserId", r.lister_kind AS "ListerKind", r.property_type AS "PropertyType", r.bedrooms AS "Bedrooms",
        r.area_sqft AS "AreaSqft", r.rent_rupees AS "RentRupees", r.deposit_rupees AS "DepositRupees", r.furnishing AS "Furnishing",
        r.preferred_tenants AS "PreferredTenants", r.parking AS "Parking", r.available_from AS "AvailableFrom", r.locality AS "Locality",
        r.mandal AS "Mandal", r.district AS "District", ST_Y(r.public_point::geometry) AS "Latitude", ST_X(r.public_point::geometry) AS "Longitude",
        ST_Y(r.centroid::geometry) AS "ExactLatitude", ST_X(r.centroid::geometry) AS "ExactLongitude",
        r.status AS "Status", r.availability_confirmed_at AS "AvailabilityConfirmedAt"
        """;

    public static void MapRentalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rentals").WithTags("Rentals");
        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapGet("/map", InAreaAsync).RequireRateLimiting(RateLimitPolicies.Map);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/nearby", NearbyAsync);
        group.MapPost("/{id:guid}/status", StatusAsync).RequireAuthorization();
        group.MapGet("/{id:guid}/contact", ContactAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Contact);
        app.MapGet("/api/v1/me/rentals", MineAsync).WithTags("Rentals").RequireAuthorization();
        app.MapGet("/api/v1/parcels/{id:guid}/nearby-rentals", NearLandAsync).WithTags("Rentals");
    }

    private static async Task<IResult> CreateAsync(CreateRentalRequest r, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (r.ListerKind is not ("owner" or "agent"))
        {
            return ApiResults.Invalid("rental.lister_kind_invalid", "Say whether you are the owner or an agent.");
        }

        if (r.PropertyType is null || !PropertyTypes.Contains(r.PropertyType))
        {
            return ApiResults.Invalid("rental.type_invalid", "Choose the type of home.");
        }

        if (r.Bedrooms is not (>= 0 and <= 10))
        {
            return ApiResults.Invalid("rental.bedrooms_invalid", "Bedrooms must be between 0 and 10.");
        }

        if (r.AreaSqft is not (>= 50 and <= 20000))
        {
            return ApiResults.Invalid("rental.area_invalid", "Area must be between 50 and 20,000 sq ft.");
        }

        if (r.RentRupees is not (>= 500 and <= 10_000_000))
        {
            return ApiResults.Invalid("rental.rent_invalid", "Enter the monthly rent in rupees (at least ₹500).");
        }

        if (r.DepositRupees is not (>= 0 and <= 100_000_000))
        {
            return ApiResults.Invalid("rental.deposit_invalid", "Enter the deposit in rupees (0 if none).");
        }

        if (r.Furnishing is null || !Furnishings.Contains(r.Furnishing))
        {
            return ApiResults.Invalid("rental.furnishing_invalid", "Choose unfurnished, semi-furnished or fully furnished.");
        }

        if (r.PreferredTenants is null || !Tenants.Contains(r.PreferredTenants))
        {
            return ApiResults.Invalid("rental.tenants_invalid", "Choose who the home is for.");
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (r.AvailableFrom is not { } from || from < today.AddDays(-365) || from > today.AddDays(365))
        {
            return ApiResults.Invalid("rental.available_invalid", "Enter when the home is available (within a year).");
        }

        var locality = r.Locality?.Trim() ?? string.Empty;
        var mandal = r.Mandal?.Trim() ?? string.Empty;
        var district = r.District?.Trim() ?? string.Empty;
        if (locality.Length is < 2 or > 120 || mandal.Length is < 2 or > 80 || district.Length is < 2 or > 80)
        {
            return ApiResults.Invalid("rental.place_invalid", "Enter the locality, mandal and district.");
        }

        if (r.Latitude is not { } lat || r.Longitude is not { } lng || double.IsNaN(lat) || double.IsNaN(lng) || lat is < 6.0 or > 37.6 || lng is < 68.0 or > 97.5)
        {
            return ApiResults.Invalid("pin.outside_india", "The point must be inside India.");
        }

        var userId = http.User.UserId();
        var now = clock.UtcNow;
        var dayAgo = now - TimeSpan.FromDays(1);
        if (await db.Rentals.CountAsync(x => x.ListerUserId == userId && x.CreatedAt >= dayAgo, ct) >= MaxPerUserPerDay)
        {
            return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "rental.daily_limit", "You have listed many homes today. Please continue tomorrow.");
        }

        var id = Guid.NewGuid();
        lat = Math.Round(lat, 4);
        lng = Math.Round(lng, 4);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO rental_listings (id, lister_user_id, lister_kind, property_type, bedrooms, area_sqft, rent_rupees, deposit_rupees, furnishing,
                preferred_tenants, parking, available_from, locality, mandal, district, centroid, availability_confirmed_at, created_at, updated_at)
            VALUES ({id}, {userId}, {r.ListerKind}, {r.PropertyType}, {(short)r.Bedrooms.Value}, {r.AreaSqft.Value}, {r.RentRupees.Value}, {r.DepositRupees.Value},
                {r.Furnishing}, {r.PreferredTenants}, {r.Parking}, {from}, {locality}, {mandal}, {district},
                ST_SetSRID(ST_MakePoint({lng}, {lat}), 4326)::geography, {now}, {now}, {now})
            """, ct);
        audit.Add(userId, "rental.created", "rental", id.ToString(), new { r.PropertyType, r.Bedrooms, r.RentRupees, r.AreaSqft });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/rentals/{id}", new { id });
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IClock clock, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<RentalRow>(Sql("SELECT " + Columns + ", NULL::double precision AS \"DistanceKm\" FROM rental_listings r WHERE r.id = {0}", id)).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return ApiResults.NotFound("Rental");
        }

        var userId = http.User.OptionalUserId();
        var row = rows[0];
        if (row.Status != "active" && row.ListerUserId != userId)
        {
            return ApiResults.Problem(StatusCodes.Status410Gone, "rental.not_available", "This home is no longer available.");
        }

        var stale = row.Status == "active" && row.AvailabilityConfirmedAt < clock.UtcNow - StaleAfter;
        var contactable = !stale && await subscriptions.IsListerAsync(row.ListerUserId, ct);
        return Results.Ok(ToResponse(row, contactable, userId, null) with { Stale = stale });
    }

    private static async Task<IResult> InAreaAsync(double? south, double? west, double? north, double? east, int? bedrooms, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        if (south is not { } s || west is not { } w || north is not { } n || east is not { } e
            || double.IsNaN(s) || double.IsNaN(w) || double.IsNaN(n) || double.IsNaN(e) || s >= n || w >= e || s < 0 || n > 40 || w < 60 || e > 100)
        {
            return ApiResults.Invalid("map.area_invalid", "The map area is not valid.");
        }

        var fresh = clock.UtcNow - StaleAfter;
        var minBedrooms = (short)Math.Clamp(bedrooms ?? 0, 0, 10);
        var rows = await db.Database.SqlQuery<RentalRow>(Sql(
            "SELECT " + Columns + """
            , NULL::double precision AS "DistanceKm"
            FROM rental_listings r
            WHERE r.status = 'active' AND r.availability_confirmed_at >= {0} AND r.bedrooms >= {1}
              AND r.public_point && ST_MakeEnvelope({2}, {3}, {4}, {5}, 4326)::geography
            ORDER BY r.created_at DESC, r.id
            LIMIT {6}
            """, fresh, minBedrooms, w, s, e, n, MaxPins + 1)).ToListAsync(ct);
        return Results.Ok(new { items = rows.Take(MaxPins).Select(r => ToResponse(r, contactable: false, viewer: null, points: null)).ToList(), truncated = rows.Count > MaxPins });
    }

    /// <summary>"Compare with homes nearby": up to 8 active homes within 5 km, rent per sq ft, and factual pros and cons.</summary>
    private static async Task<IResult> NearbyAsync(Guid id, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        var fresh = clock.UtcNow - StaleAfter;
        var subject = await db.Database.SqlQuery<RentalRow>(Sql("SELECT " + Columns + ", NULL::double precision AS \"DistanceKm\" FROM rental_listings r WHERE r.id = {0} AND r.status = 'active'", id)).ToListAsync(ct);
        if (subject.Count == 0)
        {
            // Rented or removed homes are not shown anywhere, including as the centre of a comparison.
            return ApiResults.NotFound("Rental");
        }
        var rows = await db.Database.SqlQuery<RentalRow>(Sql(
            "SELECT " + Columns + """
            , ST_Distance(r.public_point, me.public_point) / 1000.0 AS "DistanceKm"
            FROM rental_listings me
            JOIN rental_listings r ON r.id <> me.id AND r.status = 'active' AND r.availability_confirmed_at >= {0}
                                  AND ST_DWithin(r.public_point, me.public_point, {1})
            WHERE me.id = {2}
            ORDER BY r.public_point <-> me.public_point
            LIMIT {3}
            """, fresh, NearbyRadiusKm * 1000, id, MaxNearby)).ToListAsync(ct);

        var ours = ToResponse(subject[0], contactable: false, viewer: null, points: null);
        var ourPlace = Comparable(ours);
        return Results.Ok(new RentalComparisonResponse(ours,
            rows.Select(r =>
            {
                var item = ToResponse(r, contactable: false, viewer: null, points: null);
                return item with { Points = MapEndpoints.Points(NearbyComparison.Compare(ourPlace, Comparable(item))) };
            }).ToList()));
    }

    /// <summary>Homes for rent within 5 km of a piece of land: what renting nearby costs, for a buyer weighing the land.</summary>
    private static async Task<IResult> NearLandAsync(Guid id, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        if (!await db.Parcels.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Parcel");
        }

        var fresh = clock.UtcNow - StaleAfter;
        var rows = await db.Database.SqlQuery<RentalRow>(Sql(
            "SELECT " + Columns + """
            , ST_Distance(r.public_point, p.centroid) / 1000.0 AS "DistanceKm"
            FROM parcels p
            JOIN rental_listings r ON r.status = 'active' AND r.availability_confirmed_at >= {0}
                                  AND ST_DWithin(r.public_point, p.centroid, {1})
            WHERE p.id = {2} AND p.centroid IS NOT NULL
            ORDER BY r.public_point <-> p.centroid
            LIMIT {3}
            """, fresh, NearbyRadiusKm * 1000, id, MaxNearby)).ToListAsync(ct);
        return Results.Ok(rows.Select(r => ToResponse(r, contactable: false, viewer: null, points: null)).ToList());
    }

    private static async Task<IResult> StatusAsync(Guid id, RentalStatusRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id && r.ListerUserId == userId, ct);
        if (rental is null)
        {
            return ApiResults.NotFound("Rental");
        }

        var now = clock.UtcNow;
        switch (request.Action)
        {
            case "still_available" when rental.Status == "active":
                rental.AvailabilityConfirmedAt = now;
                break;
            case "rented" when rental.Status == "active":
                rental.Status = "rented";
                break;
            case "withdraw" when rental.Status == "active":
                rental.Status = "withdrawn";
                break;
            case "relist" when rental.Status is "rented" or "withdrawn":
                rental.Status = "active";
                rental.AvailabilityConfirmedAt = now;
                break;
            default:
                return ApiResults.Invalid("rental.action_invalid", "That change is not possible for this home now.");
        }

        rental.UpdatedAt = now;
        audit.Add(userId, $"rental.{request.Action}", "rental", id.ToString(), null);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MineAsync(HttpContext http, BhoomisetuDbContext db, IClock clock, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var rows = await db.Database.SqlQuery<RentalRow>(
            Sql("SELECT " + Columns + ", NULL::double precision AS \"DistanceKm\" FROM rental_listings r WHERE r.lister_user_id = {0} ORDER BY r.created_at DESC LIMIT 100", userId))
            .ToListAsync(ct);
        var fresh = clock.UtcNow - StaleAfter;
        return Results.Ok(rows.Select(r => ToResponse(r, contactable: false, viewer: userId, points: null) with { Stale = r.Status == "active" && r.AvailabilityConfirmedAt < fresh }).ToList());
    }

    private static async Task<IResult> ContactAsync(Guid id, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var rental = await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.Status == "active", ct);
        if (rental is null)
        {
            return ApiResults.NotFound("Rental");
        }

        if (rental.AvailabilityConfirmedAt < clock.UtcNow - StaleAfter)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "rental.stale", "This home has not been confirmed as available for a month, so contact is paused.");
        }

        var userId = http.User.UserId();
        var dayAgo = clock.UtcNow - TimeSpan.FromDays(1);
        if (await db.AuditLog.CountAsync(a => a.ActorUserId == userId && a.Action == "contact.revealed" && a.OccurredAt >= dayAgo, ct) >= DocumentEndpoints.MaxContactsPerUserPerDay)
        {
            return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "contact.daily_limit", "You have contacted many listers today. Please continue tomorrow.");
        }

        if (!await subscriptions.IsListerAsync(rental.ListerUserId, ct))
        {
            return ApiResults.Forbidden("contact.lister_not_subscribed", "This lister has not turned on WhatsApp contact yet.");
        }

        var lister = await db.Users.AsNoTracking().FirstAsync(u => u.Id == rental.ListerUserId, ct);
        audit.Add(userId, "contact.revealed", "rental", id.ToString(), null);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ContactResponse(lister.WhatsappE164 ?? lister.PhoneE164));
    }

    /// <summary>
    /// Public responses use the grid point (public_point, about 110 m, migration 0014), and every distance and map
    /// filter is computed from it too, so the door cannot be worked out. The lister sees their exact point.
    /// </summary>
    private static RentalResponse ToResponse(RentalRow r, bool contactable, Guid? viewer, IReadOnlyList<ComparisonPointResponse>? points)
    {
        var mine = viewer is { } v && v == r.ListerUserId;
        var response = Exact(r, contactable, viewer, points);
        return mine ? response with { Approximate = false, Latitude = r.ExactLatitude, Longitude = r.ExactLongitude } : response;
    }

    private static RentalResponse Exact(RentalRow r, bool contactable, Guid? viewer, IReadOnlyList<ComparisonPointResponse>? points) =>
        new(r.Id, r.ListerKind, r.PropertyType, r.Bedrooms, r.AreaSqft, r.RentRupees, r.DepositRupees,
            Math.Round((decimal)r.RentRupees / r.AreaSqft, 2, MidpointRounding.AwayFromZero),
            Math.Round((decimal)r.DepositRupees / r.RentRupees, 1, MidpointRounding.AwayFromZero),
            r.Furnishing, r.PreferredTenants, r.Parking, r.AvailableFrom, r.Locality, r.Mandal, r.District, r.Latitude, r.Longitude, r.Status,
            r.AvailabilityConfirmedAt, contactable, viewer is { } v && v == r.ListerUserId, r.DistanceKm is { } km ? Math.Round(km, 1) : null, points ?? []);

    private static ComparablePlace Comparable(RentalResponse r) =>
        new(r.RentPerSqft, r.AreaSqft, r.PropertyType, Bedrooms: r.Bedrooms, DepositMonths: r.DepositMonths, Furnishing: r.Furnishing);

    /// <summary>
    /// A query whose text is fixed in this file (the column list is a constant) and whose values are all passed
    /// as parameters ({0}, {1}...). Never build the text from anything a person typed.
    /// </summary>
    private static FormattableString Sql(string format, params object[] values) => FormattableStringFactory.Create(format, values);
}
