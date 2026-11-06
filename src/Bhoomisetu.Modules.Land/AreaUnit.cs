namespace Bhoomisetu.Modules.Land;

/// <summary>
/// Area units in use across Indian land records. Bigha is deliberately absent: its size differs by state
/// (and sometimes by district), so it belongs in a state's jurisdiction pack, not in a national constant.
/// </summary>
public enum AreaUnit
{
    SquareMetre,
    SquareFoot,
    /// <summary>Square yard; called gajam in Telangana and Andhra Pradesh.</summary>
    SquareYard,
    /// <summary>121 square yards (40 guntas = 1 acre).</summary>
    Gunta,
    Acre,
    /// <summary>One hundredth of an acre.</summary>
    Cent,
    Hectare,
    /// <summary>72 square feet; used in Andhra Pradesh.</summary>
    Ankanam,
}

public static class AreaUnitCodes
{
    private static readonly Dictionary<AreaUnit, string> ToCodeMap = new()
    {
        [AreaUnit.SquareMetre] = "square_metre",
        [AreaUnit.SquareFoot] = "square_foot",
        [AreaUnit.SquareYard] = "square_yard",
        [AreaUnit.Gunta] = "gunta",
        [AreaUnit.Acre] = "acre",
        [AreaUnit.Cent] = "cent",
        [AreaUnit.Hectare] = "hectare",
        [AreaUnit.Ankanam] = "ankanam",
    };

    private static readonly Dictionary<string, AreaUnit> FromCodeMap =
        ToCodeMap.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The stable code used in the API, the database and the fixtures.</summary>
    public static string ToCode(this AreaUnit unit) => ToCodeMap[unit];

    public static bool TryParse(string? code, out AreaUnit unit)
    {
        if (code is not null && FromCodeMap.TryGetValue(code.Trim(), out unit))
        {
            return true;
        }

        unit = default;
        return false;
    }

    public static IReadOnlyCollection<AreaUnit> All => ToCodeMap.Keys;
}
