using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record AreaInput(decimal Value, string? Unit);

public sealed record RoadInput(decimal? WidthM, string? WidthSource, string? Type, string? AccessRight, int? DistanceToMainRoadM);

public sealed record CreateParcelRequest(
    string? StateCode,
    string? District,
    string? Mandal,
    string? Village,
    string? LgdVillageCode,
    string? SurveyNumber,
    string? LandClass,
    AreaInput? Area,
    string? AreaSource,
    decimal? FrontageM,
    decimal? DepthM,
    string? Facing,
    RoadInput? Road);

public sealed record ParcelLocationResponse(string StateCode, string District, string Mandal, string Village, string? LgdVillageCode, string SurveyNumber, string SurveyNumberAsEntered);

public sealed record ParcelAreaResponse(decimal EnteredValue, string EnteredUnit, string Source, AreaConversionResponse Conversions);

public sealed record ParcelDimensionsResponse(decimal? FrontageM, decimal? DepthM, decimal? FrontageFeet, decimal? DepthFeet, string? Facing, decimal? AreaFromDimensionsSqm, bool? DimensionsMatchArea);

public sealed record ParcelRoadResponse(decimal? WidthM, decimal? WidthFeet, string? WidthSource, string? Type, string? AccessRight, int? DistanceToMainRoadM);

public sealed record ParcelResponse(
    Guid Id,
    ParcelLocationResponse Location,
    string LandClass,
    ParcelAreaResponse Area,
    ParcelDimensionsResponse Dimensions,
    ParcelRoadResponse Road,
    int ActiveOffers,
    DateTimeOffset CreatedAt);

public sealed record ParcelSummary(Guid Id, string StateCode, string District, string Mandal, string Village, string SurveyNumber, string LandClass, decimal AreaSquareYards, int ActiveOffers);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class ParcelEndpoints
{
    private const decimal FeetPerMetre = 3.28083989501312m; // 1 / 0.3048

    /// <summary>Frontage × depth differing from the recorded area by more than this share is flagged (irregular plots are common, so it is not rejected).</summary>
    private const decimal DimensionTolerance = 0.20m;

