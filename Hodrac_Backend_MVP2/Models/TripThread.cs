using System;
using System.Collections.Generic;

namespace Hodrac_Backend_MVP2.Models
{
    // One thread per trip post. Created lazily the first time an interest
    // is approved.
    public class TripThread
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TripPostId { get; set; }
        public TripPost? TripPost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TripThreadParticipant> Participants { get; set; } = new List<TripThreadParticipant>();
        public ICollection<ThreadMessage> Messages { get; set; } = new List<ThreadMessage>();
    }

    public class TripThreadParticipant
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TripThreadId { get; set; }
        public Guid UserId { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Used to compute unread-message badges without a separate table.
        public DateTime? LastReadAt { get; set; }
    }

    public class ThreadMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TripThreadId { get; set; }
        public Guid SenderUserId { get; set; }

        public string Body { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
