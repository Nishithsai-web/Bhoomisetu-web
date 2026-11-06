using Bhoomisetu.Modules.Land;
using Xunit;

namespace Bhoomisetu.UnitTests.Land;

public sealed class SurveyNumberAndLocationTests
{
    [Theory]
    [InlineData("123/A/1", "123/A/1")]
    [InlineData(" 123 / a / 1 ", "123/A/1")]
    [InlineData("123-A-1", "123/A/1")]
    [InlineData("123.A.1", "123/A/1")]
    [InlineData("0123/A/01", "123/A/1")]
    [InlineData("123//A", "123/A")]
    [InlineData("123A", "123A")]
    [InlineData("౧౨౩/అ", "123/అ")] // Telugu letters are kept as printed
    [InlineData("45/ఆ", "45/ఆ")]
    [InlineData("७/क", "7/क")] // Devanagari khasra sub-division
    [InlineData("0123A", "123A")] // leading zeros in a mixed part
    [InlineData("0A/1", "0A/1")] // a zero followed by a letter is kept
    [InlineData("45/\u0C06\u200C", "45/\u0C06")] // zero-width non-joiner from a phone keyboard
    [InlineData("12/\u0964", null)] // Devanagari danda is punctuation
    [InlineData("౧౨౩/A", "123/A")] // Telugu digits
    [InlineData("१२३/A", "123/A")] // Devanagari digits
    [InlineData("0", "0")]
    public void Normalises_survey_numbers(string input, string? expected)
    {
        var result = SurveyNumber.Parse(input);
        if (expected is null)
        {
            Assert.False(result.IsSuccess);
            return;
        }

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(expected, result.Value.Normalized);
        Assert.Equal(input.Trim(), result.Value.Raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("///")]
    public void Rejects_empty_survey_numbers(string? input)
    {
        Assert.Equal("survey_number.empty", SurveyNumber.Parse(input).Error!.Code);
    }

    [Theory]
    [InlineData("12#3")]
    [InlineData("ABC")]
    public void Rejects_invalid_survey_numbers(string input)
    {
        Assert.Equal("survey_number.invalid", SurveyNumber.Parse(input).Error!.Code);
    }

    [Fact]
    public void Rejects_overlong_survey_numbers()
    {
        Assert.Equal("survey_number.too_long", SurveyNumber.Parse(new string('1', 41)).Error!.Code);
    }

    [Fact]
    public void Same_land_typed_differently_gets_the_same_key()
    {
        var a = ParcelLocation.Create("tg", "Ranga Reddy", "Shankarpalle", "Mokila", null, "123/A/1").Value;
        var b = ParcelLocation.Create("TG", "  ranga   reddy ", "SHANKARPALLE", "mokila", null, "0123-a-1").Value;
        Assert.Equal(a.ParcelKey, b.ParcelKey);
    }

    [Fact]
    public void Name_key_is_the_same_with_or_without_the_lgd_code()
    {
        var withCode = ParcelLocation.Create("TG", "Rangareddy", "Shankarpalle", "Mokila", "574123", "123").Value;
        var withoutCode = ParcelLocation.Create("TG", "Rangareddy", "Shankarpalle", "Mokila", null, "123").Value;
        Assert.NotEqual(withCode.ParcelKey, withoutCode.ParcelKey);
        Assert.Equal(withCode.NameKey, withoutCode.NameKey);
        Assert.Equal(withoutCode.ParcelKey, withoutCode.NameKey);
    }

    [Fact]
    public void Non_breaking_spaces_in_names_match_normal_spaces()
    {
        var a = ParcelLocation.Create("TG", "Ranga\u00A0Reddy", "Shankarpalle", "Mokila", null, "123").Value;
        var b = ParcelLocation.Create("TG", "ranga reddy", "Shankarpalle", "Mokila", null, "123").Value;
        Assert.Equal(a.NameKey, b.NameKey);
    }

    [Fact]
    public void Lgd_code_overrides_name_spelling()
    {
        var a = ParcelLocation.Create("TG", "Rangareddy", "Shankarpalle", "Mokila", "574123", "123").Value;
        var b = ParcelLocation.Create("TG", "Ranga Reddy", "Shankarpally", "Mokila village", "574123", "123").Value;
        Assert.Equal(a.ParcelKey, b.ParcelKey);
        Assert.Equal("TG|lgd:574123|123", a.ParcelKey);
    }

    [Fact]
    public void Different_survey_numbers_get_different_keys()
    {
        var a = ParcelLocation.Create("TG", "Rangareddy", "Shankarpalle", "Mokila", null, "123/A").Value;
        var b = ParcelLocation.Create("TG", "Rangareddy", "Shankarpalle", "Mokila", null, "123/B").Value;
        Assert.NotEqual(a.ParcelKey, b.ParcelKey);
    }

    [Theory]
    [InlineData("T", "d", "m", "v", null, "1", "location.state_invalid")]
    [InlineData("TG", "", "m", "v", null, "1", "location.district_invalid")]
    [InlineData("TG", "d", " ", "v", null, "1", "location.mandal_invalid")]
    [InlineData("TG", "d", "m", null, null, "1", "location.village_invalid")]
    [InlineData("TG", "d", "m", "v", "12a", "1", "location.lgd_invalid")]
    [InlineData("TG", "d", "m", "v", null, "", "survey_number.empty")]
    public void Rejects_incomplete_locations(string? state, string? district, string? mandal, string? village, string? lgd, string? survey, string code)
    {
        Assert.Equal(code, ParcelLocation.Create(state, district, mandal, village, lgd, survey).Error!.Code);
    }
}
