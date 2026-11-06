using System.Text;
using System.Text.RegularExpressions;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Land;

/// <summary>
/// A survey (or khasra) number such as 123/A/1 or 45/ఆ, normalised so the same land typed differently
/// matches: upper case, Telugu and Devanagari digits converted to 0-9, separators (/ - . \ and spaces)
/// unified to "/", leading zeros removed from each part ("0123A" becomes "123A").
/// Telugu and Devanagari letters are kept as printed (invisible joiners removed, Unicode NFC), but the danda
/// (। ॥) is punctuation and rejected; they are not transliterated,
/// because a mapping such as ఆ to A is ambiguous. Letters and digits inside one part are never split
/// ("123A" stays "123A"), because "123A" and "123/A" can be different sub-divisions.
/// </summary>
public sealed partial record SurveyNumber
{
    private SurveyNumber(string raw, string normalized)
    {
        Raw = raw;
        Normalized = normalized;
    }

    public string Raw { get; }

    public string Normalized { get; }

    public const int MaxLength = 40;

    public static Result<SurveyNumber> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<SurveyNumber>.Failure("survey_number.empty", "Survey number is required.");
        }

        var raw = input.Trim();
        // Phone keyboards insert invisible joiners into Telugu and Hindi text, and the same letter can arrive
        // in composed or decomposed form; remove the joiners and use one form so equal numbers compare equal.
        var visible = InvisiblePattern().Replace(raw, string.Empty).Normalize(NormalizationForm.FormC);
        var ascii = IndianDigits.ToAscii(visible).ToUpperInvariant();
        var parts = SeparatorPattern().Split(ascii).Where(part => part.Length > 0).ToArray();
        if (parts.Length == 0)
        {
            return Result<SurveyNumber>.Failure("survey_number.empty", "Survey number is required.");
        }

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (!PartPattern().IsMatch(part) || part.Contains('\u0964') || part.Contains('\u0965'))
            {
                return Result<SurveyNumber>.Failure(
                    "survey_number.invalid",
                    "Survey number may contain only digits, English, Telugu or Hindi letters and separators such as / or -.");
            }

            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(TrimLeadingZeros(part));
        }

        var normalized = builder.ToString();
        if (normalized.Length > MaxLength || raw.Length > 60)
        {
            return Result<SurveyNumber>.Failure("survey_number.too_long", "Survey number is too long.");
        }

        if (!normalized.Any(char.IsAsciiDigit))
        {
            return Result<SurveyNumber>.Failure("survey_number.invalid", "Survey number must contain at least one digit.");
        }

        return Result<SurveyNumber>.Success(new SurveyNumber(raw, normalized));
    }

    /// <summary>Removes zeros at the start of a part when a digit follows them: "007" → "7", "0123A" → "123A", "0" and "0A" stay.</summary>
    private static string TrimLeadingZeros(string part) => LeadingZerosPattern().Replace(part, string.Empty);

    [GeneratedRegex(@"[\s/\\\-.]+")]
    private static partial Regex SeparatorPattern();

    // Digits, A-Z, and the Telugu (U+0C00-U+0C7F) and Devanagari (U+0900-U+097F) blocks, including vowel signs.
    [GeneratedRegex("^[0-9A-Z\u0C00-\u0C7F\u0900-\u097F]+$")]
    private static partial Regex PartPattern();

    // Zero-width space, non-joiner, joiner and byte-order mark.
    [GeneratedRegex("[\u200B\u200C\u200D\uFEFF]")]
    private static partial Regex InvisiblePattern();

    [GeneratedRegex("^0+(?=[0-9])")]
    private static partial Regex LeadingZerosPattern();
}
