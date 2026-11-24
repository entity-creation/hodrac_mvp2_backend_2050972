using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>
/// Loads travellers' chosen instalment dates (TravellerChooses trips) as the
/// sequence → date maps that PaymentSchedule.Allocate(trip, paid, travellerDates) takes.
/// One query for any number of interests; interests without dates get no entry.
/// </summary>
public static class TravellerInstalmentDates
{
    public static async Task<Dictionary<Guid, Dictionary<int, DateOnly>>> LoadAsync(
        HodracDbContext db, IReadOnlyCollection<Guid> interestIds, CancellationToken ct = default)
    {
        if (interestIds.Count == 0) return new Dictionary<Guid, Dictionary<int, DateOnly>>();
        var rows = await db.TripInterestInstalmentDates.AsNoTracking()
            .Where(d => interestIds.Contains(d.TripInterestId))
            .Select(d => new { d.TripInterestId, d.Sequence, d.DueDate })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.TripInterestId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Sequence, r => r.DueDate));
    }

    /// <summary>One interest's dates (empty when none).</summary>
    public static async Task<Dictionary<int, DateOnly>> LoadAsync(HodracDbContext db, Guid interestId, CancellationToken ct = default) =>
        (await LoadAsync(db, new[] { interestId }, ct)).GetValueOrDefault(interestId) ?? new Dictionary<int, DateOnly>();
}
