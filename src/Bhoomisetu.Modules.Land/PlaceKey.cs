using System.Text;
using System.Text.RegularExpressions;

namespace Bhoomisetu.Modules.Land;

/// <summary>
/// A sound-alike key for place names, so a village typed in Telugu, Hindi or English letters finds the same
/// land: "మోకిల", "मोकिला" and "Mokila" all become "mukil".
/// <para>
/// Telugu and Devanagari are first written in plain Latin letters (both scripts share one layout, so one table
/// serves both). Then the usual spelling differences are folded away: sh/s, ch/c, w/v, ee/i, oo/u, ow/au,
/// final y/e/i, a final -u or -am, o/u, n/m before a consonant, doubled letters, and the vowels "a" and "e",
/// which English spellings and Hindi pronunciation drop in different places, and the first syllable's vowel
/// (Kurnool / కర్నూలు).
/// </para>
/// <para>
/// The key is only for matching. It is never shown, and it can join two different names that sound alike, so
/// search shows the real village, mandal and district on every result.
/// </para>
/// </summary>
public static partial class PlaceKey
{
    /// <summary>The key for one name, or an empty string when it has no letters.</summary>
    public static string From(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var latin = ToLatin(name).ToLowerInvariant();
        var letters = new StringBuilder(latin.Length);
        foreach (var c in latin)
        {
            if (c is >= 'a' and <= 'z')
            {
                letters.Append(c);
            }
            else if (letters.Length > 0 && letters[^1] != ' ')
            {
                letters.Append(' ');
            }
        }

        var words = letters.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Fold);
        return string.Concat(words);
    }

    private static string Fold(string word)
    {
        var s = word
            .Replace("sh", "s", StringComparison.Ordinal)
            .Replace("ch", "c", StringComparison.Ordinal)
            .Replace("th", "t", StringComparison.Ordinal)
            .Replace("dh", "d", StringComparison.Ordinal)
            .Replace("kh", "k", StringComparison.Ordinal)
            .Replace("gh", "g", StringComparison.Ordinal)
            .Replace("ph", "p", StringComparison.Ordinal)
            .Replace("bh", "b", StringComparison.Ordinal)
            .Replace("jh", "j", StringComparison.Ordinal)
            .Replace("ow", "ou", StringComparison.Ordinal)
            .Replace("au", "ou", StringComparison.Ordinal)
            .Replace("ai", "y", StringComparison.Ordinal)
            .Replace("ee", "i", StringComparison.Ordinal)
            .Replace("oo", "u", StringComparison.Ordinal)
            .Replace('w', 'v')
            .Replace('z', 'j')
            .Replace('q', 'k')
            .Replace('f', 'p')
            .Replace("x", "ks", StringComparison.Ordinal);

        // "Shankarpally", "Shankarpalle" and "Shankarpalli" end the same way.
        if (s.Length > 2 && s[^1] is 'y' or 'e')
        {
            s = s[..^1] + "i";
        }

        // Telugu names end in a vowel or anusvara that English drops or writes as m:
        // గుంటూరు/Guntur, ఒంగోలు/Ongole, ఖమ్మం/Khammam, మహేశ్వరం/Maheshwaram.
        if (s.Length > 3 && s[^1] == 'n' && IsVowel(s[^2]))
        {
            s = s[..^1] + "m";
        }

        if (s.Length > 3 && s[^1] is 'i' or 'u' && !IsVowel(s[^2]))
        {
            s = s[..^1];
        }

        // "Moyinabad" (Telugu య) and "Moinabad": a y gliding into i is not written in English.
        s = GlideY().Replace(s, string.Empty);

        // English and Hindi drop or keep "a" and "e" in different places ("Hyderabad", "हैदराबाद" = haidarabad).
        s = s.Replace("a", string.Empty, StringComparison.Ordinal).Replace("e", string.Empty, StringComparison.Ordinal);

        // "Gachibowli" and "गचीबोवली" (bovli): ov before a consonant is the same sound as ou.
        s = OvBeforeConsonant().Replace(s, "ou");

        // Long o and u are spelled interchangeably in English ("Nellore"/నెల్లూరు, "Kurnool").
        s = s.Replace("ou", "u", StringComparison.Ordinal).Replace('o', 'u');

        // The first syllable's vowel is the one English and Telugu disagree on most ("Kurnool" against కర్నూలు,
        // karnulu): after a first consonant and before another, it is left out.
        if (s.Length > 3 && !IsVowel(s[0]) && IsVowel(s[1]) && !IsVowel(s[2]))
        {
            s = s.Remove(1, 1);
        }

        var folded = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];

            // Anusvara is written n or m in English ("Shamshabad", "Kondapur"): one nasal before a consonant,
            // except at the start of a word ("Medak" keeps its m).
            if (c is 'm' or 'n' && i > 0 && i + 1 < s.Length && s[i + 1] is not ('i' or 'o' or 'u'))
            {
                c = 'n';
            }

            if (folded.Length > 0 && folded[^1] == c)
            {
                continue;
            }

            folded.Append(c);
        }

        return folded.ToString();
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    [GeneratedRegex("(?<=[aeiou])y(?=i)")]
    private static partial Regex GlideY();

    [GeneratedRegex("ov(?=[^iou]|$)")]
    private static partial Regex OvBeforeConsonant();

    /// <summary>Telugu (U+0C00) and Devanagari (U+0900) to plain Latin letters; other text passes through.</summary>
    public static string ToLatin(string text)
    {
        var output = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var offset = c switch
            {
                >= 'ఀ' and <= '౿' => c - 0x0C00,
                >= 'ऀ' and <= 'ॿ' => c - 0x0900,
                _ => -1,
            };

            if (offset < 0)
            {
                if (c is not ('‌' or '‍'))
                {
                    output.Append(c);
                }

                continue;
            }

            if (Consonant(offset) is { } consonant)
            {
                output.Append(consonant);

                // The inherent "a" unless a vowel sign or virama follows (nukta is skipped first).
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (next is '़')
                {
                    i++;
                    next = i + 1 < text.Length ? text[i + 1] : '\0';
                }

                var nextOffset = next switch
                {
                    >= 'ఀ' and <= '౿' => next - 0x0C00,
                    >= 'ऀ' and <= 'ॿ' => next - 0x0900,
                    _ => -1,
                };
                if (nextOffset != 0x4D && VowelSign(nextOffset) is null)
                {
                    output.Append('a');
                }

                continue;
            }

            output.Append(VowelSign(offset) ?? IndependentVowel(offset) ?? (offset switch
            {
                0x01 or 0x02 => "n",
                0x03 => "h",
                _ => string.Empty,
            }));
        }

        return output.ToString();
    }

    private static string? IndependentVowel(int offset) => offset switch
    {
        0x05 or 0x06 => "a",
        0x07 or 0x08 => "i",
        0x09 or 0x0A => "u",
        0x0B => "ru",
        0x0D or 0x0E or 0x0F => "e",
        0x10 => "ai",
        0x11 or 0x12 or 0x13 => "o",
        0x14 => "au",
        _ => null,
    };

    private static string? VowelSign(int offset) => offset switch
    {
        0x3E => "a",
        0x3F or 0x40 => "i",
        0x41 or 0x42 => "u",
        0x43 or 0x44 => "ru",
        0x45 or 0x46 or 0x47 => "e",
        0x48 => "ai",
        0x49 or 0x4A or 0x4B => "o",
        0x4C => "au",
        _ => null,
    };

    private static string? Consonant(int offset) => offset switch
    {
        0x15 or 0x16 => "k",
        0x17 or 0x18 => "g",
        0x19 or 0x1E or 0x23 or 0x28 or 0x29 => "n",
        0x1A or 0x1B => "ch",
        0x1C or 0x1D => "j",
        0x1F or 0x20 or 0x24 or 0x25 => "t",
        0x21 or 0x22 or 0x26 or 0x27 => "d",
        0x2A or 0x2B => "p",
        0x2C or 0x2D => "b",
        0x2E => "m",
        0x2F => "y",
        0x30 or 0x31 => "r",
        0x32 or 0x33 or 0x34 => "l",
        0x35 => "v",
        0x36 or 0x37 => "sh",
        0x38 => "s",
        0x39 => "h",
        _ => null,
    };
}
