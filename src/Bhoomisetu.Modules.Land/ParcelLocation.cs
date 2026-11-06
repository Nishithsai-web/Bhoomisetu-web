using System.Text.RegularExpressions;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Land;

/// <summary>
/// Where a parcel is, down to the village, plus its survey number. The parcel key derived from it is what
/// stops the same land being created twice by different agents.
/// </summary>
public sealed partial record ParcelLocation
{
    private ParcelLocation(string stateCode, string district, string mandal, string village, string? lgdVillageCode, SurveyNumber surveyNumber)
    {
        StateCode = stateCode;
        District = district;
        Mandal = mandal;
        Village = village;
        LgdVillageCode = lgdVillageCode;
        SurveyNumber = surveyNumber;
    }

    public string StateCode { get; }

    public string District { get; }

    public string Mandal { get; }

    public string Village { get; }

    /// <summary>Local Government Directory village code. When present it identifies the village regardless of spelling or script.</summary>
    public string? LgdVillageCode { get; }

    public SurveyNumber SurveyNumber { get; }

    /// <summary>
    /// Stable identity of the land. Uses the LGD village code when known (spelling-proof), otherwise the
    /// normalised names. Names typed in different scripts will not match; LGD codes fix that and are the
    /// target for every listing.
    /// </summary>
    public string ParcelKey => LgdVillageCode is not null
        ? $"{StateCode}|lgd:{LgdVillageCode}|{SurveyNumber.Normalized}"
        : NameKey;

    /// <summary>
    /// The key from names alone, kept for every parcel even when the LGD code is known. Duplicate checks look
    /// at both keys, so leaving out (or adding) the village code cannot create a second record of the same land.
    /// </summary>
    public string NameKey => $"{StateCode}|{Key(District)}|{Key(Mandal)}|{Key(Village)}|{SurveyNumber.Normalized}";

    public static Result<ParcelLocation> Create(
        string? stateCode, string? district, string? mandal, string? village, string? lgdVillageCode, string? surveyNumber)
    {
        var state = (stateCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!StatePattern().IsMatch(state))
        {
            return Result<ParcelLocation>.Failure("location.state_invalid", "State must be a two-letter code such as TG.");
        }

        var names = new[] { (district, "district"), (mandal, "mandal"), (village, "village") };
        foreach (var (value, field) in names)
        {
            var cleaned = Clean(value);
            if (cleaned.Length is 0 or > 100)
            {
                return Result<ParcelLocation>.Failure($"location.{field}_invalid", $"{char.ToUpperInvariant(field[0])}{field[1..]} is required (up to 100 characters).");
            }
        }

        string? lgd = null;
        if (!string.IsNullOrWhiteSpace(lgdVillageCode))
        {
            lgd = IndianDigits.ToAscii(lgdVillageCode.Trim());
            if (!LgdPattern().IsMatch(lgd))
            {
                return Result<ParcelLocation>.Failure("location.lgd_invalid", "LGD village code must be digits only.");
            }
        }

        var survey = SurveyNumber.Parse(surveyNumber);
        if (!survey.IsSuccess)
        {
            return Result<ParcelLocation>.Failure(survey.Error!);
        }

        return Result<ParcelLocation>.Success(new ParcelLocation(state, Clean(district), Clean(mandal), Clean(village), lgd, survey.Value));
    }

    private static string Clean(string? value) => WhitespacePattern().Replace((value ?? string.Empty).Trim(), " ");

    private static string Key(string value) => Clean(value).ToLowerInvariant();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex StatePattern();

    [GeneratedRegex("^[0-9]{1,10}$")]
    private static partial Regex LgdPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
