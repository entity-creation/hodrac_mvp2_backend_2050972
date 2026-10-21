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
        TripCancelled       // a trip you posted or asked to join was cancelled
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
