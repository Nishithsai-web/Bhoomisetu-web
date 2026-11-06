using System.Text;

namespace Bhoomisetu.SharedKernel;

/// <summary>Converts Telugu (౦-౯) and Devanagari (०-९) digits to ASCII so numbers typed in any of our languages parse the same.</summary>
public static class IndianDigits
{
    public static string ToAscii(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                >= '౦' and <= '౯' => (char)('0' + (ch - '౦')), // Telugu
                >= '०' and <= '९' => (char)('0' + (ch - '०')), // Devanagari
                _ => ch,
            });
        }

        return builder.ToString();
    }
}
