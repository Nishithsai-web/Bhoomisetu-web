using System.Globalization;

namespace Bhoomisetu.Modules.Land;

/// <summary>Whether a comparison point favours the nearby listing (Pro), counts against it (Con), or is neutral.</summary>
public enum PointKind
{
    Pro,
    Con,
    Info,
}

/// <summary>One plain fact about a nearby listing compared with the one being viewed. Code is translated by the app.</summary>
public sealed record ComparisonPoint(string Code, PointKind Kind, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// What is compared. For land: price per square yard, area in square yards, road and documents. For homes to
/// rent: rent per square foot, area in square feet, bedrooms, deposit and furnishing. Unknown facts are null.
/// </summary>
public sealed record ComparablePlace(
    decimal? PricePerUnit,
    decimal Area,
    string Kind,
    decimal? RoadWidthM = null,
    int? DistanceToMainRoadM = null,
    bool OwnerRecorded = false,
    int ReviewedDocuments = 0,
    int? Bedrooms = null,
    decimal? DepositMonths = null,
    string? Furnishing = null);

/// <summary>
/// Pros and cons of a nearby listing against the one being viewed, worked out only from recorded facts with
/// fixed thresholds, so the same data always gives the same points. Nothing is guessed or written by AI.
/// </summary>
public static class NearbyComparison
{
    /// <summary>Price differences under 5% are "about the same".</summary>
    public const decimal PriceThreshold = 0.05m;

    /// <summary>Road widths closer than 1 m (about 3 ft) are not called wider or narrower.</summary>
    public const decimal RoadThresholdM = 1m;

    /// <summary>Distances to the main road closer than 100 m are not called closer or farther.</summary>
    public const int MainRoadThresholdM = 100;

    /// <summary>Areas within 10% of each other are not called bigger or smaller.</summary>
    public const decimal AreaThreshold = 0.10m;

    public static IReadOnlyList<ComparisonPoint> Compare(ComparablePlace subject, ComparablePlace other)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(other);
        var points = new List<ComparisonPoint>();

        // Price per unit (per square yard for land, per square foot for rent). Across different types (farm land
        // against a converted plot, a room against a villa) the price is a fact to note, not better or worse.
        var sameKind = string.Equals(subject.Kind, other.Kind, StringComparison.Ordinal);
        if (other.PricePerUnit is not { } theirs)
        {
            points.Add(Point("no_price", PointKind.Con));
        }
        else if (subject.PricePerUnit is { } ours && ours > 0)
        {
            var change = (theirs - ours) / ours;
            if (change <= -PriceThreshold)
            {
                points.Add(Point("cheaper", sameKind ? PointKind.Pro : PointKind.Info, ("percent", Percent(-change))));
            }
            else if (change >= PriceThreshold)
            {
                points.Add(Point("costlier", sameKind ? PointKind.Con : PointKind.Info, ("percent", Percent(change))));
            }
            else
            {
                points.Add(Point("similar_price", PointKind.Info));
            }
        }

        if (subject.Area > 0 && other.Area > 0)
        {
            var change = (other.Area - subject.Area) / subject.Area;
            if (change >= AreaThreshold)
            {
                points.Add(Point("bigger", PointKind.Info, ("percent", Percent(change))));
            }
            else if (change <= -AreaThreshold)
            {
                points.Add(Point("smaller", PointKind.Info, ("percent", Percent(-change))));
            }
        }

        if (!string.Equals(subject.Kind, other.Kind, StringComparison.Ordinal))
        {
            points.Add(Point("different_type", PointKind.Info, ("kind", other.Kind)));
        }

        // Road in front.
        if (other.RoadWidthM is { } road)
        {
            if (subject.RoadWidthM is { } ourRoad && road - ourRoad >= RoadThresholdM)
            {
                points.Add(Point("road_wider", PointKind.Pro, ("theirs", Feet(road)), ("ours", Feet(ourRoad))));
            }
            else if (subject.RoadWidthM is { } ourRoad2 && ourRoad2 - road >= RoadThresholdM)
            {
                points.Add(Point("road_narrower", PointKind.Con, ("theirs", Feet(road)), ("ours", Feet(ourRoad2))));
            }
        }
        else if (subject.RoadWidthM is not null)
        {
            points.Add(Point("road_unknown", PointKind.Con));
        }

        if (other.DistanceToMainRoadM is { } far && subject.DistanceToMainRoadM is { } ourFar)
        {
            if (ourFar - far >= MainRoadThresholdM)
            {
                points.Add(Point("main_road_closer", PointKind.Pro, ("theirs", far.ToString(CultureInfo.InvariantCulture)), ("ours", ourFar.ToString(CultureInfo.InvariantCulture))));
            }
            else if (far - ourFar >= MainRoadThresholdM)
            {
                points.Add(Point("main_road_farther", PointKind.Con, ("theirs", far.ToString(CultureInfo.InvariantCulture)), ("ours", ourFar.ToString(CultureInfo.InvariantCulture))));
            }
        }

        // Homes to rent.
        if (other.Bedrooms is { } beds && subject.Bedrooms is { } ourBeds && beds != ourBeds)
        {
            points.Add(Point(beds > ourBeds ? "more_bedrooms" : "fewer_bedrooms", PointKind.Info,
                ("theirs", beds.ToString(CultureInfo.InvariantCulture)), ("ours", ourBeds.ToString(CultureInfo.InvariantCulture))));
        }

        if (other.DepositMonths is { } deposit && subject.DepositMonths is { } ourDeposit && Math.Abs(deposit - ourDeposit) >= 1)
        {
            points.Add(Point(deposit < ourDeposit ? "lower_deposit" : "higher_deposit", deposit < ourDeposit ? PointKind.Pro : PointKind.Con,
                ("theirs", Months(deposit)), ("ours", Months(ourDeposit))));
        }

        if (other.Furnishing is { } furnishing && !string.Equals(furnishing, subject.Furnishing, StringComparison.Ordinal))
        {
            points.Add(Point("furnishing", PointKind.Info, ("furnishing", furnishing)));
        }

        // Checks: what Bhoomisetu recorded, never a judgement of the title.
        if (subject.ReviewedDocuments > 0 || other.ReviewedDocuments > 0 || subject.OwnerRecorded || other.OwnerRecorded)
        {
            points.Add(other.OwnerRecorded ? Point("owner_recorded", PointKind.Pro) : Point("owner_not_recorded", PointKind.Con));
            points.Add(other.ReviewedDocuments > 0
                ? Point("documents_reviewed", PointKind.Pro, ("count", other.ReviewedDocuments.ToString(CultureInfo.InvariantCulture)))
                : Point("no_documents", PointKind.Con));
        }

        return points;
    }

    private static ComparisonPoint Point(string code, PointKind kind, params (string Key, string Value)[] values) =>
        new(code, kind, values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal));

    private static string Percent(decimal fraction) => Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Road widths are spoken of in feet in Telangana and AP (a "30 ft road").</summary>
    private static string Feet(decimal metres) => Math.Round(metres * 3.28084m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Months(decimal months) => Math.Round(months, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
