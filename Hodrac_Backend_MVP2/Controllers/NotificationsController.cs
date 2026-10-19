using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.NotificationDtos;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly HodracDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public NotificationsController(HodracDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private Guid CurrentUserId => _currentUser.UserId;

        [HttpGet]
        public async Task<ActionResult<List<NotificationDto>>> GetNotifications(
            [FromQuery] bool unreadOnly = false,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Notifications.Where(n => n.RecipientUserId == CurrentUserId);
            if (unreadOnly) query = query.Where(n => !n.IsRead);

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip(Math.Max(page, 0) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(notifications.Select(NotificationDto.FromEntity).ToList());
        }

        [HttpGet("unread-count")]
        public async Task<ActionResult<int>> GetUnreadCount()
        {
            var count = await _db.Notifications
                .CountAsync(n => n.RecipientUserId == CurrentUserId && !n.IsRead);

            return Ok(count);
        }

        [HttpPatch("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == CurrentUserId);
            if (notification is null) return NotFound();

            notification.IsRead = true;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            await _db.Notifications
                .Where(n => n.RecipientUserId == CurrentUserId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

            return NoContent();
        }
    }
}
