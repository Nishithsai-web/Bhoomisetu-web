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
    /// <summary>One paid month. Fixed length, so a period bought on 31 January is as long as one bought on 1 March.</summary>
    public const int DaysPerMonth = 30;

    public bool ViewerPlanIsFree => options.ViewerPriceRupees <= 0;

    /// <summary>
    /// When the person's subscription runs out, counting renewals that follow on without a gap; null when not
    /// subscribed right now. A lister who renewed early sees the later date, so they do not pay twice.
    /// </summary>
    public async Task<DateTimeOffset?> ActiveUntilAsync(Guid userId, string audience, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var periods = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Audience == audience && s.CancelledAt == null && s.EndsAt > now)
            .OrderBy(s => s.StartsAt)
            .Select(s => new { s.StartsAt, s.EndsAt })
            .ToListAsync(ct);

        DateTimeOffset? until = null;
        foreach (var period in periods)
        {
            if (until is null)
            {
                if (period.StartsAt <= now)
                {
                    until = period.EndsAt;
                }
            }
            else if (period.StartsAt <= until)
            {
                until = period.EndsAt > until ? period.EndsAt : until;
            }
        }

        return until;
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
