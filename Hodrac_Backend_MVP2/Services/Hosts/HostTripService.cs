using System.Globalization;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Hosts;

/// <summary>
/// The host's side of their hosted trips: the trips list, the manifest (JSON
/// and CSV rows) and the WhatsApp reminder link. Read-only; money changes go
/// through <see cref="PaymentService"/>.
///
/// Ownership: a trip is "mine" only when I'm its author AND it's attached to
/// my own host profile. Anything else (other people's trips, community
/// trips) is treated as not found.
/// </summary>
public class HostTripService
{
    // Reports and due dates run on West Africa Time (UTC+1, no DST).
    public static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    private readonly HodracDbContext _db;
    private readonly IConfiguration _config;

    public HostTripService(HodracDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public static DateOnly TodayWat(DateTime utcNow) => DateOnly.FromDateTime(utcNow + Wat);

    /// <summary>The caller's hosted trip (with host profile), or null (→ 404).</summary>
    public Task<TripPost?> GetOwnedHostedTripAsync(Guid userId, Guid tripId, CancellationToken ct = default) =>
        _db.TripPosts.AsNoTracking()
            .Include(t => t.HostProfile)
            .FirstOrDefaultAsync(t => t.Id == tripId
                && t.IsHostedTrip
                && t.AuthorUserId == userId
                && t.HostProfile != null
                && t.HostProfile.UserId == userId, ct);

    public async Task<string> TripTitleAsync(Guid tripId, CancellationToken ct = default) =>
        (await TripTitlesAsync(new[] { tripId }, ct)).GetValueOrDefault(tripId) ?? "Trip";

    public async Task<Dictionary<Guid, string>> TripTitlesAsync(IReadOnlyCollection<Guid> tripIds, CancellationToken ct = default)
    {
        var rows = await _db.TripPosts.AsNoTracking()
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Caption,
                t.FreeTextDestination,
                DestinationName = t.Destination != null ? t.Destination.DestinationName : null,
                WishlistName = t.Wishlist != null ? t.Wishlist.WishlistName : null,
            })
            .ToListAsync(ct);
        return rows.ToDictionary(
            r => r.Id,
            r => PaymentService.TripTitle(r.DestinationName, r.WishlistName, r.FreeTextDestination, r.Caption));
    }

    // ── Trips list ───────────────────────────────────────────────────────────

    /// <summary>
    /// My hosted trips: upcoming first (soonest first, undated last), then past
    /// trips (most recent first).
    /// </summary>
    public async Task<List<HostTripSummaryDto>> ListTripsAsync(Guid userId, CancellationToken ct = default)
    {
        var trips = await _db.TripPosts.AsNoTracking()
            .Where(t => t.IsHostedTrip
                && t.AuthorUserId == userId
                && t.HostProfile != null
                && t.HostProfile.UserId == userId)
            .ToListAsync(ct);
        if (trips.Count == 0) return new List<HostTripSummaryDto>();

        var tripIds = trips.Select(t => t.Id).ToList();
        var titles = await TripTitlesAsync(tripIds, ct);
        var interests = await _db.TripInterests.AsNoTracking()
            .Where(i => tripIds.Contains(i.TripPostId))
            .Select(i => new { i.TripPostId, i.Status, i.PaymentStatus, i.AmountPaidKobo, i.AmountPaidViaHodracKobo })
            .ToListAsync(ct);
        var byTrip = interests.ToLookup(i => i.TripPostId);

        var today = TodayWat(DateTime.UtcNow);
        var nextDepartureId = trips
            .Where(t => t.StartDate is { } s && s >= today && t.Status != TripPostStatus.Cancelled)
            .OrderBy(t => t.StartDate)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefault();

        return trips
            .Select(t =>
            {
                var price = t.PricePerSeatKobo ?? 0;
                var rows = byTrip[t.Id].ToList();
                var seats = rows.Where(i => i.Status == TripInterestStatus.Approved && i.PaymentStatus != InterestPaymentStatus.Expired).ToList();
                var via = rows.Sum(i => i.AmountPaidViaHodracKobo);
                var total = rows.Sum(i => i.AmountPaidKobo);
                return new HostTripSummaryDto(
                    t.Id,
                    titles.GetValueOrDefault(t.Id) ?? "Trip",
                    t.StartDate,
                    t.EndDate,
                    t.Status.ToString(),
                    t.IsListed,
                    t.IsHidden,
                    t.MaxGroupSize,
                    price,
                    Approved: seats.Count,
                    SeatsWithDeposit: seats.Count(i => i.AmountPaidKobo > 0),
                    SeatsPaidInFull: seats.Count(i => i.PaymentStatus == InterestPaymentStatus.PaidInFull),
                    CollectedKobo: via,
                    CollectedManualKobo: total - via,
                    CollectedTotalKobo: total,
                    OwedKobo: seats.Sum(i => Math.Max(0, price - i.AmountPaidKobo)),
                    IsUpcoming: t.StartDate is null || t.StartDate >= today,
                    IsNextDeparture: t.Id == nextDepartureId);
            })
            .OrderByDescending(t => t.IsUpcoming)
            .ThenBy(t => t.IsUpcoming ? (t.StartDate ?? DateOnly.MaxValue).DayNumber : -(t.StartDate ?? DateOnly.MinValue).DayNumber)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Manifest ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The trip's travellers (declined requests left out; withdrawn kept with
    /// their status and money). Sorted paid in full, part paid (deposit or
    /// more), awaiting deposit, then everyone else; by name within a group.
    /// </summary>
    public async Task<ManifestDto> BuildManifestAsync(TripPost trip, CancellationToken ct = default)
    {
        var interests = await _db.TripInterests.AsNoTracking()
            .Where(i => i.TripPostId == trip.Id && i.Status != TripInterestStatus.Declined)
            .ToListAsync(ct);

        var userIds = interests.Select(i => i.RequesterUserId.ToString()).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.PhoneNumber })
            .ToDictionaryAsync(u => u.Id, ct);

        var interestIds = interests.Select(i => i.Id).ToList();
        var lastReminders = await _db.PaymentReminderLogs.AsNoTracking()
            .Where(r => interestIds.Contains(r.TripInterestId))
            .GroupBy(r => r.TripInterestId)
            .Select(g => new { g.Key, Last = g.Max(r => r.SentAt) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);

        var price = trip.PricePerSeatKobo ?? 0;
        var rows = interests
            .Select(i =>
            {
                var user = users.GetValueOrDefault(i.RequesterUserId.ToString());
                var owes = Owes(trip, i);
                ScheduleLine? next = owes > 0
                    ? PaymentSchedule.Allocate(trip, i.AmountPaidKobo).FirstOrDefault(l => !l.IsPaid)
                    : null;
                return new ManifestRowDto(
                    i.Id,
                    i.RequesterUserId,
                    string.IsNullOrWhiteSpace(user?.DisplayName) ? "Traveller" : user!.DisplayName,
                    user?.Email,
                    string.IsNullOrWhiteSpace(i.ContactPhone) ? NullIfBlank(user?.PhoneNumber) : i.ContactPhone,
                    i.Status.ToString(),
                    i.PaymentStatus.ToString(),
                    i.TermsVersionAccepted,
                    i.TermsAcceptedAt,
                    i.AmountPaidKobo,
                    i.AmountPaidViaHodracKobo,
                    i.AmountPaidKobo - i.AmountPaidViaHodracKobo,
                    owes,
                    next is null ? null : next.DueDate ?? SeatHoldDay(i.SeatHoldExpiresAt),
                    next?.RemainingKobo,
                    i.SeatHoldExpiresAt,
                    lastReminders.TryGetValue(i.Id, out var last) ? last : null,
                    i.RequestedAt);
            })
            .OrderBy(r => SortRank(r))
            .ThenBy(r => r.TravellerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RequestedAt)
            .ToList();

        var seats = rows.Where(r => r.InterestStatus == nameof(TripInterestStatus.Approved)
            && r.PaymentStatus != nameof(InterestPaymentStatus.Expired)).ToList();
        var totals = new ManifestTotalsDto(
            Travellers: rows.Count,
            Approved: seats.Count,
            SeatsWithDeposit: seats.Count(r => r.PaidKobo > 0),
            SeatsPaidInFull: seats.Count(r => r.PaymentStatus == nameof(InterestPaymentStatus.PaidInFull)),
            PaidKobo: rows.Sum(r => r.PaidKobo),
            ViaHodracKobo: rows.Sum(r => r.ViaHodracKobo),
            ManualKobo: rows.Sum(r => r.ManualKobo),
            OwesKobo: rows.Sum(r => r.OwesKobo));

        return new ManifestDto(
            trip.Id,
            await TripTitleAsync(trip.Id, ct),
            trip.StartDate,
            trip.EndDate,
            trip.MaxGroupSize,
            price,
            trip.Currency,
            trip.TermsVersion,
            rows,
            totals);
    }

    /// <summary>What an approved, not-expired traveller still owes; 0 for everyone else.</summary>
    public static long Owes(TripPost trip, TripInterest interest) =>
        interest.Status == TripInterestStatus.Approved && interest.PaymentStatus != InterestPaymentStatus.Expired
            ? Math.Max(0, (trip.PricePerSeatKobo ?? 0) - interest.AmountPaidKobo)
            : 0;

    private static int SortRank(ManifestRowDto row)
    {
        if (row.InterestStatus != nameof(TripInterestStatus.Approved)) return 3;
        return row.PaymentStatus switch
        {
            nameof(InterestPaymentStatus.PaidInFull) => 0,
            nameof(InterestPaymentStatus.PartPaid) or nameof(InterestPaymentStatus.DepositPaid) => 1,
            nameof(InterestPaymentStatus.AwaitingDeposit) => 2,
            _ => 3
        };
    }

    private static DateOnly? SeatHoldDay(DateTime? seatHoldExpiresAt) =>
        seatHoldExpiresAt is { } at ? DateOnly.FromDateTime(at + Wat) : null;

    // ── WhatsApp nudge ───────────────────────────────────────────────────────

    /// <summary>
    /// A wa.me link with a prefilled reminder (no messaging provider in v1).
    /// Null phone → the caller returns 422.
    /// </summary>
    public WhatsAppNudgeDto? BuildWhatsAppNudge(TripPost trip, string tripTitle, ManifestRowDto row)
    {
        var phone = NormalizeWhatsAppPhone(row.Phone);
        if (phone is null) return null;

        var firstName = row.TravellerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var hostName = trip.HostProfile?.DisplayName ?? "your host";
        var payUrl = $"{FrontendBaseUrl()}/trips/{trip.Id}";

        var message = $"Hi {firstName}, it's {hostName} with a quick reminder about {tripTitle}{DateRange(trip.StartDate, trip.EndDate)}. "
            + $"You have {PaymentService.Naira(row.OwesKobo)} left to pay for your seat.";
        if (row.NextDueAmountKobo is { } nextAmount)
            message += row.NextDueDate is { } due
                ? $" Next payment: {PaymentService.Naira(nextAmount)} due {due.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
                : $" Next payment: {PaymentService.Naira(nextAmount)}.";
        message += $" Pay securely on Hodrac: {payUrl}";

        return new WhatsAppNudgeDto($"https://wa.me/{phone}?text={Uri.EscapeDataString(message)}", phone, message);
    }

    /// <summary>
    /// Digits-only international number for wa.me: "+234 803 123 4567",
    /// "0803 123 4567" (Nigerian local), "803 123 4567", "00234…" and
    /// "2340803…" all become "2348031234567". Null when it isn't a phone number.
    /// </summary>
    public static string? NormalizeWhatsAppPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());

        if (!trimmed.StartsWith('+'))
        {
            if (digits.StartsWith("00")) digits = digits[2..];
            else if (digits.Length == 11 && digits[0] == '0') digits = "234" + digits[1..];
            else if (digits.Length == 10 && digits[0] is '7' or '8' or '9') digits = "234" + digits;
        }
        // "+234 (0)803…" / "2340803…": drop the trunk zero after the country code.
        if (digits.Length == 14 && digits.StartsWith("2340")) digits = "234" + digits[4..];

        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }

    private string FrontendBaseUrl() => (_config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');

    private static string DateRange(DateOnly? start, DateOnly? end) =>
        start is null ? ""
        : end is null || end == start ? $" ({start.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})"
        : $" ({start.Value.ToString("d MMM", CultureInfo.InvariantCulture)} – {end.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
