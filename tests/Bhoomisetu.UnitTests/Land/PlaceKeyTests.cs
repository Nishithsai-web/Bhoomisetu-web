using Bhoomisetu.Modules.Land;
using Xunit;

namespace Bhoomisetu.UnitTests.Land;

public sealed class PlaceKeyTests
{
    [Theory]
    [InlineData("Mokila", "మోకిల", "मोकिला")]
    [InlineData("Shankarpalle", "శంకర్‌పల్లి", "शंकरपल्ली")]
    [InlineData("Shankarpally", "Shankarpalli", "SHANKARPALLE")]
    [InlineData("Shamshabad", "శంషాబాద్", "शमशाबाद")]
    [InlineData("Kukatpally", "కూకట్‌పల్లి", "कूकटपल्ली")]
    [InlineData("Hyderabad", "హైదరాబాద్", "हैदराबाद")]
    [InlineData("Ranga Reddy", "రంగారెడ్డి", "रंगारेड्डी")]
    [InlineData("Medchal", "మేడ్చల్", "मेडचल")]
    [InlineData("Gachibowli", "గచ్చిబౌలి", "गचीबोवली")]
    [InlineData("Kondapur", "కొండాపూర్", "कोंडापुर")]
    [InlineData("Sangareddy", "సంగారెడ్డి", "संगारेड्डी")]
    [InlineData("Vikarabad", "వికారాబాద్", "विकाराबाद")]
    [InlineData("Chevella", "చేవెళ్ల", "चेवेल्ला")]
    [InlineData("Moinabad", "మొయినాబాద్", "मोइनाबाद")]
    [InlineData("Guntur", "గుంటూరు", "गुंटूर")]
    [InlineData("Ongole", "ఒంగోలు", "ओंगोल")]
    [InlineData("Nellore", "నెల్లూరు", "नेल्लोर")]
    [InlineData("Khammam", "ఖమ్మం", "खम्मम")]
    [InlineData("Maheshwaram", "మహేశ్వరం", "महेश्वरम")]
    [InlineData("Warangal", "వరంగల్", "वरंगल")]
    [InlineData("Nizamabad", "నిజామాబాద్", "निजामाबाद")]
    [InlineData("Karimnagar", "కరీంనగర్", "करीमनगर")]
    [InlineData("Tirupati", "తిరుపతి", "तिरुपति")]
    [InlineData("Vijayawada", "విజయవాడ", "विजयवाड़ा")]
    [InlineData("Visakhapatnam", "విశాఖపట్నం", "विशाखापट्टनम")]
    [InlineData("Kokapet", "కోకాపేట్", "कोकापेट")]
    [InlineData("Narsingi", "నార్సింగి", "नारसिंगी")]
    [InlineData("Adibatla", "ఆదిభట్ల", "आदिभट्ला")]
    public void The_same_place_in_three_scripts_has_one_key(string english, string telugu, string hindi)
    {
        var key = PlaceKey.From(english);
        Assert.NotEmpty(key);
        Assert.Equal(key, PlaceKey.From(telugu));
        Assert.Equal(key, PlaceKey.From(hindi));
    }

    [Theory]
    [InlineData("Mokila", "Moinabad")]
    [InlineData("Kandi", "Kondapur")]
    [InlineData("Medak", "Medchal")]
    [InlineData("Gopanpally", "Gachibowli")]
    public void Different_places_keep_different_keys(string a, string b) => Assert.NotEqual(PlaceKey.From(a), PlaceKey.From(b));

    [Fact]
    public void A_shorter_english_name_is_the_start_of_the_telugu_key() =>
        Assert.StartsWith(PlaceKey.From("Anantapur"), PlaceKey.From("అనంతపురం"), StringComparison.Ordinal);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  123 / - ")]
    public void Text_without_letters_has_an_empty_key(string? value) => Assert.Equal(string.Empty, PlaceKey.From(value));
}
