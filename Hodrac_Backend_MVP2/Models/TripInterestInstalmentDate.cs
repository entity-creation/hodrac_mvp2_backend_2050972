using System;

namespace Hodrac_Backend_MVP2.Models
{
    // A traveller's chosen due date for one instalment of a TravellerChooses
    // hosted trip. Key (TripInterestId, Sequence); deleted with the interest.
    // Written at accept-terms and by PUT .../instalment-dates; cleared when
    // the terms acceptance is reset (withdraw, request again, re-approval
    // after a terms change) or the trip's schedule setup changes.
    public class TripInterestInstalmentDate
    {
        public Guid TripInterestId { get; set; }
        public TripInterest? TripInterest { get; set; }

        public int Sequence { get; set; }
        public DateOnly DueDate { get; set; }

        public DateTime ChosenAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
