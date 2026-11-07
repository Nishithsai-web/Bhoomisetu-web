using Bhoomisetu.Api.Auth;
using Bhoomisetu.Api.Persistence;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.Modules.Offers;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bhoomisetu.Api.Endpoints;

public sealed record CreateOfferRequest(string? ListerKind, decimal AskingPriceRupees, decimal? BuyerBrokeragePercent, string? BrokerageTerms);

public sealed record OfferResponse(Guid Id, Guid ParcelId, string ListerKind, decimal AskingPriceRupees, decimal? BuyerBrokeragePercent, string? BrokerageTerms, DateTimeOffset AvailabilityConfirmedAt);

public sealed record ScoreBreakdownResponse(decimal Price, decimal Authority, decimal Checks, decimal Response, decimal Freshness);

public sealed record ComparedOfferResponse(
    int Rank,
    Guid OfferId,
    string? ListerName,
    int ListerNumber,
    string ListerKind,
    bool OwnerClaimUnverified,
    string Authority,
    IReadOnlyList<string> Checks,
    decimal AskingPriceRupees,
    decimal? BuyerBrokeragePercent,
    bool BrokerageAssumed,
    decimal EffectivePriceRupees,
    decimal EffectivePricePerSquareYardRupees,
    decimal EffectivePricePerAcreRupees,
    int? MedianResponseMinutes,
    DateTimeOffset AvailabilityConfirmedAt,
    decimal Score,
    ScoreBreakdownResponse Breakdown,
    IReadOnlyList<string> Badges);

public sealed record RankingRulesResponse(decimal PriceWeight, decimal AuthorityWeight, decimal ChecksWeight, decimal ResponseWeight, decimal FreshnessWeight,
    decimal LowPriceOutlierRatio, decimal AssumedUndisclosedBrokeragePercent, int StaleAfterDays);

public sealed record ComparisonResponse(Guid ParcelId, string LandClass, decimal AreaSquareYards, decimal AreaAcres, string Sort, decimal? MedianEffectivePriceRupees,
    RankingRulesResponse Rules, IReadOnlyList<ComparedOfferResponse> Offers);

public static class OfferEndpoints
{
    /// <summary>₹10,000 crore; anything above is a typing error.</summary>
    private const decimal MaxPriceRupees = 100_000_000_000m;

