using Bhoomisetu.Modules.Land;
using Xunit;

namespace Bhoomisetu.UnitTests.Land;

public sealed class NearbyComparisonTests
{
    private static readonly ComparablePlace Ours = new(PricePerUnit: 20_000m, Area: 200m, Kind: "non_agricultural", RoadWidthM: 9.14m, DistanceToMainRoadM: 500);

    private static string[] Codes(ComparablePlace other) => NearbyComparison.Compare(Ours, other).Select(p => p.Code).ToArray();

    [Fact]
    public void Cheaper_per_square_yard_with_a_wider_road_and_closer_main_road_are_pros()
    {
        var points = NearbyComparison.Compare(Ours, Ours with { PricePerUnit = 16_400m, RoadWidthM = 12.19m, DistanceToMainRoadM = 100 });
        var cheaper = Assert.Single(points, p => p.Code == "cheaper");
        Assert.Equal(PointKind.Pro, cheaper.Kind);
        Assert.Equal("18", cheaper.Values["percent"]);
        Assert.Equal(PointKind.Pro, Assert.Single(points, p => p.Code == "road_wider").Kind);
        Assert.Equal("12.2", Assert.Single(points, p => p.Code == "road_wider").Values["theirs"]);
        Assert.Contains(points, p => p.Code == "main_road_closer" && p.Kind == PointKind.Pro);
    }

    [Fact]
    public void Costlier_narrower_road_and_no_price_are_cons()
    {
        Assert.Contains("costlier", Codes(Ours with { PricePerUnit = 23_000m }));
        Assert.Contains("road_narrower", Codes(Ours with { RoadWidthM = 6m }));
        Assert.Contains("road_unknown", Codes(Ours with { RoadWidthM = null }));
        Assert.Contains("no_price", Codes(Ours with { PricePerUnit = null }));
    }

    [Fact]
    public void Small_differences_are_not_called_out()
    {
        var codes = Codes(Ours with { PricePerUnit = 20_500m, Area = 210m, RoadWidthM = 9.5m, DistanceToMainRoadM = 550 });
        Assert.Equal(["similar_price"], codes);
    }

    [Fact]
    public void Checks_are_compared_only_when_either_side_has_one()
    {
        Assert.DoesNotContain("owner_not_recorded", Codes(Ours));
        var withChecks = NearbyComparison.Compare(Ours with { OwnerRecorded = true }, Ours with { ReviewedDocuments = 2 }).Select(p => p.Code).ToArray();
        Assert.Contains("owner_not_recorded", withChecks);
        Assert.Contains("documents_reviewed", withChecks);
    }

    [Fact]
    public void Homes_to_rent_compare_bedrooms_deposit_and_furnishing()
    {
        var home = new ComparablePlace(PricePerUnit: 18m, Area: 1200m, Kind: "apartment", Bedrooms: 2, DepositMonths: 3, Furnishing: "semi");
        var points = NearbyComparison.Compare(home, home with { PricePerUnit = 15m, Bedrooms = 3, DepositMonths = 1, Furnishing = "full" });
        Assert.Contains(points, p => p.Code == "cheaper" && p.Values["percent"] == "17");
        Assert.Contains(points, p => p.Code == "more_bedrooms" && p.Kind == PointKind.Info);
        Assert.Contains(points, p => p.Code == "lower_deposit" && p.Kind == PointKind.Pro);
        Assert.Contains(points, p => p.Code == "furnishing" && p.Values["furnishing"] == "full");
    }
}
