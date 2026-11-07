using System.Text.RegularExpressions;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Land;

/// <summary>
/// One kind of document a lister may upload. Only public, marketing-level papers are allowed: what a
/// developer or agent already hands out freely. Ownership papers (sale deed, EC, passbook, ROR-1B, link
/// documents) and ID cards are never uploaded; buyers see those in person with the seller.
/// </summary>
/// <param name="Code">Stable code stored in the database and translated in the web app.</param>
/// <param name="ReferenceRequired">True when the paper carries a number buyers can check with the issuing office.</param>
/// <param name="ImagesOnly">True for photos; false allows PDF as well.</param>
public sealed record LandDocumentType(string Code, bool ReferenceRequired, bool ImagesOnly);

/// <summary>A document that passed every check and may be stored.</summary>
public sealed record AcceptedDocument(LandDocumentType Type, string ContentType, string Extension, string? ReferenceNumber);

/// <summary>
/// The allowed document types and the checks every upload passes. Pure rules with no storage or HTTP, so
/// they are unit-tested and the same for the API and any import tool.
/// </summary>
public static partial class LandDocuments
{
    /// <summary>Largest file accepted: enough for a scanned approval or brochure, small enough for village data plans.</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The upload dropdown, in the order shown to listers.</summary>
    public static readonly IReadOnlyList<LandDocumentType> Allowed =
    [
        new("brochure", ReferenceRequired: false, ImagesOnly: false),
        new("site_plan", ReferenceRequired: false, ImagesOnly: false),
        new("location_map", ReferenceRequired: false, ImagesOnly: false),
        // HMDA, DTCP or another development authority's layout approval (carries an LP / approval number).
        new("layout_approval", ReferenceRequired: true, ImagesOnly: false),
        // RERA registration certificate (TS RERA, AP RERA, ...), with the registration number.
        new("rera_certificate", ReferenceRequired: true, ImagesOnly: false),
        // Building permission from the municipality, GHMC or gram panchayat, with the permit number.
        new("building_permission", ReferenceRequired: true, ImagesOnly: false),
        new("site_photo", ReferenceRequired: false, ImagesOnly: true),
    ];

    /// <summary>Shown as "never upload these" next to the dropdown; not accepted under any type.</summary>
    public static readonly IReadOnlyList<string> NeverUpload =
        ["sale_deed", "link_documents", "encumbrance_certificate", "pattadar_passbook", "pahani_ror", "identity_documents"];

    public static LandDocumentType? Find(string? code) =>
        Allowed.FirstOrDefault(t => string.Equals(t.Code, code?.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// Checks the type, the reference number and the file itself. The content type comes from the file's first
    /// bytes, never from the name or the browser, so a renamed file cannot pass as a PDF.
    /// </summary>
    public static Result<AcceptedDocument> Check(string? typeCode, string? referenceNumber, long sizeBytes, ReadOnlySpan<byte> head)
    {
        var type = Find(typeCode);
        if (type is null)
        {
            return Result<AcceptedDocument>.Failure("document.type_invalid", "Choose a document type from the list.");
        }

        if (sizeBytes <= 0)
        {
            return Result<AcceptedDocument>.Failure("document.file_required", "Choose a file to upload.");
        }

        if (sizeBytes > MaxBytes)
        {
            return Result<AcceptedDocument>.Failure("document.too_large", "The file is larger than 10 MB.");
        }

        var detected = Detect(head);
        if (detected is null || (type.ImagesOnly && detected.Value.ContentType == "application/pdf"))
        {
            return type.ImagesOnly
                ? Result<AcceptedDocument>.Failure("document.photo_format", "Photos must be JPEG, PNG or WebP.")
                : Result<AcceptedDocument>.Failure("document.format", "Upload a PDF, JPEG, PNG or WebP file.");
        }

        var reference = NormaliseReference(referenceNumber);
        if (type.ReferenceRequired && reference is null)
        {
            return Result<AcceptedDocument>.Failure("document.reference_required", "Enter the number printed on this document.");
        }

        if (reference is not null && !ReferencePattern().IsMatch(reference))
        {
            return Result<AcceptedDocument>.Failure("document.reference_invalid", "The number can have letters, digits, / - and . (up to 60).");
        }

        return Result<AcceptedDocument>.Success(new AcceptedDocument(type, detected.Value.ContentType, detected.Value.Extension, reference));
    }

    /// <summary>File type from its signature: PDF, JPEG, PNG or WebP; anything else is refused.</summary>
    public static (string ContentType, string Extension)? Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8))
        {
            return ("application/pdf", "pdf");
        }

        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (head.StartsWith(png))
        {
            return ("image/png", "png");
        }

        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return ("image/webp", "webp");
        }

        return null;
    }

    /// <summary>Trims, upper-cases and collapses spaces; Telugu and Devanagari digits become 0-9. Empty means none.</summary>
    public static string? NormaliseReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = SpacesPattern().Replace(IndianDigits.ToAscii(value.Trim()), " ").ToUpperInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9 ./\-]{1,59}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();
}
