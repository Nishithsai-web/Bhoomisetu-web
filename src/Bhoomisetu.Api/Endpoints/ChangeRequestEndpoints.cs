using System.Globalization;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record CreateChangeRequest(string? Field, string? ProposedValue, string? Reason);

public sealed record ChangeRequestDecision(string? Decision, string? Note);

public sealed record ChangeRequestResponse(
    Guid Id, Guid ParcelId, string Field, string? CurrentValue, string ProposedValue, string Reason, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? DecisionNote);

/// <summary>
/// Corrections to land facts. Anyone involved with the land (who listed it, has an offer on it, or owns it)
/// can propose a change with a reason; a trust reviewer who is not the requester approves or rejects it.
/// Approved changes are applied and audited in the same transaction.
/// </summary>
public static class ChangeRequestEndpoints
{
    public static void MapChangeRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/parcels/{parcelId:guid}/change-requests", CreateAsync).RequireAuthorization().WithTags("Corrections");
        app.MapGet("/api/v1/me/change-requests", MineAsync).RequireAuthorization().WithTags("Corrections");

        var admin = app.MapGroup("/api/v1/admin/change-requests").WithTags("Admin")
            .RequireAuthorization(AdminEndpoints.TrustReviewerPolicy)
            .AddEndpointFilter(AdminEndpoints.RequireCurrentReviewerAsync);
        admin.MapGet("/", QueueAsync);
        admin.MapPost("/{requestId:guid}/decision", DecideAsync);
    }

    private static async Task<IResult> CreateAsync(Guid parcelId, CreateChangeRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        var involved = parcel.CreatedBy == userId
            || await db.Offers.AnyAsync(o => o.ParcelId == parcelId && o.ListerUserId == userId, ct)
            || await db.ParcelParties.AnyAsync(p => p.ParcelId == parcelId && p.UserId == userId, ct);
        if (!involved)
        {
            return ApiResults.Forbidden("change.not_involved", "Only people who listed, offered or own this land can ask for a correction.");
        }

        if (request.Field is null || !ParcelFacts.Fields.Contains(request.Field))
        {
            return ApiResults.Invalid("change.field_invalid", "This detail cannot be corrected this way.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 3 or > 1000)
        {
            return ApiResults.Invalid("change.reason_required", "Say briefly why the detail is wrong (3 to 1,000 characters).");
        }

        var normalized = ParcelFacts.Normalise(request.Field, request.ProposedValue);
        if (!normalized.IsSuccess)
        {
            return ApiResults.Invalid(normalized.Error!);
        }

        var current = ParcelFacts.Read(parcel, request.Field);
        if (current == normalized.Value)
        {
            return ApiResults.Invalid("change.same_value", "That is already the recorded value.");
        }

        var record = new ParcelChangeRequestRecord
        {
            Id = Guid.CreateVersion7(),
            ParcelId = parcelId,
            RequestedBy = userId,
            Field = request.Field,
            CurrentValue = current,
            ProposedValue = normalized.Value,
            Reason = request.Reason.Trim(),
            Status = "pending",
            CreatedAt = clock.UtcNow,
        };
        db.ChangeRequests.Add(record);
        audit.Add(userId, "parcel.change_requested", "parcel", parcelId.ToString(), new { record.Id, record.Field, record.CurrentValue, record.ProposedValue });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "change.already_pending", "You already asked to correct this detail. A reviewer will look at it soon.");
        }

        return Results.Created($"/api/v1/me/change-requests", ToResponse(record));
    }

    private static async Task<IResult> MineAsync(HttpContext http, BhoomisetuDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var rows = await db.ChangeRequests.AsNoTracking()
            .Where(r => r.RequestedBy == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(ToResponse).ToList());
    }

    private static async Task<IResult> QueueAsync(string? status, BhoomisetuDbContext db, CancellationToken ct)
    {
        var wanted = status ?? "pending";
        if (wanted is not ("pending" or "approved" or "rejected"))
        {
            return ApiResults.Invalid("change.status_invalid", "Status must be pending, approved or rejected.");
        }

        // Oldest first, so nobody waits forever.
        var rows = await db.ChangeRequests.AsNoTracking()
            .Where(r => r.Status == wanted)
            .OrderBy(r => r.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(ToResponse).ToList());
    }

    private static async Task<IResult> DecideAsync(Guid requestId, ChangeRequestDecision body, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var reviewerId = http.User.UserId();
        if (body.Decision is not ("approve" or "reject"))
        {
            return ApiResults.Invalid("change.decision_invalid", "Decision must be approve or reject.");
        }

        if (body.Note is { Length: > 1000 })
        {
            return ApiResults.Invalid("change.note_too_long", "Keep the note under 1,000 characters.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock the row, so two reviewers deciding at the same moment cannot both apply it.
        var request = (await db.ChangeRequests
            .FromSql($"SELECT * FROM parcel_change_requests WHERE id = {requestId} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (request is null)
        {
            return ApiResults.NotFound("Change request");
        }

        if (request.Status != "pending")
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "change.already_decided", "This request was already decided.");
        }

        if (request.RequestedBy == reviewerId)
        {
            return ApiResults.Forbidden("change.self_review", "A reviewer cannot decide their own request.");
        }

        // A reviewer with a stake in the land (listed it, has an offer, or is a party) must leave it to another reviewer.
        var reviewerInvolved = await db.Parcels.AnyAsync(p => p.Id == request.ParcelId && p.CreatedBy == reviewerId, ct)
            || await db.Offers.AnyAsync(o => o.ParcelId == request.ParcelId && o.ListerUserId == reviewerId, ct)
            || await db.ParcelParties.AnyAsync(p => p.ParcelId == request.ParcelId && p.UserId == reviewerId, ct);
        if (reviewerInvolved)
        {
            return ApiResults.Forbidden("change.reviewer_involved", "You are involved with this land, so another reviewer must decide.");
        }

        var now = clock.UtcNow;
        if (body.Decision == "approve")
        {
            var parcel = await db.Parcels.FirstAsync(p => p.Id == request.ParcelId, ct);
            var before = ParcelFacts.Read(parcel, request.Field);
            if (before != request.CurrentValue)
            {
                // The fact changed since the request was made; approving would overwrite someone else's correction.
                return ApiResults.Problem(StatusCodes.Status409Conflict, "change.stale", "This detail changed after the request was made. Reject it and ask for a new one.");
            }

            ParcelFacts.Apply(parcel, request.Field, request.ProposedValue);
            parcel.UpdatedAt = now;
        }

        request.Status = body.Decision == "approve" ? "approved" : "rejected";
        request.DecidedBy = reviewerId;
        request.DecidedAt = now;
        request.DecisionNote = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
        audit.Add(reviewerId, $"parcel.change_{request.Status}", "parcel", request.ParcelId.ToString(),
            new { request.Id, request.Field, request.CurrentValue, request.ProposedValue });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(ToResponse(request));
    }

    private static ChangeRequestResponse ToResponse(ParcelChangeRequestRecord r) =>
        new(r.Id, r.ParcelId, r.Field, r.CurrentValue, r.ProposedValue, r.Reason, r.Status, r.CreatedAt, r.DecidedAt, r.DecisionNote);
}

/// <summary>
/// The parcel facts that can be corrected through a request, with the same limits as listing. Location and
/// survey number are deliberately excluded: changing them changes which land the record is, so that goes
/// through a new listing and duplicate review instead.
/// </summary>
public static class ParcelFacts
{
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "land_class", "frontage_m", "depth_m", "facing", "road_width_m", "road_type", "access_right", "distance_to_main_road_m",
    };

    public static Result<string> Normalise(string field, string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0)
        {
            return Result<string>.Failure("change.value_required", "Give the correct value.");
        }

        v = IndianDigits.ToAscii(v);
        return field switch
        {
            "land_class" => OneOf(v, Vocabulary.LandClasses),
            "facing" => OneOf(v, Vocabulary.Facings),
            "road_type" => OneOf(v, Vocabulary.RoadTypes),
            "access_right" => OneOf(v, Vocabulary.AccessRights),
            "frontage_m" or "depth_m" => Metres(v, 10_000m),
            "road_width_m" => Metres(v, 200m),
            "distance_to_main_road_m" => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var metres) && metres <= 100_000
                ? Result<string>.Success(metres.ToString(CultureInfo.InvariantCulture))
                : Result<string>.Failure("change.value_invalid", "Give a whole number of metres up to 100 km."),
            _ => Result<string>.Failure("change.field_invalid", "This detail cannot be corrected this way."),
        };
    }

    public static string? Read(ParcelRecord p, string field) => field switch
    {
        "land_class" => p.LandClass,
        "frontage_m" => Format(p.FrontageM),
        "depth_m" => Format(p.DepthM),
        "facing" => p.Facing,
        "road_width_m" => Format(p.RoadWidthM),
        "road_type" => p.RoadType,
        "access_right" => p.AccessRight,
        "distance_to_main_road_m" => p.DistanceToMainRoadM?.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    /// <summary>Applies a value already passed through <see cref="Normalise"/>.</summary>
    public static void Apply(ParcelRecord p, string field, string value)
    {
        switch (field)
        {
            case "land_class": p.LandClass = value; break;
            case "frontage_m": p.FrontageM = decimal.Parse(value, CultureInfo.InvariantCulture); break;
            case "depth_m": p.DepthM = decimal.Parse(value, CultureInfo.InvariantCulture); break;
            case "facing": p.Facing = value; break;
            case "road_width_m":
                p.RoadWidthM = decimal.Parse(value, CultureInfo.InvariantCulture);
                // The new number is the requester's statement, so the label must say so, even if the old width came
                // from an approved layout. A reviewer who measures it can upgrade the source later.
                p.RoadWidthSource = "lister";
                break;
            case "road_type": p.RoadType = value; break;
            case "access_right": p.AccessRight = value; break;
            case "distance_to_main_road_m": p.DistanceToMainRoadM = int.Parse(value, CultureInfo.InvariantCulture); break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static Result<string> OneOf(string value, IReadOnlySet<string> allowed) =>
        allowed.Contains(value)
            ? Result<string>.Success(value)
            : Result<string>.Failure("change.value_invalid", "That value is not one of the choices.");

    private static Result<string> Metres(string value, decimal max)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var metres))
        {
            return Result<string>.Failure("change.value_invalid", "Give a number of metres, for example 9.14.");
        }

        var rounded = Math.Round(metres, 2, MidpointRounding.AwayFromZero);
        return rounded < 0.01m || rounded > max
            ? Result<string>.Failure("change.value_invalid", $"Give a number of metres between 0.01 and {max.ToString(CultureInfo.InvariantCulture)}.")
            : Result<string>.Success(Format(rounded)!);
    }

    private static string? Format(decimal? value) =>
        value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : null;
}
