using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Newsletter;

public record NewsletterRecipient(string Email, string? UserId);

public record NewsletterAudienceStats(
    int ActiveSubscribers,
    int UnsubscribedSubscribers,
    int OptedInUsers,
    int OptedOutUsers,
    int TotalUniqueRecipients);

/// <summary>
/// Who gets the newsletter, and the unsubscribe / opt-out writes.
///
/// Recipients = newsletter sign-ups without UnsubscribedAt
///            ∪ accounts with a confirmed email and NewsletterOptOut = false,
/// deduplicated by trimmed lower-case email (the account's id is kept when
/// both match). Unconfirmed accounts only receive it if they also signed up
/// through the newsletter form.
///
/// Unsubscribing (link or one-click) marks every sign-up row for the address
/// and sets NewsletterOptOut on the account with that email, and skips any
/// Pending delivery for it. Turning the newsletter off in notification
/// preferences does the same for the account's email; turning it back on
/// only clears the account flag. Signing up again through the form clears
/// UnsubscribedAt.
/// </summary>
public class NewsletterAudienceService
{
    private readonly HodracDbContext _db;

    public NewsletterAudienceService(HodracDbContext db) => _db = db;

    public static string Normalize(string email) => NewsletterUnsubscribeTokens.Normalize(email);

    private static bool LooksLikeEmail(string email) =>
        email.Length is > 3 and <= NewsletterDelivery.MaxEmailLength
        && email.IndexOf('@') is > 0 and var at && at < email.Length - 1
        && !email.Any(char.IsWhiteSpace);

    public async Task<List<NewsletterRecipient>> RecipientsAsync(CancellationToken ct = default)
    {
        var subscribers = await _db.NewsLetterEmails.AsNoTracking()
            .Where(n => n.UnsubscribedAt == null && n.UserEmail != null && n.UserEmail != "")
            .Select(n => n.UserEmail)
            .ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.EmailConfirmed && !u.NewsletterOptOut && u.Email != null && u.Email != "")
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        var byEmail = new Dictionary<string, NewsletterRecipient>(StringComparer.Ordinal);
        foreach (var u in users)
        {
            var email = Normalize(u.Email!);
            if (LooksLikeEmail(email)) byEmail[email] = new NewsletterRecipient(email, u.Id);
        }
        foreach (var raw in subscribers)
        {
            var email = Normalize(raw);
            if (LooksLikeEmail(email)) byEmail.TryAdd(email, new NewsletterRecipient(email, null));
        }

        return byEmail.Values.OrderBy(r => r.Email, StringComparer.Ordinal).ToList();
    }

    public async Task<NewsletterAudienceStats> StatsAsync(CancellationToken ct = default)
    {
        var activeSubscribers = await _db.NewsLetterEmails.CountAsync(n => n.UnsubscribedAt == null, ct);
        var unsubscribed = await _db.NewsLetterEmails.CountAsync(n => n.UnsubscribedAt != null, ct);
        var optedIn = await _db.Users.CountAsync(u => u.EmailConfirmed && !u.NewsletterOptOut && u.Email != null && u.Email != "", ct);
        var optedOut = await _db.Users.CountAsync(u => u.NewsletterOptOut, ct);
        var unique = (await RecipientsAsync(ct)).Count;
        return new NewsletterAudienceStats(activeSubscribers, unsubscribed, optedIn, optedOut, unique);
    }

    /// <summary>
    /// Unsubscribes <paramref name="email"/> everywhere. Idempotent; returns
    /// true when anything changed.
    /// </summary>
    public async Task<bool> UnsubscribeAsync(string email, DateTime nowUtc, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        var upper = normalized.ToUpperInvariant();

        var subscribers = await _db.NewsLetterEmails
            .Where(n => n.UserEmail.ToLower() == normalized && n.UnsubscribedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.UnsubscribedAt, nowUtc), ct);

        var users = await _db.Users
            .Where(u => u.NormalizedEmail == upper && !u.NewsletterOptOut)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.NewsletterOptOut, true), ct);

        await SkipPendingAsync(normalized, ct);
        return subscribers + users > 0;
    }

    /// <summary>(digest opt-in, newsletter opt-out) for the notification-preferences endpoints; null when no such user.</summary>
    public async Task<(bool EmailOptIn, bool NewsletterOptOut)?> GetPreferencesAsync(string userId, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.NotificationEmailOptIn, u.NewsletterOptOut })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.NotificationEmailOptIn, row.NewsletterOptOut);
    }

    /// <summary>The notification-preferences toggle for a signed-in user.</summary>
    public async Task SetUserOptOutAsync(string userId, bool optOut, DateTime nowUtc, CancellationToken ct = default)
    {
        await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.NewsletterOptOut, optOut), ct);
        if (!optOut) return;

        var email = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) return;

        var normalized = Normalize(email);
        await _db.NewsLetterEmails
            .Where(n => n.UserEmail.ToLower() == normalized && n.UnsubscribedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.UnsubscribedAt, nowUtc), ct);
        await SkipPendingAsync(normalized, ct);
    }

    private Task<int> SkipPendingAsync(string normalizedEmail, CancellationToken ct) =>
        _db.NewsletterDeliveries
            .Where(d => d.Email == normalizedEmail && d.Status == NewsletterDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, NewsletterDeliveryStatus.Skipped)
                .SetProperty(d => d.LastError, "Unsubscribed"), ct);
}
