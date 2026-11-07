using System.Security.Cryptography;
using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Documents;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Identity;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record DocumentTypeResponse(string Code, bool ReferenceRequired, bool ImagesOnly);

public sealed record DocumentTypesResponse(IReadOnlyList<DocumentTypeResponse> Allowed, IReadOnlyList<string> NeverUpload, int MaxBytes);

public sealed record ParcelDocumentResponse(Guid Id, string Type, string? ReferenceNumber, string ContentType, int SizeBytes, string Status, string? ReviewNote, bool UploadedByMe, DateTimeOffset CreatedAt);

public sealed record DocumentCountResponse(string Type, int Count);

/// <summary>
/// The documents section of one parcel. <c>Access</c> says what the visitor may see: "sign_in", "subscribe" or
/// "full". Everyone gets the counts of checked documents by type; documents themselves need a subscription.
/// </summary>
public sealed record ParcelDocumentsResponse(string Access, bool CanUpload, string? UploadBlockedReason, IReadOnlyList<DocumentCountResponse> Available, IReadOnlyList<ParcelDocumentResponse> Documents);

public sealed record SubscriptionStatusResponse(bool ListerActive, DateTimeOffset? ListerUntil, bool ViewerActive, DateTimeOffset? ViewerUntil, bool ViewerPlanIsFree, decimal ListerMonthlyPriceRupees, string? SupportWhatsappE164, string WhatsappE164);

public sealed record ContactResponse(string WhatsappE164);

public sealed record WhatsappRequest(string? Phone);

public sealed record DocumentDecisionRequest(string? Decision, string? Note);

public sealed record AdminDocumentResponse(Guid Id, Guid ParcelId, string Village, string Mandal, string District, string SurveyNumber, string Type, string? ReferenceNumber, string ContentType, int SizeBytes, DateTimeOffset CreatedAt);

public sealed record ActivateSubscriptionRequest(string? Phone, string? Audience, int Months, decimal AmountRupees, string? PaymentReference);

