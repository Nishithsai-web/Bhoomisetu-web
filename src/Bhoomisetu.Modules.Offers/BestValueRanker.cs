using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Modules.Offers;

/// <summary>
/// Compares every lister's offer for one parcel. All offers are for the same land, so totals are compared
/// directly; the API adds price per gajam or acre for display.
///
/// Rules (published on the comparison page):
/// - Effective price = asking price + buyer-paid brokerage. Undisclosed agent brokerage is assumed at
///   <see cref="RankingOptions.AssumedUndisclosedBrokeragePercent"/> so hiding fees never looks cheaper.
/// - An offer below <see cref="RankingOptions.LowPriceOutlierRatio"/> of the median of the other offers
///   (needs at least two others) is a low-price outlier: flagged "verify before visiting" and ranked after
///   every normal offer in best-value order, because very low prices are a common bait.
/// - Best value = weighted score of price, owner authority, checks passed, response time and freshness.
/// - An "owner" claim counts only when the lister is the owner of record. Until then the offer is priced,
///   scored and badged as an agent's, and flagged <see cref="RankedOffer.OwnerClaimUnverified"/>.
/// - Nothing paid influences the order; there is no input for it.
/// </summary>
public sealed class BestValueRanker
{
    private readonly RankingOptions _options;
    private readonly IClock _clock;

    public BestValueRanker(RankingOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        _options = options;
        _clock = clock;
    }

    public OfferComparisonResult Compare(IReadOnlyCollection<OfferSnapshot> offers, ComparisonSort sort)
    {
        ArgumentNullException.ThrowIfNull(offers);
        if (offers.Count == 0)
        {
            return new OfferComparisonResult(sort, [], null, _options);
        }

        if (offers.Select(o => o.OfferId).Distinct().Count() != offers.Count)
        {
            throw new ArgumentException("Offers must have distinct ids.", nameof(offers));
        }

        var now = _clock.UtcNow;
        var priced = offers.Select(o => (Offer: o, Effective: EffectivePrice(o, out var assumed), Assumed: assumed)).ToList();

        var outliers = new HashSet<Guid>();
        foreach (var item in priced)
        {
            var others = priced.Where(p => p.Offer.OfferId != item.Offer.OfferId).Select(p => (decimal)p.Effective).ToList();
            if (others.Count >= 2 && item.Effective < _options.LowPriceOutlierRatio * Median(others))
            {
                outliers.Add(item.Offer.OfferId);
            }
        }

        var normal = priced.Where(p => !outliers.Contains(p.Offer.OfferId)).ToList();
        var referencePrice = (normal.Count > 0 ? normal : priced).Min(p => p.Effective);

        var scored = priced.Select(p =>
        {
            var breakdown = new ScoreBreakdown(
                Price: Math.Min(1m, (decimal)referencePrice / p.Effective),
                Authority: AuthorityScore(p.Offer.Authority),
                Checks: ChecksScore(p.Offer),
                Response: ResponseScore(p.Offer.MedianResponseMinutes),
                Freshness: FreshnessScore(now - p.Offer.AvailabilityConfirmedAt));
            var score = (breakdown.Price * _options.PriceWeight)
                + (breakdown.Authority * _options.AuthorityWeight)
                + (breakdown.Checks * _options.ChecksWeight)
                + (breakdown.Response * _options.ResponseWeight)
                + (breakdown.Freshness * _options.FreshnessWeight);
            return new Scored(p.Offer, p.Effective, p.Assumed, Math.Round(score, 4, MidpointRounding.AwayFromZero),
                Round(breakdown), outliers.Contains(p.Offer.OfferId), IsStale(now - p.Offer.AvailabilityConfirmedAt),
                IsVerifiedOwner(p.Offer));
        }).ToList();

        var bestValueOrder = OrderBestValue(scored).ToList();
        var badges = AssignBadges(scored, bestValueOrder);

        var ordered = sort switch
        {
            ComparisonSort.BestValue => bestValueOrder,
            ComparisonSort.LowestPrice => scored
                .OrderBy(s => s.Effective).ThenByDescending(s => s.Score).ThenBy(s => s.Offer.CreatedAt).ThenBy(s => s.Offer.OfferId).ToList(),
            ComparisonSort.BestVerified => scored
                .OrderBy(s => s.Offer.Authority == Authority.NotConfirmed ? 1 : 0)
                .ThenByDescending(s => s.Breakdown.Checks)
                .ThenBy(s => s.Effective).ThenBy(s => s.Offer.CreatedAt).ThenBy(s => s.Offer.OfferId).ToList(),
            ComparisonSort.FastestResponse => scored
                .OrderBy(s => s.Offer.MedianResponseMinutes is null ? 1 : 0)
                .ThenBy(s => s.Offer.MedianResponseMinutes ?? 0)
                .ThenBy(s => s.Effective).ThenBy(s => s.Offer.CreatedAt).ThenBy(s => s.Offer.OfferId).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown sort."),
        };

        var ranked = ordered
            .Select((s, index) => new RankedOffer(s.Offer, index + 1, s.Effective, s.Assumed, s.Score, s.Breakdown, s.IsOutlier, badges[s.Offer.OfferId],
                OwnerClaimUnverified: s.Offer.ListerKind == ListerKind.Owner && !s.IsVerifiedOwner))
            .ToList();

        return new OfferComparisonResult(sort, ranked, (long)Math.Round(Median(priced.Select(p => (decimal)p.Effective).ToList()), MidpointRounding.AwayFromZero), _options);
    }

