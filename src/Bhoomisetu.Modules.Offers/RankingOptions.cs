namespace Bhoomisetu.Modules.Offers;

/// <summary>
/// Weights and thresholds for the best-value score. These are published to users on the comparison page,
/// so any change is a product decision and must be recorded in the decision register.
/// </summary>
public sealed record RankingOptions
{
    public decimal PriceWeight { get; init; } = 0.40m;
    public decimal AuthorityWeight { get; init; } = 0.25m;
    public decimal ChecksWeight { get; init; } = 0.15m;
    public decimal ResponseWeight { get; init; } = 0.10m;
    public decimal FreshnessWeight { get; init; } = 0.10m;

    /// <summary>An offer below this share of the median of the other offers is flagged "verify before visiting".</summary>
    public decimal LowPriceOutlierRatio { get; init; } = 0.70m;

    /// <summary>Brokerage assumed for an agent who has not disclosed terms, so undisclosed fees do not look cheaper.</summary>
    public decimal AssumedUndisclosedBrokeragePercent { get; init; } = 2.0m;

    public int FullResponseScoreWithinMinutes { get; init; } = 120;
    public int ZeroResponseScoreAfterMinutes { get; init; } = 48 * 60;
    public decimal UnknownResponseScore { get; init; } = 0.5m;

    public int FullFreshnessWithinDays { get; init; } = 7;
    public int StaleAfterDays { get; init; } = 30;

    public static RankingOptions Default { get; } = new();

    public void Validate()
    {
        var sum = PriceWeight + AuthorityWeight + ChecksWeight + ResponseWeight + FreshnessWeight;
        if (sum != 1.0m)
        {
            throw new InvalidOperationException($"Ranking weights must add up to 1.0; they add up to {sum}.");
        }

        if (new[] { PriceWeight, AuthorityWeight, ChecksWeight, ResponseWeight, FreshnessWeight }.Any(w => w < 0))
        {
            throw new InvalidOperationException("Ranking weights cannot be negative.");
        }

        if (LowPriceOutlierRatio is <= 0 or >= 1)
        {
            throw new InvalidOperationException("LowPriceOutlierRatio must be between 0 and 1.");
        }

        if (FullResponseScoreWithinMinutes >= ZeroResponseScoreAfterMinutes || FullFreshnessWithinDays >= StaleAfterDays)
        {
            throw new InvalidOperationException("Full-score thresholds must be below zero-score thresholds.");
        }
    }
}
