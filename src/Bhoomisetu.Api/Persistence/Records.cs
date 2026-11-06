namespace Bhoomisetu.Api.Persistence;

// Row types mapped one-to-one onto the tables in db/migrations. Domain rules live in the module projects;
// these classes only carry data.

public sealed class UserRecord
{
    public Guid Id { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public string? DisplayName { get; set; }
    public string PreferredLocale { get; set; } = "en";
    public bool IsTrustReviewer { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserCheckRecord
{
    public Guid UserId { get; set; }
    public string CheckType { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; set; }
    public string? Reference { get; set; }
}

public sealed class OtpChallengeRecord
{
    public Guid Id { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public byte[] CodeHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class ParcelRecord
{
    public Guid Id { get; set; }
    public string ParcelKey { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Mandal { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public string? LgdVillageCode { get; set; }
    public string SurveyNumberRaw { get; set; } = string.Empty;
    public string SurveyNumber { get; set; } = string.Empty;
    public string LandClass { get; set; } = string.Empty;
    public decimal RecordedAreaSqm { get; set; }
    public decimal EnteredAreaValue { get; set; }
    public string EnteredAreaUnit { get; set; } = string.Empty;
    public string AreaSource { get; set; } = string.Empty;
    public decimal? FrontageM { get; set; }
    public decimal? DepthM { get; set; }
    public string? Facing { get; set; }
    public decimal? RoadWidthM { get; set; }
    public string? RoadWidthSource { get; set; }
    public string? RoadType { get; set; }
    public string? AccessRight { get; set; }
    public int? DistanceToMainRoadM { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ParcelPartyRecord
{
    public Guid ParcelId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; set; }
    public Guid ConfirmedBy { get; set; }
}

public sealed class OfferRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid ListerUserId { get; set; }
    public string ListerKind { get; set; } = string.Empty;
    public long AskingPricePaise { get; set; }
    public decimal? BuyerBrokeragePercent { get; set; }
    public string? BrokerageTerms { get; set; }
    public DateTimeOffset AvailabilityConfirmedAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ListerStatsRecord
{
    public Guid UserId { get; set; }
    public int? MedianResponseMinutes { get; set; }
    public int SampleSize { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditRecord
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = "{}";
}

public sealed class ParcelChangeRequestRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid RequestedBy { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? CurrentValue { get; set; }
    public string ProposedValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
