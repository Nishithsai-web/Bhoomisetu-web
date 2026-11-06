namespace Bhoomisetu.Modules.Offers;

public enum ComparisonSort
{
    BestValue,
    LowestPrice,
    BestVerified,
    FastestResponse,
}

public enum OfferBadge
{
    BestValue,
    LowestPrice,
    VerifyBeforeVisiting,
    NoBrokerage,
    AvailabilityStale,
    AuthorityNotConfirmed,
}

/// <summary>The parts of the best-value score, each between 0 and 1, shown to the buyer as the explanation.</summary>
public sealed record ScoreBreakdown(decimal Price, decimal Authority, decimal Checks, decimal Response, decimal Freshness);

public sealed record RankedOffer(
    OfferSnapshot Offer,
    int Rank,
    long EffectivePricePaise,
    bool BrokerageAssumed,
    decimal Score,
    ScoreBreakdown Breakdown,
    bool IsLowPriceOutlier,
    IReadOnlyList<OfferBadge> Badges);

public sealed record OfferComparisonResult(
    ComparisonSort Sort,
    IReadOnlyList<RankedOffer> Offers,
    long? MedianEffectivePricePaise,
    RankingOptions Options);
