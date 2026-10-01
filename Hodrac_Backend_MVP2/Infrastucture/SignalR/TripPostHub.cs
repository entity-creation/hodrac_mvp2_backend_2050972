using Microsoft.AspNetCore.SignalR;

namespace Hodrac_Backend_MVP2.Infrastucture.SignalR
{
    public class TripPostHub : Hub
    {
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
    }
}
