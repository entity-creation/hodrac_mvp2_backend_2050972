using System;

namespace Hodrac_Backend_MVP2.Models
{
    public enum TripInterestStatus
    {
        Requested,
        Approved,
        Declined,
        Withdrawn
    }

    // Created the moment a user taps "Interested" on a trip post.
    // Stays in "Requested" until the trip author approves or declines it.
    public class TripInterest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TripPostId { get; set; }
        public TripPost? TripPost { get; set; }

        public Guid RequesterUserId { get; set; }

        public TripInterestStatus Status { get; set; } = TripInterestStatus.Requested;

        // Optional short note the requester can attach, e.g. "I've been to
        // Beijing before, happy to help plan!"
        public string? Message { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
    }
}
