using Hodrac_Backend_MVP2.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.SignalR
{
    // Server-push only. TripsController / AdminTripsController broadcast through
    // IHubContext<TripPostHub>: feed events to All, per-user events to "user:{id}",
    // and host manifest updates to "trip-host:{tripId}" (trip author only).
    [Authorize]
    public class TripPostHub : Hub
    {
        private readonly HodracDbContext _db;

        public TripPostHub(HodracDbContext db) => _db = db;

        public static string TripHostGroup(Guid tripId) => $"trip-host:{tripId}";

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    $"user:{userId}"
                );
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    $"user:{userId}"
                );
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>Live manifest updates for a trip. Only the trip's author may join.</summary>
        public async Task JoinTripHost(Guid tripId)
        {
            if (!Guid.TryParse(Context.UserIdentifier, out var userId))
                throw new HubException("Not allowed.");

            var isAuthor = await _db.TripPosts
                .AsNoTracking()
                .AnyAsync(t => t.Id == tripId && t.AuthorUserId == userId, Context.ConnectionAborted);

            if (!isAuthor)
                throw new HubException("Only the trip's host can follow its manifest.");

            await Groups.AddToGroupAsync(Context.ConnectionId, TripHostGroup(tripId));
        }

        public async Task LeaveTripHost(Guid tripId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, TripHostGroup(tripId));
    }
}