public sealed record SubscriptionResponse(Guid Id, Guid UserId, string Audience, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Public land documents, subscriptions and WhatsApp contact.
/// <list type="bullet">
/// <item>Owners and agents with a lister subscription upload public papers only (see <see cref="LandDocuments"/>).</item>
/// <item>A trust reviewer looks at each document before subscribed buyers can open it.</item>
/// <item>Signed-in visitors can contact a subscribed lister on WhatsApp.</item>
/// </list>
/// </summary>
public static class DocumentEndpoints
{
    internal const int MaxContactsPerUserPerDay = 50;

    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/document-types", () => Results.Ok(new DocumentTypesResponse(
            LandDocuments.Allowed.Select(t => new DocumentTypeResponse(t.Code, t.ReferenceRequired, t.ImagesOnly)).ToList(),
            LandDocuments.NeverUpload, LandDocuments.MaxBytes))).WithTags("Documents");

        var parcel = app.MapGroup("/api/v1/parcels/{parcelId:guid}").WithTags("Documents");
        parcel.MapGet("/documents", ListAsync);
        parcel.MapPost("/documents", UploadAsync).RequireAuthorization();
        parcel.MapGet("/documents/{documentId:guid}/file", FileAsync).RequireAuthorization();
        parcel.MapDelete("/documents/{documentId:guid}", RemoveAsync).RequireAuthorization();
        parcel.MapGet("/offers/{offerId:guid}/contact", ContactAsync).RequireAuthorization();

        var me = app.MapGroup("/api/v1/me").WithTags("Subscriptions").RequireAuthorization();
        me.MapGet("/subscription", StatusAsync);
        me.MapPut("/whatsapp", SetWhatsappAsync);

        var admin = app.MapGroup("/api/v1/admin").WithTags("Admin")
            .RequireAuthorization(AdminEndpoints.TrustReviewerPolicy)
            .AddEndpointFilter(AdminEndpoints.RequireCurrentReviewerAsync);
        admin.MapGet("/documents", QueueAsync);
        admin.MapPost("/documents/{documentId:guid}/decision", DecideAsync);
        admin.MapPost("/subscriptions", ActivateAsync);
    }

    private static async Task<IResult> ListAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, CancellationToken ct)
    {
        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        if (ParcelEndpoints.RejectIfDuplicate(parcel) is { } duplicate)
        {
            return duplicate;
        }

        var userId = http.User.OptionalUserId();
        var isReviewer = userId is { } r && await db.Users.AnyAsync(u => u.Id == r && u.IsTrustReviewer, ct);
        var isViewer = await subscriptions.IsViewerAsync(userId, ct);
        var access = userId is null ? "sign_in" : isViewer || isReviewer ? "full" : "subscribe";

        var available = await db.ParcelDocuments.AsNoTracking()
            .Where(d => d.ParcelId == parcelId && d.Status == "approved")
            .GroupBy(d => d.DocType)
            .Select(g => new DocumentCountResponse(g.Key, g.Count()))
            .ToListAsync(ct);

        // Subscribed viewers see checked documents; uploaders also see their own, whatever the review status.
        var query = db.ParcelDocuments.AsNoTracking().Where(d => d.ParcelId == parcelId && d.Status != "removed");
        query = access == "full"
            ? query.Where(d => d.Status == "approved" || d.UploadedBy == userId || isReviewer)
            : query.Where(d => userId != null && d.UploadedBy == userId);
        var documents = await query.OrderBy(d => d.DocType).ThenBy(d => d.CreatedAt).ToListAsync(ct);

        string? blocked = null;
        if (userId is not { } uid)
        {
            blocked = "sign_in";
        }
        else if (!await IsInvolvedAsync(db, parcel, uid, ct))
        {
            blocked = "not_involved";
        }
        else if (!await subscriptions.IsListerAsync(uid, ct))
        {
            blocked = "subscribe";
        }

        return Results.Ok(new ParcelDocumentsResponse(access, blocked is null, blocked,
            available.OrderBy(a => a.Type, StringComparer.Ordinal).ToList(),
            documents.Select(d => ToResponse(d, userId)).ToList()));
    }

    private static async Task<IResult> UploadAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IDocumentStorage storage,
        DocumentOptions options, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var request = http.Request;
        if (request.ContentLength > LandDocuments.MaxBytes + 64 * 1024)
        {
            return ApiResults.Problem(StatusCodes.Status413PayloadTooLarge, "document.too_large", "The file is larger than 10 MB.");
        }

        if (!request.HasFormContentType)
        {
            return ApiResults.Invalid("document.file_required", "Choose a file to upload.");
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
        if (!await IsInvolvedAsync(db, parcel, userId, ct))
        {
            return ApiResults.Forbidden("document.not_involved", "Only the owner or an agent with a price on this land can add documents.");
        }

        if (!await subscriptions.IsListerAsync(userId, ct))
        {
            return ApiResults.Forbidden("subscription.lister_required", "Adding documents needs an active owner or agent subscription.");
        }

        var now = clock.UtcNow;
        var dayAgo = now - TimeSpan.FromDays(1);
        if (await db.ParcelDocuments.CountAsync(d => d.UploadedBy == userId && d.CreatedAt >= dayAgo, ct) >= options.MaxUploadsPerUserPerDay)
        {
            return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "document.daily_limit", "You have added many documents today. Please continue tomorrow.");
        }

        if (await db.ParcelDocuments.CountAsync(d => d.ParcelId == parcelId && d.UploadedBy == userId && (d.Status == "pending" || d.Status == "approved"), ct) >= options.MaxLivePerUploaderPerParcel)
        {
            return ApiResults.Invalid("document.parcel_limit", "You already have the most documents allowed on this land. Remove one first.");
        }

        var form = await request.ReadFormAsync(ct);
        if (!string.Equals(form["declaration"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResults.Invalid("document.declaration_required", "Confirm that this is not a sale deed, EC, passbook or ID card.");
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return ApiResults.Invalid("document.file_required", "Choose a file to upload.");
        }

        if (file.Length > LandDocuments.MaxBytes)
        {
            return ApiResults.Problem(StatusCodes.Status413PayloadTooLarge, "document.too_large", "The file is larger than 10 MB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        var checkedDocument = LandDocuments.Check(form["type"], form["referenceNumber"], bytes.Length, bytes.Span[..Math.Min(16, bytes.Length)]);
        if (!checkedDocument.IsSuccess)
        {
            return ApiResults.Invalid(checkedDocument.Error!);
        }

        var accepted = checkedDocument.Value;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        if (await db.ParcelDocuments.AnyAsync(d => d.ParcelId == parcelId && d.Sha256 == sha256 && (d.Status == "pending" || d.Status == "approved"), ct))
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "document.already_added", "This file is already added to this land.");
        }

        var record = new ParcelDocumentRecord
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            UploadedBy = userId,
            DocType = accepted.Type.Code,
            ReferenceNumber = accepted.ReferenceNumber,
            ContentType = accepted.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = sha256,
            StorageKey = $"{Guid.NewGuid():N}.{accepted.Extension}",
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now,
        };

        // File first, then the row: a crash in between leaves an orphan file, never a row without its file.
        buffer.Position = 0;
        await storage.SaveAsync(record.StorageKey, buffer, ct);
        db.ParcelDocuments.Add(record);
        audit.Add(userId, "document.uploaded", "parcel", parcelId.ToString(), new { documentId = record.Id, type = record.DocType, record.SizeBytes });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await storage.DeleteAsync(record.StorageKey, CancellationToken.None);
            return ApiResults.Problem(StatusCodes.Status409Conflict, "document.already_added", "This file is already added to this land.");
        }

        return Results.Created($"/api/v1/parcels/{parcelId}/documents/{record.Id}/file", ToResponse(record, userId));
    }

    private static async Task<IResult> FileAsync(Guid parcelId, Guid documentId, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IDocumentStorage storage, AuditWriter audit, CancellationToken ct)
    {
        var document = await db.ParcelDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.ParcelId == parcelId && d.Status != "removed", ct);
        if (document is null)
        {
            return ApiResults.NotFound("Document");
        }

        var userId = http.User.UserId();
        var isReviewer = await db.Users.AnyAsync(u => u.Id == userId && u.IsTrustReviewer, ct);
        var allowed = document.UploadedBy == userId || isReviewer
            || (document.Status == "approved" && await subscriptions.IsViewerAsync(userId, ct));
        if (!allowed)
        {
            return document.Status == "approved"
                ? ApiResults.Forbidden("subscription.viewer_required", "Opening documents needs a subscription.")
                : ApiResults.NotFound("Document");
        }

        var stream = await storage.OpenReadAsync(document.StorageKey, ct);
        if (stream is null)
        {
            return ApiResults.NotFound("Document file");
        }

        audit.Add(userId, "document.opened", "parcel", parcelId.ToString(), new { documentId });
        await db.SaveChangesAsync(ct);
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";
        var extension = document.StorageKey[(document.StorageKey.LastIndexOf('.') + 1)..];
        return Results.File(stream, document.ContentType, $"{document.DocType}-{document.Id.ToString("N")[..8]}.{extension}");
    }

    private static async Task<IResult> RemoveAsync(Guid parcelId, Guid documentId, HttpContext http, BhoomisetuDbContext db, IDocumentStorage storage, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var document = await db.ParcelDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.ParcelId == parcelId && d.UploadedBy == userId && d.Status != "removed", ct);
        if (document is null)
        {
            return ApiResults.NotFound("Document");
        }

        document.Status = "removed";
        document.UpdatedAt = clock.UtcNow;
        audit.Add(userId, "document.removed", "parcel", parcelId.ToString(), new { documentId });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(document.StorageKey, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ContactAsync(Guid parcelId, Guid offerId, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var offer = await db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId && o.ParcelId == parcelId && o.Status == "active", ct);
        if (offer is null)
        {
            return ApiResults.NotFound("Offer");
        }

        var userId = http.User.UserId();
        var dayAgo = clock.UtcNow - TimeSpan.FromDays(1);
        if (await db.AuditLog.CountAsync(a => a.ActorUserId == userId && a.Action == "contact.revealed" && a.OccurredAt >= dayAgo, ct) >= MaxContactsPerUserPerDay)
        {
            return ApiResults.Problem(StatusCodes.Status429TooManyRequests, "contact.daily_limit", "You have contacted many listers today. Please continue tomorrow.");
        }

        if (!await subscriptions.IsListerAsync(offer.ListerUserId, ct))
        {
            return ApiResults.Forbidden("contact.lister_not_subscribed", "This lister has not turned on WhatsApp contact yet.");
        }

        var lister = await db.Users.AsNoTracking().FirstAsync(u => u.Id == offer.ListerUserId, ct);
        audit.Add(userId, "contact.revealed", "offer", offerId.ToString(), new { parcelId });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ContactResponse(lister.WhatsappE164 ?? lister.PhoneE164));
    }

    private static async Task<IResult> StatusAsync(HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, SubscriptionOptions options, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var listerUntil = await subscriptions.ActiveUntilAsync(userId, Audiences.Lister, ct);
        var viewerUntil = await subscriptions.ActiveUntilAsync(userId, Audiences.Viewer, ct);
        return Results.Ok(new SubscriptionStatusResponse(listerUntil is not null, listerUntil, subscriptions.ViewerPlanIsFree || viewerUntil is not null, viewerUntil,
            subscriptions.ViewerPlanIsFree, options.ListerMonthlyPriceRupees, options.SupportWhatsappE164, user.WhatsappE164 ?? user.PhoneE164));
    }

    private static async Task<IResult> SetWhatsappAsync(WhatsappRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        string? whatsapp = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var parsed = IndianMobileNumber.Parse(request.Phone);
            if (!parsed.IsSuccess)
            {
                return ApiResults.Invalid(parsed.Error!);
            }

            whatsapp = parsed.Value.E164 == user.PhoneE164 ? null : parsed.Value.E164;
        }

        user.WhatsappE164 = whatsapp;
        user.UpdatedAt = clock.UtcNow;
        audit.Add(userId, "user.whatsapp_changed", "user", userId.ToString(), new { usesSignInNumber = whatsapp is null });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> QueueAsync(BhoomisetuDbContext db, CancellationToken ct)
    {
        var rows = await db.ParcelDocuments.AsNoTracking()
            .Where(d => d.Status == "pending")
            .OrderBy(d => d.CreatedAt)
            .Take(200)
            .Join(db.Parcels, d => d.ParcelId, p => p.Id, (d, p) => new AdminDocumentResponse(d.Id, d.ParcelId, p.Village, p.Mandal, p.District, p.SurveyNumber, d.DocType, d.ReferenceNumber, d.ContentType, d.SizeBytes, d.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> DecideAsync(Guid documentId, DocumentDecisionRequest body, HttpContext http, BhoomisetuDbContext db, IDocumentStorage storage, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (body.Decision is not ("approve" or "reject"))
        {
            return ApiResults.Invalid("document.decision_invalid", "Decision must be approve or reject.");
        }

        var note = body.Note?.Trim();
        if (body.Decision == "reject" && string.IsNullOrEmpty(note))
        {
            return ApiResults.Invalid("document.note_required", "Say why the document was rejected, so the lister can fix it.");
        }

        if (note is { Length: > 1000 })
        {
            return ApiResults.Invalid("document.note_too_long", "The note can be at most 1,000 characters.");
        }

        var reviewerId = http.User.UserId();
        var document = await db.ParcelDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return ApiResults.NotFound("Document");
        }

        if (document.Status != "pending")
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "document.already_decided", "This document was already decided or removed.");
        }

        if (document.UploadedBy == reviewerId || await IsReviewerInvolvedAsync(db, document.ParcelId, reviewerId, ct))
        {
            return ApiResults.Forbidden("document.reviewer_involved", "You are involved with this land, so another reviewer must decide.");
        }

        var now = clock.UtcNow;
        document.Status = body.Decision == "approve" ? "approved" : "rejected";
        document.ReviewNote = string.IsNullOrEmpty(note) ? null : note;
        document.ReviewedBy = reviewerId;
        document.ReviewedAt = now;
        document.UpdatedAt = now;
        audit.Add(reviewerId, $"document.{document.Status}", "parcel", document.ParcelId.ToString(), new { documentId, note = document.ReviewNote });
        await db.SaveChangesAsync(ct);

        // A rejected file is kept only as long as needed: the row keeps the reason, the file is deleted.
        if (document.Status == "rejected")
        {
            await storage.DeleteAsync(document.StorageKey, ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ActivateAsync(ActivateSubscriptionRequest request, HttpContext http, BhoomisetuDbContext db, SubscriptionService subscriptions, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (request.Audience is not (Audiences.Lister or Audiences.Viewer))
        {
            return ApiResults.Invalid("subscription.audience_invalid", "Choose owner/agent or buyer.");
        }

        if (request.Months is < 1 or > 24)
        {
            return ApiResults.Invalid("subscription.months_invalid", "Months must be between 1 and 24.");
        }

        if (request.AmountRupees < 0 || request.AmountRupees > 10_000_000 || decimal.Round(request.AmountRupees, 2) != request.AmountRupees)
        {
            return ApiResults.Invalid("subscription.amount_invalid", "Enter the amount paid in rupees.");
        }

        var reference = request.PaymentReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 100)
        {
            return ApiResults.Invalid("subscription.reference_required", "Enter the payment reference (UPI or receipt number).");
        }

        var phone = IndianMobileNumber.Parse(request.Phone);
        if (!phone.IsSuccess)
        {
            return ApiResults.Invalid(phone.Error!);
        }

        var e164 = phone.Value.E164;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneE164 == e164, ct);
        if (user is null)
        {
            return ApiResults.NotFound("Account for this number");
        }

        var reviewerId = http.User.UserId();
        if (user.Id == reviewerId)
        {
            return ApiResults.Forbidden("subscription.self", "A reviewer cannot activate their own subscription.");
        }

        var starts = await subscriptions.NextStartAsync(user.Id, request.Audience, ct);
        var record = new SubscriptionRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Audience = request.Audience,
            StartsAt = starts,
            EndsAt = starts.AddMonths(request.Months),
            AmountPaise = (long)(request.AmountRupees * 100),
            PaymentReference = reference,
            ActivatedBy = reviewerId,
            CreatedAt = clock.UtcNow,
        };
        db.Subscriptions.Add(record);
        audit.Add(reviewerId, "subscription.activated", "user", user.Id.ToString(),
            new { record.Audience, request.Months, record.AmountPaise, paymentReference = reference, record.StartsAt, record.EndsAt });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SubscriptionResponse(record.Id, record.UserId, record.Audience, record.StartsAt, record.EndsAt));
    }

    /// <summary>Who may add documents: whoever listed the land, a lister with an active price on it, or a recorded party.</summary>
    private static async Task<bool> IsInvolvedAsync(BhoomisetuDbContext db, ParcelRecord parcel, Guid userId, CancellationToken ct) =>
        parcel.CreatedBy == userId
        || await db.Offers.AnyAsync(o => o.ParcelId == parcel.Id && o.ListerUserId == userId && o.Status == "active", ct)
        || await db.ParcelParties.AnyAsync(p => p.ParcelId == parcel.Id && p.UserId == userId, ct);

    private static async Task<bool> IsReviewerInvolvedAsync(BhoomisetuDbContext db, Guid parcelId, Guid reviewerId, CancellationToken ct) =>
        await db.Parcels.AnyAsync(p => p.Id == parcelId && p.CreatedBy == reviewerId, ct)
        || await db.Offers.AnyAsync(o => o.ParcelId == parcelId && o.ListerUserId == reviewerId, ct)
        || await db.ParcelParties.AnyAsync(p => p.ParcelId == parcelId && p.UserId == reviewerId, ct);

    private static ParcelDocumentResponse ToResponse(ParcelDocumentRecord d, Guid? viewer) =>
        new(d.Id, d.DocType, d.ReferenceNumber, d.ContentType, d.SizeBytes, d.Status, d.UploadedBy == viewer ? d.ReviewNote : null, d.UploadedBy == viewer, d.CreatedAt);
}
