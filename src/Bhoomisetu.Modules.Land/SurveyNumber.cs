using System.Text;
using System.Text.RegularExpressions;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Land;

/// <summary>
/// A survey (or khasra) number such as 123/A/1, normalised so the same land typed differently matches:
/// upper case, Telugu and Devanagari digits converted, separators (/ - . \ and spaces) unified to "/",
/// leading zeros removed from numeric parts. Letters and digits inside one part are never split
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
        var ascii = IndianDigits.ToAscii(raw).ToUpperInvariant();
        var parts = SeparatorPattern().Split(ascii).Where(part => part.Length > 0).ToArray();
        if (parts.Length == 0)
        {
            return Result<SurveyNumber>.Failure("survey_number.empty", "Survey number is required.");
        }

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (!PartPattern().IsMatch(part))
            {
                return Result<SurveyNumber>.Failure(
                    "survey_number.invalid",
                    "Survey number may contain only letters, digits and separators such as / or -.");
            }

            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(part.All(char.IsAsciiDigit) ? TrimLeadingZeros(part) : part);
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

    private static string TrimLeadingZeros(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    [GeneratedRegex(@"[\s/\\\-.]+")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex("^[0-9A-Z]+$")]
    private static partial Regex PartPattern();
}
