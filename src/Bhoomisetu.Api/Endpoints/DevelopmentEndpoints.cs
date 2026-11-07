using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record NearbyDevelopmentResponse(Guid ProjectId, string Name, string Kind, string Status, string Authority, string? SourceUrl, DateOnly SourceDate, decimal? DistanceKm, bool MayAffectParcel);

public sealed record DevelopmentProjectRequest(string? Name, string? Kind, string? Status, string? Authority, string? SourceUrl, DateOnly? SourceDate);

public sealed record DevelopmentProjectResponse(Guid Id, string Name, string Kind, string Status, string Authority, string? SourceUrl, DateOnly SourceDate, int LinkedParcels);

public sealed record LinkDevelopmentRequest(Guid? ProjectId, decimal? DistanceKm, bool MayAffectParcel);

public sealed record ListingReportRequest(string? Reason, string? Details);

public sealed record ListingReportResponse(Guid Id, Guid ParcelId, string Village, string Mandal, string District, string SurveyNumber, string Reason, string? Details, DateTimeOffset CreatedAt);

public sealed record ReportDecisionRequest(string? Decision, string? Note);

/// <summary>
/// Nearby developments (metro, highways, road widening, corridors) shown on each land page, and "report this
/// listing". Developments come only from reviewers with an official source and date; they are information, never
/// a promise that a price will rise.
/// </summary>
public static class DevelopmentEndpoints
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
        { "metro", "highway", "ring_road", "road_widening", "new_road", "railway", "airport", "industrial_corridor", "it_corridor", "other" };

    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
        { "proposed_unverified", "approved", "under_construction", "completed", "cancelled" };

    public static readonly IReadOnlySet<string> ReportReasons = new HashSet<string>(StringComparer.Ordinal)
        { "wrong_details", "already_sold", "fraud_suspected", "duplicate", "ownership_dispute", "other" };

    internal const int MaxReportsPerUserPerDay = 10;

    public static void MapDevelopmentEndpoints(this IEndpointRouteBuilder app)
    {
        var parcel = app.MapGroup("/api/v1/parcels/{parcelId:guid}").WithTags("Developments");
        parcel.MapGet("/developments", NearbyAsync);
        parcel.MapPost("/reports", ReportAsync).RequireAuthorization();

        var admin = app.MapGroup("/api/v1/admin").WithTags("Admin")
            .RequireAuthorization(AdminEndpoints.TrustReviewerPolicy)
            .AddEndpointFilter(AdminEndpoints.RequireCurrentReviewerAsync);
        admin.MapGet("/developments", ProjectsAsync);
        admin.MapPost("/developments", CreateProjectAsync);
        admin.MapPut("/developments/{projectId:guid}", UpdateProjectAsync);
        admin.MapPut("/parcels/{parcelId:guid}/developments", LinkAsync);
        admin.MapDelete("/parcels/{parcelId:guid}/developments/{projectId:guid}", UnlinkAsync);
        admin.MapGet("/reports", ReportQueueAsync);
        admin.MapPost("/reports/{reportId:guid}/decision", DecideReportAsync);
    }

    private static async Task<IResult> NearbyAsync(Guid parcelId, BhoomisetuDbContext db, CancellationToken ct)
    {
        if (!await db.Parcels.AnyAsync(p => p.Id == parcelId, ct))
        {
            return ApiResults.NotFound("Parcel");
        }

        var rows = await db.ParcelDevelopmentLinks.AsNoTracking()
            .Where(l => l.ParcelId == parcelId)
            .Join(db.DevelopmentProjects, l => l.ProjectId, p => p.Id, (l, p) => new NearbyDevelopmentResponse(
                p.Id, p.Name, p.Kind, p.Status, p.Authority, p.SourceUrl, p.SourceDate, l.DistanceKm, l.MayAffectParcel))
            .ToListAsync(ct);

        // Projects that may touch the land first, then nearest first; unknown distances last.
        return Results.Ok(rows.OrderByDescending(r => r.MayAffectParcel).ThenBy(r => r.DistanceKm ?? decimal.MaxValue).ThenBy(r => r.Name, StringComparer.Ordinal).ToList());
    }

    private static async Task<IResult> ProjectsAsync(BhoomisetuDbContext db, CancellationToken ct)
    {
        var rows = await db.DevelopmentProjects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new DevelopmentProjectResponse(p.Id, p.Name, p.Kind, p.Status, p.Authority, p.SourceUrl, p.SourceDate,
                db.ParcelDevelopmentLinks.Count(l => l.ProjectId == p.Id)))
            .Take(500)
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateProjectAsync(DevelopmentProjectRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (Validate(request, clock) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var record = new DevelopmentProjectRecord
        {
            Id = Guid.NewGuid(),
            CreatedBy = http.User.UserId(),
            CreatedAt = now,
        };
        Apply(record, request, now);
        db.DevelopmentProjects.Add(record);
        audit.Add(record.CreatedBy, "development.created", "development", record.Id.ToString(), new { record.Name, record.Kind, record.Status, record.SourceUrl, record.SourceDate });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/admin/developments/{record.Id}", ToResponse(record, 0));
    }

    private static async Task<IResult> UpdateProjectAsync(Guid projectId, DevelopmentProjectRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (Validate(request, clock) is { } invalid)
        {
            return invalid;
        }

        var record = await db.DevelopmentProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (record is null)
        {
            return ApiResults.NotFound("Development");
        }

        var before = new { record.Status, record.SourceUrl, record.SourceDate };
        Apply(record, request, clock.UtcNow);
        audit.Add(http.User.UserId(), "development.updated", "development", projectId.ToString(), new { before, after = new { record.Status, record.SourceUrl, record.SourceDate } });
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(record, await db.ParcelDevelopmentLinks.CountAsync(l => l.ProjectId == projectId, ct)));
    }

    private static async Task<IResult> LinkAsync(Guid parcelId, LinkDevelopmentRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (request.DistanceKm is { } d && (d < 0 || d > 500 || decimal.Round(d, 2) != d))
        {
            return ApiResults.Invalid("development.distance_invalid", "Distance must be between 0 and 500 km, with at most 2 decimals.");
        }

        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        if (ParcelEndpoints.RejectIfDuplicate(parcel) is { } duplicate)
        {
            return duplicate;
        }

        if (request.ProjectId is not { } projectId || !await db.DevelopmentProjects.AnyAsync(p => p.Id == projectId, ct))
        {
            return ApiResults.Invalid("development.project_invalid", "Choose a development from the list.");
        }

        var userId = http.User.UserId();
        var link = await db.ParcelDevelopmentLinks.FirstOrDefaultAsync(l => l.ParcelId == parcelId && l.ProjectId == projectId, ct);
        if (link is null)
        {
            link = new ParcelDevelopmentLinkRecord { ParcelId = parcelId, ProjectId = projectId, CreatedBy = userId, CreatedAt = clock.UtcNow };
            db.ParcelDevelopmentLinks.Add(link);
        }

        link.DistanceKm = request.DistanceKm;
        link.MayAffectParcel = request.MayAffectParcel;
        audit.Add(userId, "development.linked", "parcel", parcelId.ToString(), new { projectId, request.DistanceKm, request.MayAffectParcel });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "This land changed at the same time. Please try again.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> UnlinkAsync(Guid parcelId, Guid projectId, HttpContext http, BhoomisetuDbContext db, AuditWriter audit, CancellationToken ct)
    {
        var link = await db.ParcelDevelopmentLinks.FirstOrDefaultAsync(l => l.ParcelId == parcelId && l.ProjectId == projectId, ct);
        if (link is null)
        {
            return ApiResults.NotFound("Link");
        }

        db.ParcelDevelopmentLinks.Remove(link);
        audit.Add(http.User.UserId(), "development.unlinked", "parcel", parcelId.ToString(), new { projectId });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReportAsync(Guid parcelId, ListingReportRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (request.Reason is null || !ReportReasons.Contains(request.Reason))
        {
            return ApiResults.Invalid("report.reason_invalid", "Choose why you are reporting this land.");
        }

        var details = request.Details?.Trim();
        if (details is { Length: > 1000 })
        {
            return ApiResults.Invalid("report.details_too_long", "Details can be at most 1,000 characters.");
        }

        if (request.Reason == "other" && string.IsNullOrEmpty(details))
        {
            return ApiResults.Invalid("report.details_required", "Say in a few words what is wrong.");
        }

        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        if (ParcelEndpoints.RejectIfDuplicate(parcel) is { } duplicate)
        {
            return duplicate;
        }

        var userId = http.User.UserId();
        var now = clock.UtcNow;
        var dayAgo = now - TimeSpan.FromDays(1);
        if (await db.ListingReports.CountAsync(r => r.ReporterId == userId && r.CreatedAt >= dayAgo, ct) >= MaxReportsPerUserPerDay)
        {
            return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "report.daily_limit", "You have sent many reports today. Please continue tomorrow.");
        }

        var record = new ListingReportRecord
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            ReporterId = userId,
            Reason = request.Reason,
            Details = string.IsNullOrEmpty(details) ? null : details,
            Status = "open",
            CreatedAt = now,
        };
        db.ListingReports.Add(record);
        audit.Add(userId, "report.created", "parcel", parcelId.ToString(), new { reportId = record.Id, record.Reason });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "report.already_open", "You already reported this land. A reviewer will look at it.");
        }

        return Results.Created($"/api/v1/parcels/{parcelId}", new { id = record.Id });
    }

    private static async Task<IResult> ReportQueueAsync(BhoomisetuDbContext db, CancellationToken ct)
    {
        var rows = await db.ListingReports.AsNoTracking()
            .Where(r => r.Status == "open")
            .OrderBy(r => r.CreatedAt)
            .Take(200)
            .Join(db.Parcels, r => r.ParcelId, p => p.Id, (r, p) => new ListingReportResponse(r.Id, r.ParcelId, p.Village, p.Mandal, p.District, p.SurveyNumber, r.Reason, r.Details, r.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> DecideReportAsync(Guid reportId, ReportDecisionRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (request.Decision is not ("resolve" or "dismiss"))
        {
            return ApiResults.Invalid("report.decision_invalid", "Decision must be resolve or dismiss.");
        }

        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            return ApiResults.Invalid("report.note_required", "Say what was done about this report.");
        }

        if (note.Length > 1000)
        {
            return ApiResults.Invalid("report.note_too_long", "The note can be at most 1,000 characters.");
        }

        var reviewerId = http.User.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var report = await db.ListingReports.FromSql($"SELECT * FROM listing_reports WHERE id = {reportId} FOR UPDATE").FirstOrDefaultAsync(ct);
        if (report is null)
        {
            return ApiResults.NotFound("Report");
        }

        if (report.Status != "open")
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "report.already_decided", "This report was already handled.");
        }

        if (report.ReporterId == reviewerId || await DocumentEndpoints.IsReviewerInvolvedAsync(db, report.ParcelId, reviewerId, ct))
        {
            return ApiResults.Forbidden("report.reviewer_involved", "You sent this report or are involved with this land, so another reviewer must handle it.");
        }

        report.Status = request.Decision == "resolve" ? "resolved" : "dismissed";
        report.ResolutionNote = note;
        report.ResolvedBy = reviewerId;
        report.ResolvedAt = clock.UtcNow;
        audit.Add(reviewerId, $"report.{report.Status}", "parcel", report.ParcelId.ToString(), new { reportId, note });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static IResult? Validate(DevelopmentProjectRequest r, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length is < 2 or > 200)
        {
            return ApiResults.Invalid("development.name_invalid", "Enter the project's name (2 to 200 characters).");
        }

        if (r.Kind is null || !Kinds.Contains(r.Kind))
        {
            return ApiResults.Invalid("development.kind_invalid", "Choose the kind of project.");
        }

        if (r.Status is null || !Statuses.Contains(r.Status))
        {
            return ApiResults.Invalid("development.status_invalid", "Choose the project's status.");
        }

        if (string.IsNullOrWhiteSpace(r.Authority) || r.Authority.Trim().Length is < 2 or > 200)
        {
            return ApiResults.Invalid("development.authority_invalid", "Enter the authority (for example NHAI, HMDA, R&B department).");
        }

        // Anything other than "proposed, unverified" needs an official link a buyer can open.
        var url = r.SourceUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || url.Length is < 12 or > 500 || url.Any(char.IsWhiteSpace)))
        {
            return ApiResults.Invalid("development.source_invalid", "The source must be an https:// link.");
        }

        if (string.IsNullOrEmpty(url) && r.Status != "proposed_unverified")
        {
            return ApiResults.Invalid("development.source_required", "Give the official source link, or mark the project as proposed and unverified.");
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (r.SourceDate is not { } date || date > today || date.Year < 1990)
        {
            return ApiResults.Invalid("development.date_invalid", "Enter the date of the source (not in the future).");
        }

        return null;
    }

    private static void Apply(DevelopmentProjectRecord record, DevelopmentProjectRequest r, DateTimeOffset now)
    {
        record.Name = r.Name!.Trim();
        record.Kind = r.Kind!;
        record.Status = r.Status!;
        record.Authority = r.Authority!.Trim();
        record.SourceUrl = string.IsNullOrWhiteSpace(r.SourceUrl) ? null : r.SourceUrl.Trim();
        record.SourceDate = r.SourceDate!.Value;
        record.UpdatedAt = now;
    }

    private static DevelopmentProjectResponse ToResponse(DevelopmentProjectRecord p, int linked) =>
        new(p.Id, p.Name, p.Kind, p.Status, p.Authority, p.SourceUrl, p.SourceDate, linked);
}
