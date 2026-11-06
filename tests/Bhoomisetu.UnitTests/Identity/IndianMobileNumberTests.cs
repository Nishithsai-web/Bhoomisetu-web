using Bhoomisetu.Modules.Identity;
using Xunit;

namespace Bhoomisetu.UnitTests.Identity;

public sealed class IndianMobileNumberTests
{
    [Theory]
    [InlineData("9876543210")]
    [InlineData("+919876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("919876543210")]
    [InlineData("09876543210")]
    [InlineData("(+91) 98765 43210")]
    [InlineData("+91 (0) 98765-43210")]
    [InlineData("+91 0 98765 43210")]
    [InlineData("0091 98765 43210")]
    [InlineData("+91.98765.43210")]
    [InlineData("౯౮౭౬౫౪౩౨౧౦")] // Telugu digits
    [InlineData("९८७६५४३२१०")] // Devanagari digits
    public void Accepts_common_ways_of_writing_a_mobile(string input)
    {
        var result = IndianMobileNumber.Parse(input);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("+919876543210", result.Value.E164);
    }

    [Theory]
    [InlineData("5876543210", "phone.invalid")] // must start with 6-9
    [InlineData("987654321", "phone.invalid")] // 9 digits
    [InlineData("98765432101", "phone.invalid")] // 11 digits without leading 0
    [InlineData("+14155550100", "phone.not_indian")]
    [InlineData("+9198765432", "phone.invalid")] // Indian prefix but too short
    [InlineData("98765x43210", "phone.invalid")]
    [InlineData("++919876543210", "phone.invalid")]
    [InlineData("91+9876543210", "phone.invalid")]
    [InlineData("0044 7700 900123", "phone.not_indian")]
    [InlineData("", "phone.empty")]
    [InlineData(null, "phone.empty")]
    public void Rejects_bad_numbers(string? input, string code)
    {
        Assert.Equal(code, IndianMobileNumber.Parse(input).Error!.Code);
    }

    [Fact]
    public void Masks_all_but_last_four_digits()
    {
        Assert.Equal("+91******3210", IndianMobileNumber.Parse("9876543210").Value.Masked);
    }
}
