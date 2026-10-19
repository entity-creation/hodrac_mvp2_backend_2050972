using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hodrac_Backend_MVP2.Infrastucture.SignalR
{
    // Server-push only. NotificationService sends "NotificationReceived" to
    // the recipient via Clients.User(...), resolved by CustomUserIdProvider.
    [Authorize]
    public class NotificationHub : Hub
    {
    }
}
