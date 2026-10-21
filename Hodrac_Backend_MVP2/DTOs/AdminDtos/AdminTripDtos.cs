using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Trips ────────────────────────────────────────────────────────────────

    public record AdminTripAuthorDto(Guid UserId, string? DisplayName, string? Email);

    public record AdminTripInterestCountsDto(int Requested, int Approved, int Declined, int Withdrawn);

    public record AdminTripSummaryDto(
        Guid Id,
        string Caption,             // truncated to ~140 chars
        string DestinationLabel,
        AdminTripAuthorDto Author,
        TripPostStatus Status,
        bool IsHidden,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? MaxGroupSize,
        AdminTripInterestCountsDto InterestCounts,
        int MessageCount,
        DateTime CreatedAt
    );

    public record AdminTripInterestDto(
        Guid Id,
        Guid RequesterUserId,
        string? RequesterName,
        string? RequesterEmail,
        TripInterestStatus Status,
        string? Message,
        DateTime RequestedAt,
        DateTime? RespondedAt
    );

    public record AdminTripDetailDto(
        Guid Id,
        string Caption,
        string DestinationLabel,
        Guid? DestinationId,
        Guid? WishlistId,
        string? FreeTextDestination,
        string? CoverImageUrl,
        AdminTripAuthorDto Author,
        TripPostStatus Status,
        DateOnly? StartDate,
        DateOnly? EndDate,
        bool IsDateFlexible,
        int? MaxGroupSize,
        bool IsHidden,
        DateTime? HiddenAt,
        string? HiddenReason,
        Guid? HiddenByUserId,
        AdminTripInterestCountsDto InterestCounts,
        List<AdminTripInterestDto> Interests,
        Guid? ThreadId,
        int ThreadParticipantCount,
        int MessageCount,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record AdminThreadMessageDto(
        Guid Id,
        Guid SenderUserId,
        string? SenderName,
        string Body,
        DateTime CreatedAt
    );

    public record HideTripRequest(
        [Required, MaxLength(TripPost.MaxHiddenReasonLength)] string Reason
    );

    public record CancelTripRequest(
        [MaxLength(TripPost.MaxHiddenReasonLength)] string? Reason
    );
}
