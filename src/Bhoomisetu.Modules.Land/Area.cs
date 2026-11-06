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

    public static Result<Area> From(decimal value, AreaUnit unit)
    {
        if (value <= 0)
        {
            return Result<Area>.Failure("area.not_positive", "Area must be greater than zero.");
        }

        var sqm = value * FactorToSquareMetres(unit);
        if (sqm > MaxSquareMetres)
        {
            return Result<Area>.Failure("area.too_large", "Area is larger than any single parcel we accept.");
        }

        return Result<Area>.Success(new Area(sqm));
    }

    public decimal In(AreaUnit unit) => SquareMetres / FactorToSquareMetres(unit);

    /// <summary>The same area in every supported unit, rounded for display.</summary>
    public IReadOnlyDictionary<AreaUnit, decimal> InAllUnits(int decimals = 6) =>
        AreaUnitCodes.All.ToDictionary(unit => unit, unit => Math.Round(In(unit), decimals, MidpointRounding.AwayFromZero));
}