    public static void MapOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parcels/{parcelId:guid}").WithTags("Offers");
        group.MapPost("/offers", CreateAsync).RequireAuthorization();
        group.MapPost("/offers/mine/withdraw", WithdrawAsync).RequireAuthorization();
        group.MapGet("/comparison", CompareAsync);
        group.MapPost("/agents/{agentUserId:guid}/confirm", ConfirmAgentAsync).RequireAuthorization();
        group.MapPost("/offers/{offerId:guid}/confirm-lister", ConfirmListerOfOfferAsync).RequireAuthorization();
        group.MapDelete("/agents/{agentUserId:guid}/confirm", RevokeAgentAsync).RequireAuthorization();
    }

    /// <summary>Creates the caller's offer, replacing their previous active offer for this parcel.</summary>
    private static async Task<IResult> CreateAsync(Guid parcelId, CreateOfferRequest request, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        if (request.ListerKind is null || !Vocabulary.ListerKinds.Contains(request.ListerKind))
        {
            return ApiResults.Invalid("offer.lister_kind_invalid", "Lister kind must be owner or agent.");
        }

        if (request.AskingPriceRupees <= 0 || request.AskingPriceRupees > MaxPriceRupees || decimal.Round(request.AskingPriceRupees, 2) != request.AskingPriceRupees)
        {
            return ApiResults.Invalid("offer.price_invalid", "Asking price must be a positive amount in rupees with at most two decimals.");
        }

        if (request.BuyerBrokeragePercent is { } pct && (pct < 0 || pct > 10 || decimal.Round(pct, 2) != pct))
        {
            return ApiResults.Invalid("offer.brokerage_invalid", "Brokerage must be between 0 and 10 percent.");
        }

        if (request.ListerKind == "owner" && request.BuyerBrokeragePercent is > 0)
        {
            return ApiResults.Invalid("offer.owner_brokerage", "An owner's own offer cannot charge brokerage.");
        }

        if (request.BrokerageTerms is { Length: > 500 })
        {
            return ApiResults.Invalid("offer.terms_too_long", "Brokerage terms can be at most 500 characters.");
        }

        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        if (ParcelEndpoints.RejectIfDuplicate(parcel) is { } duplicate)
        {
            return duplicate;
        }

        var userId = http.User.UserId();
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var previous = await db.Offers.Where(o => o.ParcelId == parcelId && o.ListerUserId == userId && o.Status == "active").ToListAsync(ct);
        foreach (var old in previous)
        {
            old.Status = "withdrawn";
            old.UpdatedAt = now;
        }

        // Flush the withdrawal first so the partial unique index sees no active offer when the new one is inserted.
        await db.SaveChangesAsync(ct);

        var offer = new OfferRecord
        {
            Id = Guid.CreateVersion7(),
            ParcelId = parcelId,
            ListerUserId = userId,
            ListerKind = request.ListerKind,
            AskingPricePaise = (long)(request.AskingPriceRupees * 100m),
            BuyerBrokeragePercent = request.ListerKind == "owner" ? null : request.BuyerBrokeragePercent,
            BrokerageTerms = string.IsNullOrWhiteSpace(request.BrokerageTerms) ? null : request.BrokerageTerms.Trim(),
            AvailabilityConfirmedAt = now,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Offers.Add(offer);
        audit.Add(userId, previous.Count > 0 ? "offer.replaced" : "offer.created", "offer", offer.Id.ToString(),
            new { parcelId, offer.AskingPricePaise, replaced = previous.Select(p => p.Id).ToArray() });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "offer.concurrent_update", "Your offer was changed at the same moment. Please try again.");
        }

        return Results.Created($"/api/v1/parcels/{parcelId}/comparison", ToResponse(offer));
    }

    private static async Task<IResult> WithdrawAsync(Guid parcelId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var active = await db.Offers.Where(o => o.ParcelId == parcelId && o.ListerUserId == userId && o.Status == "active").ToListAsync(ct);
        if (active.Count == 0)
        {
            return ApiResults.NotFound("Your active offer");
        }

        var now = clock.UtcNow;
        foreach (var offer in active)
        {
            offer.Status = "withdrawn";
            offer.UpdatedAt = now;
            audit.Add(userId, "offer.withdrawn", "offer", offer.Id.ToString(), new { parcelId });
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CompareAsync(Guid parcelId, string? sort, BhoomisetuDbContext db, BestValueRanker ranker, CancellationToken ct)
    {
        if (!Vocabulary.TryParseSort(sort, out var parsedSort))
        {
            return ApiResults.Invalid("comparison.sort_invalid", "Sort must be best_value, lowest_price, best_verified or fastest_response.");
        }

        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null)
        {
            return ApiResults.NotFound("Parcel");
        }

        // Comparing a set-aside duplicate would split the offers for one piece of land; point to the kept record.
        if (ParcelEndpoints.RejectIfDuplicate(parcel) is { } duplicate)
        {
            return duplicate;
        }

        var offers = await db.Offers.AsNoTracking().Where(o => o.ParcelId == parcelId && o.Status == "active").ToListAsync(ct);
        var listerIds = offers.Select(o => o.ListerUserId).Distinct().ToList();
        var parties = await db.ParcelParties.AsNoTracking().Where(p => p.ParcelId == parcelId).ToListAsync(ct);
        var checks = await db.UserChecks.AsNoTracking().Where(c => listerIds.Contains(c.UserId)).ToListAsync(ct);
        var stats = await db.ListerStats.AsNoTracking().Where(s => listerIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        var names = await db.Users.AsNoTracking().Where(u => listerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var owner = parties.FirstOrDefault(p => p.Role == Vocabulary.OwnerOfRecord);
        var snapshots = offers.Select(o => new OfferSnapshot(
            o.Id,
            o.ListerUserId,
            Vocabulary.ParseListerKind(o.ListerKind),
            AuthorityFor(o.ListerUserId, owner, parties),
            Vocabulary.ParseChecks(checks.Where(c => c.UserId == o.ListerUserId).Select(c => c.CheckType)),
            o.AskingPricePaise,
            o.BuyerBrokeragePercent,
            stats.TryGetValue(o.ListerUserId, out var s) ? s.MedianResponseMinutes : null,
            o.AvailabilityConfirmedAt,
            o.CreatedAt)).ToList();

        var result = ranker.Compare(snapshots, parsedSort);

        // Stable numbers by when each lister joined the parcel, so "Lister 2" means the same person in every sort.
        // The web app turns the number into words in the reader's language.
        var listerNumbers = offers.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Select((o, i) => (o.Id, Number: i + 1)).ToDictionary(x => x.Id, x => x.Number);

        var areaSqm = parcel.RecordedAreaSqm;
        var squareYards = areaSqm / Area.FactorToSquareMetres(AreaUnit.SquareYard);
        var acres = areaSqm / Area.FactorToSquareMetres(AreaUnit.Acre);

        var options = result.Options;
        var response = new ComparisonResponse(
            parcelId,
            parcel.LandClass,
            Math.Round(squareYards, 2, MidpointRounding.AwayFromZero),
            Math.Round(acres, 4, MidpointRounding.AwayFromZero),
            Vocabulary.Code(parsedSort),
            result.MedianEffectivePricePaise is { } median ? Rupees(median) : null,
            new RankingRulesResponse(options.PriceWeight, options.AuthorityWeight, options.ChecksWeight, options.ResponseWeight, options.FreshnessWeight,
                options.LowPriceOutlierRatio, options.AssumedUndisclosedBrokeragePercent, options.StaleAfterDays),
            result.Offers.Select(r => new ComparedOfferResponse(
                r.Rank,
                r.Offer.OfferId,
                names.GetValueOrDefault(r.Offer.ListerUserId),
                listerNumbers[r.Offer.OfferId],
                Vocabulary.Code(r.Offer.ListerKind),
                r.OwnerClaimUnverified,
                Vocabulary.Code(r.Offer.Authority),
                Vocabulary.CheckCodes(r.Offer.Checks),
                Rupees(r.Offer.AskingPricePaise),
                r.Offer.BuyerBrokeragePercent,
                r.BrokerageAssumed,
                Rupees(r.EffectivePricePaise),
                Math.Round(Rupees(r.EffectivePricePaise) / squareYards, 2, MidpointRounding.AwayFromZero),
                Math.Round(Rupees(r.EffectivePricePaise) / acres, 2, MidpointRounding.AwayFromZero),
                r.Offer.MedianResponseMinutes,
                r.Offer.AvailabilityConfirmedAt,
                r.Score,
                new ScoreBreakdownResponse(r.Breakdown.Price, r.Breakdown.Authority, r.Breakdown.Checks, r.Breakdown.Response, r.Breakdown.Freshness),
                r.Badges.Select(Vocabulary.Code).ToList())).ToList());

        return Results.Ok(response);
    }

    /// <summary>
    /// Owner of record: set by a trust reviewer. Confirmed agent: confirmed by the current owner of record.
    /// A confirmation made by a previous owner no longer counts once ownership changes.
    /// </summary>
    internal static Authority AuthorityFor(Guid listerId, ParcelPartyRecord? owner, IReadOnlyCollection<ParcelPartyRecord> parties)
    {
        if (owner is null)
        {
            return Authority.NotConfirmed;
        }

        if (owner.UserId == listerId)
        {
            return Authority.OwnerOfRecord;
        }

        return parties.Any(p => p.Role == Vocabulary.AuthorisedAgent && p.UserId == listerId && p.ConfirmedBy == owner.UserId)
            ? Authority.OwnerConfirmedAgent
            : Authority.NotConfirmed;
    }

    internal static async Task<IResult> ConfirmAgentAsync(Guid parcelId, Guid agentUserId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var isOwner = await db.ParcelParties.AnyAsync(p => p.ParcelId == parcelId && p.UserId == userId && p.Role == Vocabulary.OwnerOfRecord, ct);
        if (!isOwner)
        {
            return ApiResults.Forbidden("parcel.not_owner_of_record", "Only the owner of record can confirm agents for this land.");
        }

        if (agentUserId == userId)
        {
            return ApiResults.Invalid("parcel.agent_is_owner", "You are the owner; you do not need to confirm yourself.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == agentUserId, ct))
        {
            return ApiResults.NotFound("Agent");
        }

        var existing = await db.ParcelParties.FirstOrDefaultAsync(p => p.ParcelId == parcelId && p.UserId == agentUserId && p.Role == Vocabulary.AuthorisedAgent, ct);
        var now = clock.UtcNow;
        if (existing is null)
        {
            db.ParcelParties.Add(new ParcelPartyRecord { ParcelId = parcelId, UserId = agentUserId, Role = Vocabulary.AuthorisedAgent, ConfirmedAt = now, ConfirmedBy = userId });
        }
        else
        {
            // Re-confirming after an ownership change makes the confirmation count again.
            existing.ConfirmedAt = now;
            existing.ConfirmedBy = userId;
        }

        audit.Add(userId, "parcel.agent_confirmed", "parcel", parcelId.ToString(), new { agentUserId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "parcel.concurrent_update", "This was changed at the same moment. Please try again.");
        }

        return Results.NoContent();
    }

    /// <summary>The owner confirms whoever made a given offer, without needing to know their account id.</summary>
    private static async Task<IResult> ConfirmListerOfOfferAsync(Guid parcelId, Guid offerId, HttpContext http, BhoomisetuDbContext db, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var listerId = await db.Offers.AsNoTracking()
            .Where(o => o.Id == offerId && o.ParcelId == parcelId && o.Status == "active")
            .Select(o => (Guid?)o.ListerUserId)
            .FirstOrDefaultAsync(ct);
        return listerId is { } id
            ? await ConfirmAgentAsync(parcelId, id, http, db, clock, audit, ct)
            : ApiResults.NotFound("Offer");
    }

    internal static async Task<IResult> RevokeAgentAsync(Guid parcelId, Guid agentUserId, HttpContext http, BhoomisetuDbContext db, AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var isOwner = await db.ParcelParties.AnyAsync(p => p.ParcelId == parcelId && p.UserId == userId && p.Role == Vocabulary.OwnerOfRecord, ct);
        if (!isOwner)
        {
            return ApiResults.Forbidden("parcel.not_owner_of_record", "Only the owner of record can change agents for this land.");
        }

        var existing = await db.ParcelParties.FirstOrDefaultAsync(p => p.ParcelId == parcelId && p.UserId == agentUserId && p.Role == Vocabulary.AuthorisedAgent, ct);
        if (existing is null)
        {
            return ApiResults.NotFound("Confirmed agent");
        }

        db.ParcelParties.Remove(existing);
        audit.Add(userId, "parcel.agent_revoked", "parcel", parcelId.ToString(), new { agentUserId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone revoked the same agent a moment earlier; the result the owner wanted already holds.
            return Results.NoContent();
        }

        return Results.NoContent();
    }

    private static decimal Rupees(long paise) => paise / 100m;

    private static OfferResponse ToResponse(OfferRecord o) =>
        new(o.Id, o.ParcelId, o.ListerKind, Rupees(o.AskingPricePaise), o.BuyerBrokeragePercent, o.BrokerageTerms, o.AvailabilityConfirmedAt);
}
