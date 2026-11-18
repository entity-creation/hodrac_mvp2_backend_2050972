using System;

namespace Hodrac_Backend_MVP2.Models
{
    public enum NotificationType
    {
        InterestReceived,   // someone tapped Interested on your trip
        InterestApproved,   // your request to join was approved
        InterestDeclined,   // your request to join was declined
        NewThreadMessage,   // new message in a trip thread you're in
        TripStartingSoon,   // reminder, optional future use
        TripCancelled,      // a trip you posted or asked to join was cancelled

        // Hodrac Host (append only: stored as int)
        HostVerified,       // an admin verified your host profile
        HostRejected,       // an admin rejected your host verification
        HostedSeatWithdrawn, // an approved traveller left your hosted trip

        // Hodrac Host payments
        PaymentReceived,    // (host) a traveller paid for a seat
        PaymentConfirmed,   // (traveller) your payment went through
        InstalmentDue,      // (traveller) an instalment is due soon
        SeatHoldExpiring,   // (traveller) pay the deposit before your seat hold ends
        SeatHoldExpired,    // (both) an unpaid seat hold expired
        DisputeOpened,      // (host) a traveller disputed a payment

        // Hodrac Host verification (append only)
        HostVerificationRevoked // an admin removed your Verified host badge
    }

    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid RecipientUserId { get; set; }
        public NotificationType Type { get; set; }

        public Guid? TripPostId { get; set; }
        public Guid? ActorUserId { get; set; } // who caused the event, if anyone

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Set by EmailDigestService once this notification has been included in
        // a digest email, so it is never emailed twice.
        public DateTime? EmailedAt { get; set; }
    }
}
