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
    public int TokenVersion { get; set; }
    public string? WhatsappE164 { get; set; }
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
    public string? RequesterKey { get; set; }
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

    /// <summary>Sound-alike search keys (PlaceKey), filled by the DbContext on every save.</summary>
    public string? VillageKey { get; set; }

    public string? MandalKey { get; set; }

    public string? DistrictKey { get; set; }

    /// <summary>Computed by the database from NameKey: true for a record set aside as a duplicate.</summary>
    public bool IsHiddenDuplicate { get; set; }
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

public sealed class SubscriptionRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Audience { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public long AmountPaise { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public Guid ActivatedBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ParcelDocumentRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid UploadedBy { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ReviewNote { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DevelopmentProjectRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public DateOnly SourceDate { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ParcelDevelopmentLinkRecord
{
    public Guid ParcelId { get; set; }
    public Guid ProjectId { get; set; }
    public decimal? DistanceKm { get; set; }
    public bool MayAffectParcel { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ListingReportRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid ReporterId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Status { get; set; } = "open";
    public string? ResolutionNote { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A home offered for rent. Its map point (centroid) is read and written with SQL, like parcels.</summary>
public sealed class RentalRecord
{
    public Guid Id { get; set; }
    public Guid ListerUserId { get; set; }
    public string ListerKind { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public short Bedrooms { get; set; }
    public int AreaSqft { get; set; }
    public int RentRupees { get; set; }
    public int DepositRupees { get; set; }
    public string Furnishing { get; set; } = string.Empty;
    public string PreferredTenants { get; set; } = string.Empty;
    public bool Parking { get; set; }
    public DateOnly AvailableFrom { get; set; }
    public string Locality { get; set; } = string.Empty;
    public string Mandal { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset AvailabilityConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
