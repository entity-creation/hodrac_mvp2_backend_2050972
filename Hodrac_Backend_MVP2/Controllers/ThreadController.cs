using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/trips/{tripId:guid}/thread")]
    public class ThreadController : ControllerBase
    {
        private readonly HodracDbContext _db;
        private readonly INotificationService _notifications;
        private readonly ICurrentUserService _currentUser;

        public ThreadController(HodracDbContext db, INotificationService notifications, ICurrentUserService currentUser)
        {
            _db = db;
            _notifications = notifications;
            _currentUser = currentUser;
        }

        private Guid CurrentUserId => _currentUser.UserId;

        private async Task<TripThread?> GetThreadIfMemberAsync(Guid tripId)
        {
            var thread = await _db.TripThreads
                .Include(t => t.Participants)
                .FirstOrDefaultAsync(t => t.TripPostId == tripId);

            if (thread is null) return null;
            if (!thread.Participants.Any(p => p.UserId == CurrentUserId)) return null;

            return thread;
        }

        [HttpGet]
        public async Task<IActionResult> GetThread(Guid tripId)
        {
            var thread = await GetThreadIfMemberAsync(tripId);
            if (thread is null) return NotFound("No thread yet, or you're not a participant.");

            return Ok(new
            {
                thread.Id,
                Participants = thread.Participants.Select(p => new { p.UserId, p.JoinedAt })
            });
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetMessages(Guid tripId, [FromQuery] int page = 0, [FromQuery] int pageSize = 50)
        {
            var thread = await GetThreadIfMemberAsync(tripId);
            if (thread is null) return NotFound();

            var messages = await _db.ThreadMessages
                .Where(m => m.TripThreadId == thread.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Mark the caller's last-read timestamp for unread badges elsewhere.
            var participant = await _db.TripThreadParticipants
                .FirstAsync(p => p.TripThreadId == thread.Id && p.UserId == CurrentUserId);
            participant.LastReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(messages);
        }

        public record SendMessageBody(string Body);

        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage(Guid tripId, SendMessageBody body)
        {
            if (string.IsNullOrWhiteSpace(body.Body)) return BadRequest("Message can't be empty.");

            var thread = await GetThreadIfMemberAsync(tripId);
            if (thread is null) return NotFound();

            var message = new ThreadMessage
            {
                TripThreadId = thread.Id,
                SenderUserId = CurrentUserId,
                Body = body.Body.Trim()
            };

            _db.ThreadMessages.Add(message);
            await _db.SaveChangesAsync();

            var recipientIds = thread.Participants
                .Select(p => p.UserId)
                .Where(id => id != CurrentUserId)
                .ToList();

            await _notifications.NotifyNewThreadMessage(tripId, recipientIds, CurrentUserId);

            // NOTE: also push this over your real-time channel (e.g. SignalR
            // hub group named by thread.Id) so open clients update live
            // instead of relying on polling this endpoint.

            return Ok(message);
        }
    }
}
