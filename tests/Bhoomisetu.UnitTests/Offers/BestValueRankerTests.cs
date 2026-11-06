using Bhoomisetu.Modules.Offers;
using Xunit;

namespace Bhoomisetu.UnitTests.Offers;

public sealed class BestValueRankerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private readonly BestValueRanker _ranker = new(RankingOptions.Default, new FakeClock(Now));

    /// <summary>
    /// A: confirmed agent, fully checked, ₹50,00,000 + 1% brokerage, fast, fresh.
    /// B: unconfirmed agent, phone only, ₹48,00,000 with undisclosed brokerage (2% assumed).
    /// C: unconfirmed agent, ₹30,00,000 + 1%: far below the others, and availability 40 days old.
    /// D: owner of record, ₹51,00,000, no brokerage, response time unknown.
    /// </summary>
    private static List<OfferSnapshot> Market() =>
    [
        Offer(A, ListerKind.Agent, Authority.OwnerConfirmedAgent, ListerChecks.PhoneOtp | ListerChecks.Identity | ListerChecks.ReraAgent, 5_000_000, 1m, 60, 1),
        Offer(B, ListerKind.Agent, Authority.NotConfirmed, ListerChecks.PhoneOtp, 4_800_000, null, 600, 2),
        Offer(C, ListerKind.Agent, Authority.NotConfirmed, ListerChecks.PhoneOtp, 3_000_000, 1m, 30, 40),
        Offer(D, ListerKind.Owner, Authority.OwnerOfRecord, ListerChecks.PhoneOtp | ListerChecks.Identity, 5_100_000, null, null, 0),
    ];

    [Fact]
    public void Best_value_prefers_the_checked_confirmed_offer_over_the_cheapest()
    {
        var result = _ranker.Compare(Market(), ComparisonSort.BestValue);

        Assert.Equal(new[] { A, D, B, C }, result.Offers.Select(o => o.Offer.OfferId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Offers.Select(o => o.Rank));
        Assert.Equal(0.9878m, result.Offers[0].Score);
        Assert.Equal(0.934m, result.Offers[1].Score);
        Assert.Equal(0.7076m, result.Offers[2].Score);
        Assert.Equal(0.625m, result.Offers[3].Score);
    }

    [Fact]
    public void Effective_price_adds_disclosed_or_assumed_brokerage()
    {
        var result = _ranker.Compare(Market(), ComparisonSort.BestValue).Offers.ToDictionary(o => o.Offer.OfferId);

        Assert.Equal(505_000_000L, result[A].EffectivePricePaise);
        Assert.False(result[A].BrokerageAssumed);
        Assert.Equal(489_600_000L, result[B].EffectivePricePaise);
        Assert.True(result[B].BrokerageAssumed);
        Assert.Equal(510_000_000L, result[D].EffectivePricePaise);
        Assert.False(result[D].BrokerageAssumed);
    }

    [Fact]
    public void Very_low_price_is_flagged_and_ranked_last_for_best_value()
    {
        var result = _ranker.Compare(Market(), ComparisonSort.BestValue).Offers.ToDictionary(o => o.Offer.OfferId);

        Assert.True(result[C].IsLowPriceOutlier);
        Assert.Contains(OfferBadge.VerifyBeforeVisiting, result[C].Badges);
        Assert.Contains(OfferBadge.LowestPrice, result[C].Badges);
        Assert.Contains(OfferBadge.AvailabilityStale, result[C].Badges);
        Assert.DoesNotContain(OfferBadge.BestValue, result[C].Badges);
        Assert.False(result[A].IsLowPriceOutlier);
    }

    [Fact]
    public void Badges_describe_each_offer()
    {
        var result = _ranker.Compare(Market(), ComparisonSort.BestValue).Offers.ToDictionary(o => o.Offer.OfferId);

        Assert.Equal(new[] { OfferBadge.BestValue }, result[A].Badges);
        Assert.Equal(new[] { OfferBadge.AuthorityNotConfirmed }, result[B].Badges);
        Assert.Equal(new[] { OfferBadge.NoBrokerage }, result[D].Badges);
    }

    [Fact]
    public void Score_breakdown_is_exposed()
    {
        var b = _ranker.Compare(Market(), ComparisonSort.BestValue).Offers.Single(o => o.Offer.OfferId == B).Breakdown;

        Assert.Equal(1m, b.Price);
        Assert.Equal(0.3m, b.Authority);
        Assert.Equal(0.3333m, b.Checks);
        Assert.Equal(0.8261m, b.Response);
        Assert.Equal(1m, b.Freshness);
    }

    [Theory]
    [InlineData(ComparisonSort.LowestPrice, "c,b,a,d")]
    [InlineData(ComparisonSort.BestVerified, "a,d,c,b")]
    [InlineData(ComparisonSort.FastestResponse, "c,a,b,d")]
    public void Other_sorts_order_as_documented(ComparisonSort sort, string expected)
    {
        var ids = _ranker.Compare(Market(), sort).Offers.Select(o => o.Offer.OfferId.ToString()[^1..]);
        Assert.Equal(expected, string.Join(',', ids));
    }

    [Fact]
    public void Badges_do_not_change_with_sort()
    {
        var bestValue = _ranker.Compare(Market(), ComparisonSort.BestValue).Offers.ToDictionary(o => o.Offer.OfferId, o => o.Badges);
        var lowest = _ranker.Compare(Market(), ComparisonSort.LowestPrice).Offers.ToDictionary(o => o.Offer.OfferId, o => o.Badges);

        foreach (var id in new[] { A, B, C, D })
        {
            Assert.Equal(bestValue[id], lowest[id]);
        }
    }

    [Fact]
    public void Median_effective_price_is_reported()
    {
        Assert.Equal(497_300_000L, _ranker.Compare(Market(), ComparisonSort.BestValue).MedianEffectivePricePaise);
    }

    [Fact]
    public void Single_offer_gets_no_comparative_badges()
    {
        var single = _ranker.Compare([Market()[0]], ComparisonSort.BestValue).Offers.Single();
        Assert.DoesNotContain(OfferBadge.BestValue, single.Badges);
        Assert.DoesNotContain(OfferBadge.LowestPrice, single.Badges);
        Assert.Equal(1, single.Rank);
    }

    [Fact]
    public void Outlier_needs_at_least_two_other_offers()
    {
        var two = _ranker.Compare([Market()[0], Market()[2]], ComparisonSort.BestValue).Offers;
        Assert.All(two, o => Assert.False(o.IsLowPriceOutlier));
    }

    [Fact]
    public void No_offers_gives_an_empty_result()
    {
        var result = _ranker.Compare([], ComparisonSort.BestValue);
        Assert.Empty(result.Offers);
        Assert.Null(result.MedianEffectivePricePaise);
    }

    [Fact]
    public void Stale_offer_cannot_hold_the_best_value_badge()
    {
        var offers = new List<OfferSnapshot>
        {
            Offer(A, ListerKind.Owner, Authority.OwnerOfRecord, ListerChecks.PhoneOtp | ListerChecks.Identity, 1_000_000, null, 30, 31),
            Offer(B, ListerKind.Owner, Authority.NotConfirmed, ListerChecks.PhoneOtp, 1_000_000, null, 30, 1),
        };
        var result = _ranker.Compare(offers, ComparisonSort.BestValue).Offers.ToDictionary(o => o.Offer.OfferId);
        Assert.DoesNotContain(OfferBadge.BestValue, result[A].Badges);
        Assert.Contains(OfferBadge.AvailabilityStale, result[A].Badges);
    }

    [Fact]
    public void Duplicate_offer_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => _ranker.Compare([Market()[0], Market()[0]], ComparisonSort.BestValue));
    }

    [Fact]
    public void Weights_must_add_up_to_one()
    {
        var bad = RankingOptions.Default with { PriceWeight = 0.5m };
        Assert.Throws<InvalidOperationException>(() => new BestValueRanker(bad, new FakeClock(Now)));
    }

    [Fact]
    public void Ranking_is_deterministic_for_identical_offers()
    {
        var offers = new List<OfferSnapshot>
        {
            Offer(B, ListerKind.Agent, Authority.NotConfirmed, ListerChecks.PhoneOtp, 1_000_000, 1m, 30, 1),
            Offer(A, ListerKind.Agent, Authority.NotConfirmed, ListerChecks.PhoneOtp, 1_000_000, 1m, 30, 1),
        };
        var first = _ranker.Compare(offers, ComparisonSort.BestValue).Offers.Select(o => o.Offer.OfferId).ToList();
        offers.Reverse();
        var second = _ranker.Compare(offers, ComparisonSort.BestValue).Offers.Select(o => o.Offer.OfferId).ToList();
        Assert.Equal(first, second);
    }

    private static OfferSnapshot Offer(Guid id, ListerKind kind, Authority authority, ListerChecks checks, long rupees, decimal? brokerage, int? responseMinutes, int daysSinceConfirmed) =>
        new(id, id, kind, authority, checks, rupees * 100, brokerage, responseMinutes, Now.AddDays(-daysSinceConfirmed), Now.AddDays(-60));
}
