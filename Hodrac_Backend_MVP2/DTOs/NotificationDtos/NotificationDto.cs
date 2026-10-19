using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.NotificationDtos
{
    public class NotificationDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public Guid? TripPostId { get; set; }
        public Guid? ActorUserId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        public static NotificationDto FromEntity(Notification n) => new()
        {
            Id = n.Id,
            Type = n.Type.ToString(),
            TripPostId = n.TripPostId,
            ActorUserId = n.ActorUserId,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt
        };
    }
}