    public static void MapParcelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parcels").WithTags("Parcels");

        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/", SearchAsync);
    }

    private static async Task<IResult> CreateAsync(CreateParcelRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var location = ParcelLocation.Create(request.StateCode, request.District, request.Mandal, request.Village, request.LgdVillageCode, request.SurveyNumber);
        if (!location.IsSuccess)
        {
            return ApiResults.Invalid(location.Error!);
        }

        if (request.LandClass is null || !Vocabulary.LandClasses.Contains(request.LandClass))
        {
            return ApiResults.Invalid("parcel.land_class_invalid", "Land class must be agricultural, non_agricultural, converted or unknown.");
        }

        if (request.Area is null || !AreaUnitCodes.TryParse(request.Area.Unit, out var unit))
        {
            return ApiResults.Invalid("area.unit_invalid", "Give the area with a supported unit.");
        }

        var area = Modules.Land.Area.From(request.Area.Value, unit);
        if (!area.IsSuccess)
        {
            return ApiResults.Invalid(area.Error!);
        }

        if (request.AreaSource is null || !Vocabulary.AreaSources.Contains(request.AreaSource))
        {
            return ApiResults.Invalid("parcel.area_source_invalid", "Area source must be record, deed, survey or lister.");
        }

        // Lengths are stored with 2 decimals; anything that rounds to zero is a typing error.
        if (request.FrontageM is { } f && (Math.Round(f, 2) < 0.01m || f > 10_000) || request.DepthM is { } d && (Math.Round(d, 2) < 0.01m || d > 10_000))
        {
            return ApiResults.Invalid("parcel.dimensions_invalid", "Frontage and depth must be between 0 and 10,000 metres.");
        }

        if (request.Facing is not null && !Vocabulary.Facings.Contains(request.Facing))
        {
            return ApiResults.Invalid("parcel.facing_invalid", "Facing must be a compass direction such as east or north_east.");
        }

        var road = request.Road;
        if (road is not null)
        {
            if (road.WidthM is { } w && (Math.Round(w, 2) < 0.01m || w > 200))
            {
                return ApiResults.Invalid("road.width_invalid", "Road width must be between 0 and 200 metres.");
            }

            if ((road.WidthM is null) != (road.WidthSource is null))
            {
                return ApiResults.Invalid("road.width_source_required", "Road width and where it came from must be given together.");
            }

            if (road.WidthSource is not null && !Vocabulary.RoadWidthSources.Contains(road.WidthSource))
            {
                return ApiResults.Invalid("road.width_source_invalid", "Road width source must be approved_layout, measured_on_map or lister.");
            }

            if (road.Type is not null && !Vocabulary.RoadTypes.Contains(road.Type))
            {
                return ApiResults.Invalid("road.type_invalid", "Unknown road type.");
            }

            if (road.AccessRight is not null && !Vocabulary.AccessRights.Contains(road.AccessRight))
            {
                return ApiResults.Invalid("road.access_right_invalid", "Unknown access right.");
            }

            if (road.DistanceToMainRoadM is < 0 or > 100_000)
            {
                return ApiResults.Invalid("road.distance_invalid", "Distance to the main road must be between 0 and 100 km.");
            }
        }

        var loc = location.Value;
        // Either key matching means the same land: with or without the LGD village code.
        var existing = await FindExistingAsync(db, loc, ct);
        if (existing is { } existingId)
        {
            return DuplicateParcel(existingId);
        }

        var now = clock.UtcNow;
        var userId = http.User.UserId();
        var record = new ParcelRecord
        {
            Id = Guid.CreateVersion7(),
            ParcelKey = loc.ParcelKey,
            NameKey = loc.NameKey,
            StateCode = loc.StateCode,
            District = loc.District,
            Mandal = loc.Mandal,
            Village = loc.Village,
            LgdVillageCode = loc.LgdVillageCode,
            SurveyNumberRaw = loc.SurveyNumber.Raw,
            SurveyNumber = loc.SurveyNumber.Normalized,
            LandClass = request.LandClass,
            RecordedAreaSqm = Math.Round(area.Value.SquareMetres, 4, MidpointRounding.AwayFromZero),
            EnteredAreaValue = Modules.Land.Area.NormaliseEntered(request.Area.Value),
            EnteredAreaUnit = unit.ToCode(),
            AreaSource = request.AreaSource,
            FrontageM = Round2(request.FrontageM),
            DepthM = Round2(request.DepthM),
            Facing = request.Facing,
            RoadWidthM = Round2(road?.WidthM),
            RoadWidthSource = road?.WidthSource,
            RoadType = road?.Type,
            AccessRight = road?.AccessRight,
            DistanceToMainRoadM = road?.DistanceToMainRoadM,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Parcels.Add(record);
        audit.Add(userId, "parcel.created", "parcel", record.Id.ToString(), new { record.ParcelKey });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request created the same land a moment ago.
            db.ChangeTracker.Clear();
            var winner = await FindExistingAsync(db, loc, ct);
            return winner is { } id ? DuplicateParcel(id) : throw new InvalidOperationException("Unique violation without a matching parcel.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NumericValueOutOfRange } pg)
        {
            // Validation above should prevent this; if a rule drifts from the schema, answer 400 instead of 500.
            http.RequestServices.GetRequiredService<ILogger<CreateParcelRequest>>()
                .LogWarning(ex, "Parcel rejected by database rule {Constraint}", pg.ConstraintName);
            return ApiResults.Invalid("parcel.invalid", "Some details are outside the allowed range. Please check the numbers.");
        }

        return Results.Created($"/api/v1/parcels/{record.Id}", ToResponse(record, activeOffers: 0));
    }

    private static Task<Guid?> FindExistingAsync(BhoomisetuDbContext db, ParcelLocation loc, CancellationToken ct)
    {
        var parcelKey = loc.ParcelKey;
        var nameKey = loc.NameKey;
        return db.Parcels.AsNoTracking()
            .Where(p => p.ParcelKey == parcelKey || p.NameKey == nameKey)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static decimal? Round2(decimal? value) => value is { } v ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;

    private static IResult DuplicateParcel(Guid existingId) => ApiResults.Problem(
        StatusCodes.Status409Conflict,
        "parcel.already_exists",
        "This land is already listed. Add your offer to the existing parcel instead.",
        new Dictionary<string, object?> { ["existingParcelId"] = existingId });

    private static async Task<IResult> GetAsync(Guid id, BhoomisetuDbContext db, CancellationToken ct)
    {
        var record = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (record is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        var offers = await db.Offers.CountAsync(o => o.ParcelId == id && o.Status == "active", ct);
        return Results.Ok(ToResponse(record, offers));
    }

    private static async Task<IResult> SearchAsync(
        string? q, string? state, string? district, string? mandal, string? village, int? page, int? pageSize, BhoomisetuDbContext db, CancellationToken ct)
    {
        // Capped so (page - 1) * size can never overflow into a negative OFFSET.
        var p = Math.Clamp(page ?? 1, 1, 10_000);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);

        var query = db.Parcels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
        {
            var s = state.Trim().ToUpperInvariant();
            query = query.Where(x => x.StateCode == s);
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            query = query.Where(x => EF.Functions.ILike(x.District, ContainsPattern(district)));
        }

        if (!string.IsNullOrWhiteSpace(mandal))
        {
            query = query.Where(x => EF.Functions.ILike(x.Mandal, ContainsPattern(mandal)));
        }

        if (!string.IsNullOrWhiteSpace(village))
        {
            query = query.Where(x => EF.Functions.ILike(x.Village, ContainsPattern(village)));
        }

        // One free-text box: a village, mandal or district name.
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = ContainsPattern(q);
            query = query.Where(x => EF.Functions.ILike(x.Village, pattern) || EF.Functions.ILike(x.Mandal, pattern) || EF.Functions.ILike(x.District, pattern));
        }

        var total = await query.CountAsync(ct);
        var squareYard = Modules.Land.Area.FactorToSquareMetres(AreaUnit.SquareYard);
        var items = await query
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new
            {
                x.Id, x.StateCode, x.District, x.Mandal, x.Village, x.SurveyNumber, x.LandClass, x.RecordedAreaSqm,
                Offers = db.Offers.Count(o => o.ParcelId == x.Id && o.Status == "active"),
            })
            .ToListAsync(ct);

        var summaries = items
            .Select(x => new ParcelSummary(x.Id, x.StateCode, x.District, x.Mandal, x.Village, x.SurveyNumber, x.LandClass,
                Math.Round(x.RecordedAreaSqm / squareYard, 2, MidpointRounding.AwayFromZero), x.Offers))
            .ToList();
        return Results.Ok(new PagedResponse<ParcelSummary>(summaries, p, size, total));
    }

    /// <summary>ILIKE pattern for "contains", with the user's own % and _ escaped.</summary>
    private static string ContainsPattern(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    internal static ParcelResponse ToResponse(ParcelRecord r, int activeOffers)
    {
        if (!AreaUnitCodes.TryParse(r.EnteredAreaUnit, out var unit))
        {
            throw new InvalidOperationException($"Parcel {r.Id} has unknown unit {r.EnteredAreaUnit}.");
        }

        var area = Modules.Land.Area.From(r.EnteredAreaValue, unit).Value;
        decimal? fromDimensions = r.FrontageM is { } f && r.DepthM is { } d ? Math.Round(f * d, 2, MidpointRounding.AwayFromZero) : null;
        bool? matches = fromDimensions is { } dims
            ? Math.Abs(dims - r.RecordedAreaSqm) <= r.RecordedAreaSqm * DimensionTolerance
            : null;

        return new ParcelResponse(
            r.Id,
            new ParcelLocationResponse(r.StateCode, r.District, r.Mandal, r.Village, r.LgdVillageCode, r.SurveyNumber, r.SurveyNumberRaw),
            r.LandClass,
            new ParcelAreaResponse(r.EnteredAreaValue, r.EnteredAreaUnit, r.AreaSource, UnitsEndpoints.ToResponse(r.EnteredAreaValue, unit, area)),
            new ParcelDimensionsResponse(r.FrontageM, r.DepthM, ToFeet(r.FrontageM), ToFeet(r.DepthM), r.Facing, fromDimensions, matches),
            new ParcelRoadResponse(r.RoadWidthM, ToFeet(r.RoadWidthM), r.RoadWidthSource, r.RoadType, r.AccessRight, r.DistanceToMainRoadM),
            activeOffers,
            r.CreatedAt);
    }

    private static decimal? ToFeet(decimal? metres) =>
        metres is { } m ? Math.Round(m * FeetPerMetre, 1, MidpointRounding.AwayFromZero) : null;
}
