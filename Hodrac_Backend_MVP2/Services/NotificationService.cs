using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Services
{
    public interface INotificationService
    {
        Task NotifyInterestReceived(TripPost trip, Guid requesterUserId);
        Task NotifyInterestApproved(TripPost trip, Guid requesterUserId);
        Task NotifyInterestDeclined(TripPost trip, Guid requesterUserId);
        Task NotifyNewThreadMessage(Guid tripPostId, List<Guid> recipientUserIds, Guid senderUserId);
    }

    // Creates the in-app Notification rows. Real-time push (SignalR /
    // web push) and the email digest both read from this same table, so
    // this is the single place events are recorded.
    public class NotificationService : INotificationService
    {
        private readonly HodracDbContext _db;

        public NotificationService(HodracDbContext db)
        {
            _db = db;
        }

        public async Task NotifyInterestReceived(TripPost trip, Guid requesterUserId)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = trip.AuthorUserId,
                Type = NotificationType.InterestReceived,
                TripPostId = trip.Id,
                ActorUserId = requesterUserId
            });
            await _db.SaveChangesAsync();

            // TODO: push over real-time channel to trip.AuthorUserId
        }

        public async Task NotifyInterestApproved(TripPost trip, Guid requesterUserId)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = requesterUserId,
                Type = NotificationType.InterestApproved,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId
            });
            await _db.SaveChangesAsync();
        }

        public async Task NotifyInterestDeclined(TripPost trip, Guid requesterUserId)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = requesterUserId,
                Type = NotificationType.InterestDeclined,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId
            });
            await _db.SaveChangesAsync();
        }

        public async Task NotifyNewThreadMessage(Guid tripPostId, List<Guid> recipientUserIds, Guid senderUserId)
        {
            foreach (var recipientId in recipientUserIds)
            {
                _db.Notifications.Add(new Notification
                {
                    RecipientUserId = recipientId,
                    Type = NotificationType.NewThreadMessage,
                    TripPostId = tripPostId,
                    ActorUserId = senderUserId
                });
            }
            await _db.SaveChangesAsync();
        }
    }
}
