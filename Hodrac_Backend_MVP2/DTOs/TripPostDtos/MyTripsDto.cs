using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.TripPostDtos
{
    // One row per interest the current user has sent (any non-withdrawn
    // status), with the trip it's for attached so the page doesn't need a
    // second round trip per card.
    public record MyTripRequestDto(
        Guid InterestId,
        TripInterestStatus Status,
        string? Message,
        DateTime RequestedAt,
        DateTime? RespondedAt,
        TripPostDto Trip);

    // One row per trip the current user has posted, with how many people
    // are still waiting on a response and whether a thread exists yet
    // (it won't until the first approval).
    public record MyTripPostDto(
        TripPostDto Trip,
        int PendingInterestCount,
        bool HasThread);
}
