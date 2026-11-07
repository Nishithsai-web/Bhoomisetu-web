using Bhoomisetu.Api.Persistence;
using Bhoomisetu.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Documents;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    /// <summary>
    /// Price of the buyer (viewer) plan. 0 means every signed-in person counts as a subscribed viewer, so
    /// documents need only a free sign-up. Owners and agents always need a lister subscription.
    /// </summary>
    public decimal ViewerPriceRupees { get; set; }

    /// <summary>Monthly lister price shown in the app; payments are taken outside the app and recorded by a reviewer.</summary>
    public decimal ListerMonthlyPriceRupees { get; set; } = 499;

    /// <summary>Bhoomisetu's own WhatsApp number for subscription enquiries, shown to listers (E.164, optional).</summary>
    public string? SupportWhatsappE164 { get; set; }
}

public static class Audiences
{
    public const string Lister = "lister";
    public const string Viewer = "viewer";
}

/// <summary>Answers "is this person subscribed right now?" the same way for every endpoint.</summary>
public sealed class SubscriptionService(BhoomisetuDbContext db, IClock clock, SubscriptionOptions options)
{
    public bool ViewerPlanIsFree => options.ViewerPriceRupees <= 0;

    /// <summary>End of the person's current period for the audience, or null when not subscribed.</summary>
    public async Task<DateTimeOffset?> ActiveUntilAsync(Guid userId, string audience, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Audience == audience && s.CancelledAt == null && s.StartsAt <= now && s.EndsAt > now)
            .OrderByDescending(s => s.EndsAt)
            .Select(s => (DateTimeOffset?)s.EndsAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> IsListerAsync(Guid userId, CancellationToken ct) => await ActiveUntilAsync(userId, Audiences.Lister, ct) is not null;

    /// <summary>Viewers: any signed-in person when the viewer plan is free, otherwise an active viewer subscription.</summary>
    public async Task<bool> IsViewerAsync(Guid? userId, CancellationToken ct) =>
        userId is { } id && (ViewerPlanIsFree || await ActiveUntilAsync(id, Audiences.Viewer, ct) is not null);

    /// <summary>When a new period should start: now, or when the latest uncancelled period ends if that is later (renewal).</summary>
    public async Task<DateTimeOffset> NextStartAsync(Guid userId, string audience, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var latestEnd = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Audience == audience && s.CancelledAt == null)
            .MaxAsync(s => (DateTimeOffset?)s.EndsAt, ct);
        return latestEnd is { } end && end > now ? end : now;
    }
}
