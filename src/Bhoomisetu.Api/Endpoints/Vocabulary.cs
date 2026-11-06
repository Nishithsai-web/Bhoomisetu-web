using Bhoomisetu.Modules.Offers;

namespace Bhoomisetu.Api.Endpoints;

/// <summary>Allowed codes for constrained fields. Must match the CHECK constraints in db/migrations/0001_core.sql.</summary>
public static class Vocabulary
{
    public static readonly IReadOnlySet<string> LandClasses = Set("agricultural", "non_agricultural", "converted", "unknown");
    public static readonly IReadOnlySet<string> AreaSources = Set("record", "deed", "survey", "lister");
    public static readonly IReadOnlySet<string> Facings = Set("north", "east", "south", "west", "north_east", "north_west", "south_east", "south_west");
    public static readonly IReadOnlySet<string> RoadWidthSources = Set("approved_layout", "measured_on_map", "lister");
    public static readonly IReadOnlySet<string> RoadTypes = Set("national_highway", "state_highway", "main_road", "layout_road", "village_road", "cart_track");
    public static readonly IReadOnlySet<string> AccessRights = Set("direct_public_road", "through_private_land_with_right", "through_private_land_no_record", "unknown");
    public static readonly IReadOnlySet<string> ListerKinds = Set("owner", "agent");

    public const string OwnerOfRecord = "owner_of_record";
    public const string AuthorisedAgent = "authorised_agent";

    public static string Code(ListerKind kind) => kind switch
    {
        ListerKind.Owner => "owner",
        ListerKind.Agent => "agent",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ListerKind ParseListerKind(string code) => code == "owner" ? ListerKind.Owner : ListerKind.Agent;

    public static string Code(Authority authority) => authority switch
    {
        Authority.OwnerOfRecord => "owner_of_record",
        Authority.OwnerConfirmedAgent => "owner_confirmed_agent",
        Authority.NotConfirmed => "not_confirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(authority), authority, null),
    };

    public static string Code(OfferBadge badge) => badge switch
    {
        OfferBadge.BestValue => "best_value",
        OfferBadge.LowestPrice => "lowest_price",
        OfferBadge.VerifyBeforeVisiting => "verify_before_visiting",
        OfferBadge.NoBrokerage => "no_brokerage",
        OfferBadge.AvailabilityStale => "availability_stale",
        OfferBadge.AuthorityNotConfirmed => "authority_not_confirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(badge), badge, null),
    };

    public static bool TryParseSort(string? code, out ComparisonSort sort)
    {
        switch (code)
        {
            case null or "" or "best_value":
                sort = ComparisonSort.BestValue;
                return true;
            case "lowest_price":
                sort = ComparisonSort.LowestPrice;
                return true;
            case "best_verified":
                sort = ComparisonSort.BestVerified;
                return true;
            case "fastest_response":
                sort = ComparisonSort.FastestResponse;
                return true;
            default:
                sort = default;
                return false;
        }
    }

    public static string Code(ComparisonSort sort) => sort switch
    {
        ComparisonSort.BestValue => "best_value",
        ComparisonSort.LowestPrice => "lowest_price",
        ComparisonSort.BestVerified => "best_verified",
        ComparisonSort.FastestResponse => "fastest_response",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };

    public static ListerChecks ParseChecks(IEnumerable<string> checkTypes)
    {
        var result = ListerChecks.None;
        foreach (var type in checkTypes)
        {
            result |= type switch
            {
                "phone_otp" => ListerChecks.PhoneOtp,
                "identity" => ListerChecks.Identity,
                "rera_agent" => ListerChecks.ReraAgent,
                _ => ListerChecks.None,
            };
        }

        return result;
    }

    public static IReadOnlyList<string> CheckCodes(ListerChecks checks)
    {
        var list = new List<string>();
        if (checks.HasFlag(ListerChecks.PhoneOtp))
        {
            list.Add("phone_otp");
        }

        if (checks.HasFlag(ListerChecks.Identity))
        {
            list.Add("identity");
        }

        if (checks.HasFlag(ListerChecks.ReraAgent))
        {
            list.Add("rera_agent");
        }

        return list;
    }

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
