using System.Text.Json;
using Bhoomisetu.Modules.Land;
using Xunit;

namespace Bhoomisetu.UnitTests.Land;

public sealed class AreaTests
{
    public static TheoryData<int> FixtureCases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < LoadFixture().GetProperty("cases").GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void Converts_like_the_shared_fixture(int index)
    {
        var testCase = LoadFixture().GetProperty("cases")[index];
        Assert.True(AreaUnitCodes.TryParse(testCase.GetProperty("unit").GetString(), out var unit));
        var area = Area.From(testCase.GetProperty("value").GetDecimal(), unit);
        Assert.True(area.IsSuccess);

        foreach (var expected in testCase.GetProperty("expected").EnumerateObject())
        {
            Assert.True(AreaUnitCodes.TryParse(expected.Name, out var target));
            var actual = area.Value.In(target);
            Assert.True(Math.Abs(actual - expected.Value.GetDecimal()) <= 0.000001m,
                $"{testCase.GetProperty("value")} {unit} in {target}: expected {expected.Value}, got {actual}");
        }
    }

    [Theory]
    [InlineData(1, AreaUnit.Acre, AreaUnit.Gunta, 40)]
    [InlineData(1, AreaUnit.Gunta, AreaUnit.SquareYard, 121)]
    [InlineData(1, AreaUnit.Acre, AreaUnit.SquareYard, 4840)]
    [InlineData(1, AreaUnit.Acre, AreaUnit.Cent, 100)]
    [InlineData(1, AreaUnit.SquareYard, AreaUnit.SquareFoot, 9)]
    [InlineData(1, AreaUnit.Acre, AreaUnit.Ankanam, 605)]
    public void Exact_textbook_relationships_hold(int value, AreaUnit from, AreaUnit to, int expected)
    {
        Assert.Equal(expected, Area.From(value, from).Value.In(to));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_non_positive_area(int value)
    {
        var result = Area.From(value, AreaUnit.Acre);
        Assert.False(result.IsSuccess);
        Assert.Equal("area.not_positive", result.Error!.Code);
    }

    [Theory]
    [InlineData("0.001", AreaUnit.SquareFoot, "area.too_small")] // 0.0000929 m²
    [InlineData("0.00001", AreaUnit.Acre, "area.not_positive")] // rounds to 0 at 4 decimals
    [InlineData("79228162514264337593543950335", AreaUnit.Acre, "area.too_large")] // decimal.MaxValue
    [InlineData("10000000000", AreaUnit.SquareFoot, "area.too_large")] // does not fit numeric(14,4)
    public void Rejects_values_the_database_cannot_hold(string value, AreaUnit unit, string code)
    {
        var result = Area.From(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), unit);
        Assert.Equal(code, result.Error!.Code);
    }

    [Fact]
    public void Entered_value_is_kept_to_four_decimals()
    {
        Assert.Equal(0.1235m, Area.NormaliseEntered(0.12345m));
        Assert.Equal(Area.From(0.1235m, AreaUnit.Acre).Value, Area.From(0.12345m, AreaUnit.Acre).Value);
    }

    [Fact]
    public void Rejects_absurdly_large_area()
    {
        var result = Area.From(200_000, AreaUnit.Hectare);
        Assert.Equal("area.too_large", result.Error!.Code);
    }

    [Theory]
    [InlineData("acre", AreaUnit.Acre)]
    [InlineData(" Square_Yard ", AreaUnit.SquareYard)]
    [InlineData("GUNTA", AreaUnit.Gunta)]
    public void Parses_unit_codes_case_insensitively(string code, AreaUnit expected)
    {
        Assert.True(AreaUnitCodes.TryParse(code, out var unit));
        Assert.Equal(expected, unit);
    }

    [Theory]
    [InlineData("bigha")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_unknown_units(string? code)
    {
        Assert.False(AreaUnitCodes.TryParse(code, out _));
    }

    [Fact]
    public void Every_unit_has_a_round_trip_code()
    {
        foreach (var unit in Enum.GetValues<AreaUnit>())
        {
            Assert.True(AreaUnitCodes.TryParse(unit.ToCode(), out var parsed));
            Assert.Equal(unit, parsed);
        }
    }

    private static JsonElement LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "area-conversions.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
