using Bhoomisetu.Modules.Land;
using Xunit;

namespace Bhoomisetu.UnitTests.Land;

public sealed class LandDocumentsTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8];

    [Theory]
    [InlineData("brochure")]
    [InlineData("site_plan")]
    [InlineData("location_map")]
    public void Public_papers_without_a_number_are_accepted_as_pdf(string type)
    {
        var result = LandDocuments.Check(type, null, 1000, Pdf);
        Assert.True(result.IsSuccess);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal("pdf", result.Value.Extension);
    }

    [Theory]
    [InlineData("sale_deed")]
    [InlineData("encumbrance_certificate")]
    [InlineData("pattadar_passbook")]
    [InlineData("aadhaar")]
    [InlineData("")]
    [InlineData(null)]
    public void Ownership_papers_and_unknown_types_are_refused(string? type)
    {
        Assert.Equal("document.type_invalid", LandDocuments.Check(type, null, 1000, Pdf).Error!.Code);
    }

    [Theory]
    [InlineData("layout_approval")]
    [InlineData("rera_certificate")]
    [InlineData("building_permission")]
    public void Approvals_need_the_number_printed_on_them(string type)
    {
        Assert.Equal("document.reference_required", LandDocuments.Check(type, "  ", 1000, Pdf).Error!.Code);
        Assert.True(LandDocuments.Check(type, "p02400001234", 1000, Pdf).IsSuccess);
    }

    [Fact]
    public void Reference_numbers_are_normalised_and_checked()
    {
        var result = LandDocuments.Check("layout_approval", "  lp no.  ౧౨౩/HMDA/2023 ", 1000, Pdf);
        Assert.Equal("LP NO. 123/HMDA/2023", result.Value.ReferenceNumber);
        Assert.Equal("document.reference_invalid", LandDocuments.Check("rera_certificate", "<script>", 1000, Pdf).Error!.Code);
        Assert.Equal("document.reference_invalid", LandDocuments.Check("rera_certificate", new string('A', 61), 1000, Pdf).Error!.Code);
    }

    [Fact]
    public void Content_type_comes_from_the_file_not_its_name()
    {
        Assert.Equal("image/jpeg", LandDocuments.Detect(Jpeg)!.Value.ContentType);
        Assert.Equal("image/png", LandDocuments.Detect(Png)!.Value.ContentType);
        Assert.Equal("image/webp", LandDocuments.Detect(Webp)!.Value.ContentType);
        Assert.Null(LandDocuments.Detect("MZ\u0090\u0000"u8));
        Assert.Null(LandDocuments.Detect("<html>"u8));
        Assert.Equal("document.format", LandDocuments.Check("brochure", null, 1000, "<svg onload=x>"u8).Error!.Code);
    }

    [Fact]
    public void Photos_must_be_images()
    {
        Assert.Equal("document.photo_format", LandDocuments.Check("site_photo", null, 1000, Pdf).Error!.Code);
        Assert.True(LandDocuments.Check("site_photo", null, 1000, Jpeg).IsSuccess);
    }

    [Fact]
    public void Size_is_checked()
    {
        Assert.Equal("document.file_required", LandDocuments.Check("brochure", null, 0, Pdf).Error!.Code);
        Assert.Equal("document.too_large", LandDocuments.Check("brochure", null, LandDocuments.MaxBytes + 1, Pdf).Error!.Code);
        Assert.True(LandDocuments.Check("brochure", null, LandDocuments.MaxBytes, Pdf).IsSuccess);
    }
}
