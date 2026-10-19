using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hodrac_Backend_MVP2.Infrastucture.SignalR
{
    // Server-push only. ThreadController broadcasts "ThreadMessageReceived"
    // to each thread participant via Clients.Users(...), which resolves
    // connections through CustomUserIdProvider (NameIdentifier claim).
    [Authorize]
    public class TripThreadHub : Hub
    {
    }
}
