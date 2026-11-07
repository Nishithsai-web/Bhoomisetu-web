using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Land;

/// <summary>An area, stored canonically in square metres. Exact decimal factors, no floating point.</summary>
public readonly record struct Area
{
    // Exact by definition: 1 ft = 0.3048 m, 1 yd = 0.9144 m.
    private const decimal SquareFootInSqm = 0.09290304m;
    private const decimal SquareYardInSqm = 0.83612736m;

    private Area(decimal squareMetres) => SquareMetres = squareMetres;

    public decimal SquareMetres { get; }

    public static decimal FactorToSquareMetres(AreaUnit unit) => unit switch
    {
        AreaUnit.SquareMetre => 1m,
        AreaUnit.SquareFoot => SquareFootInSqm,
        AreaUnit.SquareYard => SquareYardInSqm,
        AreaUnit.Gunta => 121m * SquareYardInSqm,
        AreaUnit.Acre => 4840m * SquareYardInSqm,
        AreaUnit.Cent => 48.4m * SquareYardInSqm,
        AreaUnit.Hectare => 10_000m,
        AreaUnit.Ankanam => 72m * SquareFootInSqm,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown area unit."),
    };

    /// <summary>Largest area we accept: 100,000 hectares, far above any single parcel, to catch typing errors.</summary>
    public const decimal MaxSquareMetres = 1_000_000_000m;

    /// <summary>Smallest area we accept: 0.01 m². Anything smaller is a typing error and would round to zero in storage.</summary>
    public const decimal MinSquareMetres = 0.01m;

    /// <summary>Largest number a person may type (fits the database column numeric(14,4)).</summary>
    public const decimal MaxEnteredValue = 9_999_999_999m;

    /// <summary>Entered values are kept to 4 decimals, the precision stored in the database.</summary>
    public static decimal NormaliseEntered(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    public static Result<Area> From(decimal value, AreaUnit unit)
    {
        var entered = NormaliseEntered(value);
        if (entered <= 0)
        {
            return Result<Area>.Failure("area.not_positive", "Area must be greater than zero.");
        }

        if (entered > MaxEnteredValue)
        {
            return Result<Area>.Failure("area.too_large", "Area is larger than any single parcel we accept.");
        }

        decimal sqm;
        try
        {
            sqm = entered * FactorToSquareMetres(unit);
        }
        catch (OverflowException)
        {
            return Result<Area>.Failure("area.too_large", "Area is larger than any single parcel we accept.");
        }

        if (sqm > MaxSquareMetres)
        {
            return Result<Area>.Failure("area.too_large", "Area is larger than any single parcel we accept.");
        }

        if (sqm < MinSquareMetres)
        {
            return Result<Area>.Failure("area.too_small", "Area is too small for a piece of land. Check the number and the unit.");
        }

        return Result<Area>.Success(new Area(sqm));
    }

    public decimal In(AreaUnit unit) => SquareMetres / FactorToSquareMetres(unit);

    /// <summary>The same area in every supported unit, rounded for display.</summary>
    public IReadOnlyDictionary<AreaUnit, decimal> InAllUnits(int decimals = 6)
    {
        // A lambda inside a struct cannot capture `this`, so take a copy first.
        var area = this;
        return AreaUnitCodes.All.ToDictionary(unit => unit, unit => Math.Round(area.In(unit), decimals, MidpointRounding.AwayFromZero));
    }
}
