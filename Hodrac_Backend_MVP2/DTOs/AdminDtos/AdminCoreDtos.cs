using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // Paged lists reuse AdminPagedResult<T> from DTOs.PartnerDtos.

    // ─── Me ───────────────────────────────────────────────────────────────────

    public record AdminMeDto(string Email, string DisplayName, bool IsAdmin);

    // ─── Overview ─────────────────────────────────────────────────────────────

    public record AdminOverviewDto(
        int Days,
        DateTime WindowStart,               // UTC midnight of the first day in the window
        AdminOverviewTotalsDto Totals,
        AdminOverviewWindowDto Window
    );

    public record AdminOverviewTotalsDto(
        int Users,
        int ConfirmedUsers,
        int OnboardedUsers,
        int Templates,
        int UserWishlists,
        int Destinations,
        Dictionary<string, int> TripsByStatus,     // every TripPostStatus present, zero-filled
        int OpenTrips,
        int PendingInterests,                      // TripInterest.Status == Requested
        int ThreadMessages,
        int NewsletterSubscribers,
        Dictionary<string, int> LeadsByStatus,     // every PartnerLeadStatuses value, zero-filled
        int PartnerClicks
    );

    /// <summary>
    /// Daily series are UTC date buckets computed in SQL. Days with no rows are
    /// omitted (sparse) — the client should zero-fill between WindowStart and today.
    /// </summary>
    public record AdminOverviewWindowDto(
        int ActiveUsers,                           // LastActiveAt within the window
        int NewUsers,
        int NewTrips,
        int PartnerClicks,
        List<AdminDailyCountDto> NewUsersPerDay,
        List<AdminDailyCountDto> NewTripsPerDay,
        List<AdminDailyCountDto> PartnerClicksPerDay
    );

    public record AdminDailyCountDto(DateOnly Date, int Count);

    // ─── Users ────────────────────────────────────────────────────────────────

    public record AdminUserRowDto(
        string Id,
        string? Email,
        string DisplayName,
        string AvatarUrl,
        string? CountryCode,
        bool EmailConfirmed,
        bool HasCompletedOnboarding,
        bool IsLocked,
        DateTimeOffset? LockoutEnd,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset? LastActiveAt
    );

    public record AdminUserDetailDto(
        string Id,
        string? Email,
        string DisplayName,
        string AvatarUrl,
        string? CountryCode,
        bool EmailConfirmed,
        bool HasCompletedOnboarding,
        bool IsLocked,
        DateTimeOffset? LockoutEnd,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset? LastActiveAt,
        bool NotificationEmailOptIn,
        int AccessFailedCount,
        AdminUserCountsDto Counts
    );

    public record AdminUserCountsDto(
        int TripsPosted,
        int TripJoinRequests,
        int WishlistsOwned,             // non-template wishlists with OwnerUserId = user
        int SavedWishlists,
        int SavedDestinations,
        int UnreadNotifications,
        int ActiveSessions              // non-revoked, unexpired refresh tokens
    );

    /// <summary>Until null = effectively permanent (now + 100 years). Must be in the future.</summary>
    public record AdminLockUserRequest(
        DateTimeOffset? Until,
        [MaxLength(500)] string? Reason
    );

    // ─── Newsletter ───────────────────────────────────────────────────────────

    public record AdminNewsletterSubscriberDto(Guid EmailId, string UserEmail);
}
