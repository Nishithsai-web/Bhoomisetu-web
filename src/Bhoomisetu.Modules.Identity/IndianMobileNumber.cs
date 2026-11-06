using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Identity;

/// <summary>
/// An Indian mobile number in E.164 form (+91 followed by 10 digits starting 6 to 9). Accepts the ways people
/// actually type it: with or without +91 or 91, a leading 0, spaces, dashes, brackets, and Telugu or
/// Devanagari digits. Landlines are rejected because OTP must go to a mobile.
/// </summary>
public sealed record IndianMobileNumber
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

        var ascii = IndianDigits.ToAscii(input.Trim());
        var hasPlus = ascii.Contains('+');
        var digits = new string(ascii.Where(char.IsAsciiDigit).ToArray());
        var rest = ascii.Where(c => !char.IsAsciiDigit(c)).ToArray();
        if (rest.Any(c => c is not (' ' or '-' or '(' or ')' or '+')) || rest.Count(c => c == '+') > 1)
        {
            return Result<IndianMobileNumber>.Failure("phone.invalid", "Enter a 10-digit Indian mobile number.");
        }

        string national;
        if (hasPlus)
        {
            if (digits.Length != 12 || !digits.StartsWith("91", StringComparison.Ordinal))
            {
                return Result<IndianMobileNumber>.Failure("phone.not_indian", "Only Indian mobile numbers (+91) are supported.");
            }

            national = digits[2..];
        }
        else
        {
            national = digits.Length switch
            {
                10 => digits,
                11 when digits[0] == '0' => digits[1..],
                12 when digits.StartsWith("91", StringComparison.Ordinal) => digits[2..],
                _ => string.Empty,
            };
        }

        if (national.Length != 10 || national[0] is < '6' or > '9')
        {
            return Result<IndianMobileNumber>.Failure("phone.invalid", "Enter a 10-digit Indian mobile number.");
        }

        return Result<IndianMobileNumber>.Success(new IndianMobileNumber("+91" + national));
    }
}
