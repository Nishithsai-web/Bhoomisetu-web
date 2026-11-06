using Bhoomisetu.Modules.Land;

namespace Bhoomisetu.Api.Endpoints;

public sealed record AreaConversionResponse(decimal Value, string Unit, decimal SquareMetres, IReadOnlyDictionary<string, decimal> Units);

public static class UnitsEndpoints
{
    public static void MapUnitsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/units/area", (decimal? value, string? unit) =>
        {
            if (value is null)
            {
                return ApiResults.Invalid("area.value_required", "Give a value to convert.");
            }

            if (!AreaUnitCodes.TryParse(unit, out var parsedUnit))
            {
                return ApiResults.Invalid("area.unit_invalid", $"Unit must be one of: {string.Join(", ", AreaUnitCodes.All.Select(u => u.ToCode()))}.");
            }

            var area = Area.From(value.Value, parsedUnit);
            if (!area.IsSuccess)
            {
                return ApiResults.Invalid(area.Error!);
            }

            return Results.Ok(ToResponse(value.Value, parsedUnit, area.Value));
        }).WithTags("Units");
    }

    public static AreaConversionResponse ToResponse(decimal value, AreaUnit unit, Area area) =>
        new(value, unit.ToCode(), Math.Round(area.SquareMetres, 4, MidpointRounding.AwayFromZero),
            area.InAllUnits().ToDictionary(pair => pair.Key.ToCode(), pair => pair.Value));
}
