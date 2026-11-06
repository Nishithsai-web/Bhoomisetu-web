using System.Text.RegularExpressions;
using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Identity;

/// <summary>
/// An Indian mobile number in E.164 form (+91 followed by 10 digits starting 6 to 9). Accepts the ways people
/// actually type it: with or without +91 or 91, a leading 0, spaces, dashes, brackets, and Telugu or
/// Devanagari digits. Landlines are rejected because OTP must go to a mobile.
/// </summary>
public sealed partial record IndianMobileNumber
{
    private IndianMobileNumber(string e164) => E164 = e164;

    public string E164 { get; }

    /// <summary>Last four digits only, for logs and support screens.</summary>
    public string Masked => $"+91******{E164[^4..]}";

    public static Result<IndianMobileNumber> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<IndianMobileNumber>.Failure("phone.empty", "Mobile number is required.");
        }

        // Remove the separators people type; anything else that is not a digit or a leading + is an error.
        var compact = SeparatorPattern().Replace(IndianDigits.ToAscii(input.Trim()), string.Empty);
        if (!ShapePattern().IsMatch(compact))
        {
            return Result<IndianMobileNumber>.Failure("phone.invalid", "Enter a 10-digit Indian mobile number.");
        }

        // International form: +91 or 0091, optionally followed by the trunk 0 some people still type.
        var international = InternationalPattern().Match(compact);
        string national;
        if (international.Success)
        {
            national = international.Groups["number"].Value;
        }
        else if (compact.StartsWith('+') || compact.StartsWith("00", StringComparison.Ordinal))
        {
            return Result<IndianMobileNumber>.Failure("phone.not_indian", "Only Indian mobile numbers (+91) are supported.");
        }
        else
        {
            national = compact.Length switch
            {
                10 => compact,
                11 when compact[0] == '0' => compact[1..],
                12 when compact.StartsWith("91", StringComparison.Ordinal) => compact[2..],
                _ => string.Empty,
            };
        }

        if (national.Length != 10 || national[0] is < '6' or > '9')
        {
            return Result<IndianMobileNumber>.Failure("phone.invalid", "Enter a 10-digit Indian mobile number.");
        }

        return Result<IndianMobileNumber>.Success(new IndianMobileNumber("+91" + national));
    }

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex SeparatorPattern();

    /// <summary>Digits only, with at most one + and only at the very start.</summary>
    [GeneratedRegex(@"^\+?[0-9]+$")]
    private static partial Regex ShapePattern();

    [GeneratedRegex(@"^(?:\+91|0091)0?(?<number>[0-9]+)$")]
    private static partial Regex InternationalPattern();
}
