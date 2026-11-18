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

        // Hodrac Host verification decisions. emailedDirectly: the caller already
        // emailed the host, so the digest must not repeat it.
        Task NotifyHostVerified(Guid hostUserId, Guid adminUserId, bool emailedDirectly);
        Task NotifyHostRejected(Guid hostUserId, Guid adminUserId, bool emailedDirectly);
        Task NotifyHostVerificationRevoked(Guid hostUserId, Guid adminUserId, bool emailedDirectly);

        // An approved traveller withdrew from a hosted trip (seat released;
        // any payment records are kept).
        Task NotifyHostedSeatWithdrawn(TripPost trip, Guid requesterUserId);

        // Hodrac Host payments. emailedDirectly: the traveller already got a
        // receipt / the host already got a dispute email, so the digest skips it.
        Task NotifyPaymentReceived(TripPost trip, Guid payerUserId);
        Task NotifyPaymentConfirmed(TripPost trip, Guid payerUserId, bool emailedDirectly);
        Task NotifyDisputeOpened(TripPost trip, Guid payerUserId, bool emailedDirectly);

        // Hodrac Host background jobs (traveller reminders are always emailed
        // directly by the job, so the digest skips them when emailedDirectly).
        Task NotifyInstalmentDue(TripPost trip, Guid travellerUserId, bool emailedDirectly);
        Task NotifySeatHoldExpiring(TripPost trip, Guid travellerUserId, bool emailedDirectly);
        // Both sides: the traveller (actor: host) and the host (actor: traveller).
        Task NotifySeatHoldExpired(TripPost trip, Guid travellerUserId, bool travellerEmailedDirectly);
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

        public Task NotifyHostVerified(Guid hostUserId, Guid adminUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = hostUserId,
                Type = NotificationType.HostVerified,
                ActorUserId = adminUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifyHostRejected(Guid hostUserId, Guid adminUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = hostUserId,
                Type = NotificationType.HostRejected,
                ActorUserId = adminUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifyHostVerificationRevoked(Guid hostUserId, Guid adminUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = hostUserId,
                Type = NotificationType.HostVerificationRevoked,
                ActorUserId = adminUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifyHostedSeatWithdrawn(TripPost trip, Guid requesterUserId) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = trip.AuthorUserId,
                Type = NotificationType.HostedSeatWithdrawn,
                TripPostId = trip.Id,
                ActorUserId = requesterUserId
            });

        public Task NotifyPaymentReceived(TripPost trip, Guid payerUserId) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = trip.AuthorUserId,
                Type = NotificationType.PaymentReceived,
                TripPostId = trip.Id,
                ActorUserId = payerUserId
            });

        public Task NotifyPaymentConfirmed(TripPost trip, Guid payerUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = payerUserId,
                Type = NotificationType.PaymentConfirmed,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifyDisputeOpened(TripPost trip, Guid payerUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = trip.AuthorUserId,
                Type = NotificationType.DisputeOpened,
                TripPostId = trip.Id,
                ActorUserId = payerUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifyInstalmentDue(TripPost trip, Guid travellerUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = travellerUserId,
                Type = NotificationType.InstalmentDue,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifySeatHoldExpiring(TripPost trip, Guid travellerUserId, bool emailedDirectly) =>
            SaveAndPushAsync(new Notification
            {
                RecipientUserId = travellerUserId,
                Type = NotificationType.SeatHoldExpiring,
                TripPostId = trip.Id,
                ActorUserId = trip.AuthorUserId,
                EmailedAt = emailedDirectly ? DateTime.UtcNow : null
            });

        public Task NotifySeatHoldExpired(TripPost trip, Guid travellerUserId, bool travellerEmailedDirectly) =>
            SaveAndPushAsync(
                new Notification
                {
                    RecipientUserId = travellerUserId,
                    Type = NotificationType.SeatHoldExpired,
                    TripPostId = trip.Id,
                    ActorUserId = trip.AuthorUserId,
                    EmailedAt = travellerEmailedDirectly ? DateTime.UtcNow : null
                },
                new Notification
                {
                    RecipientUserId = trip.AuthorUserId,
                    Type = NotificationType.SeatHoldExpired,
                    TripPostId = trip.Id,
                    ActorUserId = travellerUserId
                });

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
