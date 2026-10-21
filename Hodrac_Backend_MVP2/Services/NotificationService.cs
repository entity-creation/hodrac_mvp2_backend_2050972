using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.NotificationDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.SignalR;

namespace Hodrac_Backend_MVP2.Services
{
    public interface INotificationService
    {
        Task NotifyInterestReceived(TripPost trip, Guid requesterUserId);
        Task NotifyInterestApproved(TripPost trip, Guid requesterUserId);
        Task NotifyInterestDeclined(TripPost trip, Guid requesterUserId);
        Task NotifyNewThreadMessage(Guid tripPostId, List<Guid> recipientUserIds, Guid senderUserId);
        Task NotifyTripCancelled(TripPost trip, IEnumerable<Guid> recipientUserIds, Guid? actorUserId);
    }

    // Creates the in-app Notification rows and pushes each one to its
    // recipient over NotificationHub. The email digest reads from this same
    // table, so this is the single place events are recorded.
    public class NotificationService : INotificationService
    {
        private readonly HodracDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public NotificationService(HodracDbContext db, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public Task NotifyInterestReceived(TripPost trip, Guid requesterUserId) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = trip.AuthorUserId,
                Type = NotificationType.InterestReceived,
                TripPostId = trip.Id,
                ActorUserId = requesterUserId
            });

        public Task NotifyInterestApproved(TripPost trip, Guid requesterUserId) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = requesterUserId,
                Type = NotificationType.InterestApproved,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId
            });

        public Task NotifyInterestDeclined(TripPost trip, Guid requesterUserId) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = requesterUserId,
                Type = NotificationType.InterestDeclined,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId
            });

        public Task NotifyNewThreadMessage(Guid tripPostId, List<Guid> recipientUserIds, Guid senderUserId) =>
            SaveAndPushAsync(recipientUserIds.Select(recipientId => new Notification
            {
                RecipientUserId = recipientId,
                Type = NotificationType.NewThreadMessage,
                TripPostId = tripPostId,
                ActorUserId = senderUserId
            }).ToArray());

        public Task NotifyTripCancelled(TripPost trip, IEnumerable<Guid> recipientUserIds, Guid? actorUserId) =>
            SaveAndPushAsync(recipientUserIds.Distinct().Select(recipientId => new Notification
            {
                RecipientUserId = recipientId,
                Type = NotificationType.TripCancelled,
                TripPostId = trip.Id,
                ActorUserId = actorUserId
            }).ToArray());

        private async Task SaveAndPushAsync(params Notification[] notifications)
        {
            if (notifications.Length == 0) return;

            _db.Notifications.AddRange(notifications);
            await _db.SaveChangesAsync();

            foreach (var notification in notifications)
            {
                await _hub.Clients
                    .User(notification.RecipientUserId.ToString())
                    .SendAsync("NotificationReceived", NotificationDto.FromEntity(notification));
            }
        }
    }
}
