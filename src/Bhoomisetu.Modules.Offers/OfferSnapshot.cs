namespace Bhoomisetu.Modules.Offers;

public enum ListerKind
{
    Owner,
    Agent,
}

/// <summary>What we know about the lister's right to sell this land.</summary>
public enum Authority
{
    /// <summary>The lister is the owner of record, set by a trust reviewer after document review.</summary>
    OwnerOfRecord,
    /// <summary>An agent the owner of record has confirmed for this parcel.</summary>
    OwnerConfirmedAgent,
    /// <summary>No confirmation yet. Shown to buyers as "authority not confirmed".</summary>
    NotConfirmed,
}

[Flags]
public enum ListerChecks
{
    None = 0,
    PhoneOtp = 1,
    Identity = 2,
    ReraAgent = 4,
}

/// <summary>One lister's offer for a parcel, with the facts the comparison needs. Built by the API from the database.</summary>
public sealed record OfferSnapshot(
    Guid OfferId,
    Guid ListerUserId,
    ListerKind ListerKind,
    Authority Authority,
    ListerChecks Checks,
    long AskingPricePaise,
    decimal? BuyerBrokeragePercent,
    int? MedianResponseMinutes,
    DateTimeOffset AvailabilityConfirmedAt,
    DateTimeOffset CreatedAt);