    private long EffectivePrice(OfferSnapshot offer, out bool assumed)
    {
        if (offer.AskingPricePaise <= 0)
        {
            throw new ArgumentException($"Offer {offer.OfferId} has a non-positive price.", nameof(offer));
        }

        assumed = false;
        if (IsVerifiedOwner(offer))
        {
            return offer.AskingPricePaise;
        }

        var percent = offer.BuyerBrokeragePercent;
        if (percent is null)
        {
            assumed = true;
            percent = _options.AssumedUndisclosedBrokeragePercent;
        }

        var brokerage = Math.Round(offer.AskingPricePaise * percent.Value / 100m, MidpointRounding.AwayFromZero);
        return offer.AskingPricePaise + (long)brokerage;
    }

    private static decimal AuthorityScore(Authority authority) => authority switch
    {
        Authority.OwnerOfRecord => 1m,
        Authority.OwnerConfirmedAgent => 1m,
        Authority.NotConfirmed => 0.3m,
        _ => throw new ArgumentOutOfRangeException(nameof(authority), authority, "Unknown authority."),
    };

    /// <summary>Only a lister recorded as owner of record is treated as the owner; a bare claim is not enough.</summary>
    private static bool IsVerifiedOwner(OfferSnapshot offer) =>
        offer.ListerKind == ListerKind.Owner && offer.Authority == Authority.OwnerOfRecord;

    private static decimal ChecksScore(OfferSnapshot offer)
    {
        // Owners are not estate agents, so RERA agent registration does not apply to them.
        var applicable = IsVerifiedOwner(offer)
            ? new[] { ListerChecks.PhoneOtp, ListerChecks.Identity }
            : new[] { ListerChecks.PhoneOtp, ListerChecks.Identity, ListerChecks.ReraAgent };
        var passed = applicable.Count(check => offer.Checks.HasFlag(check));
        return (decimal)passed / applicable.Length;
    }

    private decimal ResponseScore(int? medianMinutes)
    {
        if (medianMinutes is null)
        {
            return _options.UnknownResponseScore;
        }

        return Linear(medianMinutes.Value, _options.FullResponseScoreWithinMinutes, _options.ZeroResponseScoreAfterMinutes);
    }

    private decimal FreshnessScore(TimeSpan age) =>
        Linear((decimal)Math.Max(0, age.TotalDays), _options.FullFreshnessWithinDays, _options.StaleAfterDays);

    private bool IsStale(TimeSpan age) => age.TotalDays > _options.StaleAfterDays;

    /// <summary>1 at or below <paramref name="full"/>, 0 at or above <paramref name="zero"/>, straight line between.</summary>
    private static decimal Linear(decimal value, decimal full, decimal zero)
    {
        if (value <= full)
        {
            return 1m;
        }

        if (value >= zero)
        {
            return 0m;
        }

        return 1m - ((value - full) / (zero - full));
    }

    private static IEnumerable<Scored> OrderBestValue(IEnumerable<Scored> scored) => scored
        .OrderBy(s => s.IsOutlier ? 1 : 0)
        .ThenByDescending(s => s.Score)
        .ThenBy(s => s.Effective)
        .ThenBy(s => s.Offer.CreatedAt)
        .ThenBy(s => s.Offer.OfferId);

    private static Dictionary<Guid, IReadOnlyList<OfferBadge>> AssignBadges(IReadOnlyList<Scored> scored, IReadOnlyList<Scored> bestValueOrder)
    {
        var comparable = scored.Count >= 2;
        var bestValueId = comparable
            ? bestValueOrder.FirstOrDefault(s => !s.IsOutlier && !s.IsStale)?.Offer.OfferId
            : null;
        // The bait-price warning and a "lowest price" badge would contradict each other, so outliers never get it.
        var normal = scored.Where(s => !s.IsOutlier).ToList();
        long? lowest = normal.Count > 0 ? normal.Min(s => s.Effective) : null;

        var result = new Dictionary<Guid, IReadOnlyList<OfferBadge>>();
        foreach (var s in scored)
        {
            var list = new List<OfferBadge>();
            if (s.Offer.OfferId == bestValueId)
            {
                list.Add(OfferBadge.BestValue);
            }

            if (comparable && !s.IsOutlier && s.Effective == lowest)
            {
                list.Add(OfferBadge.LowestPrice);
            }

            if (s.IsOutlier)
            {
                list.Add(OfferBadge.VerifyBeforeVisiting);
            }

            if (s.IsVerifiedOwner || s.Offer.BuyerBrokeragePercent == 0m)
            {
                list.Add(OfferBadge.NoBrokerage);
            }

            if (s.IsStale)
            {
                list.Add(OfferBadge.AvailabilityStale);
            }

            if (s.Offer.Authority == Authority.NotConfirmed)
            {
                list.Add(OfferBadge.AuthorityNotConfirmed);
            }

            result[s.Offer.OfferId] = list;
        }

        return result;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    private static ScoreBreakdown Round(ScoreBreakdown b) => new(
        Math.Round(b.Price, 4, MidpointRounding.AwayFromZero),
        Math.Round(b.Authority, 4, MidpointRounding.AwayFromZero),
        Math.Round(b.Checks, 4, MidpointRounding.AwayFromZero),
        Math.Round(b.Response, 4, MidpointRounding.AwayFromZero),
        Math.Round(b.Freshness, 4, MidpointRounding.AwayFromZero));

    private sealed record Scored(
        OfferSnapshot Offer,
        long Effective,
        bool Assumed,
        decimal Score,
        ScoreBreakdown Breakdown,
        bool IsOutlier,
        bool IsStale,
        bool IsVerifiedOwner);
}
