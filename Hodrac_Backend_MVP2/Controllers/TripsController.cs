using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/trips")]
    public class TripsController : ControllerBase
    {
        private readonly HodracDbContext _db;
        private readonly INotificationService _notifications;
        private readonly ICurrentUserService _currentUser;

        public TripsController(HodracDbContext db, INotificationService notifications, ICurrentUserService currentUser)
        {
            _db = db;
            _notifications = notifications;
            _currentUser = currentUser;
        }

        private Guid CurrentUserId => _currentUser.UserId;

        // ---- Create ----

        // DestinationId and WishlistId are both optional — but not both at
        // once. If neither is set, freeTextDestination (or just the
        // caption) carries the "where" for display and basic text search.
        public record CreateTripRequest(
            Guid? DestinationId,
            Guid? WishlistId,
            string? FreeTextDestination,
            DateOnly? StartDate,
            DateOnly? EndDate,
            bool IsDateFlexible,
            string Caption,
            string? CoverImageUrl,
            int? MaxGroupSize);

        [HttpPost]
        public async Task<ActionResult<TripPostDto>> CreateTrip(CreateTripRequest request)
        {
            if (request.DestinationId is not null && request.WishlistId is not null)
                return BadRequest("A trip can reference a destination or a wishlist, not both.");

            if (request.DestinationId is not null
                && !await _db.Set<Destination>().AnyAsync(d => d.DestinationId == request.DestinationId))
                return BadRequest("Destination not found.");

            if (request.WishlistId is not null
                && !await _db.Set<Wishlist>().AnyAsync(w => w.WishlistId == request.WishlistId))
                return BadRequest("Wishlist not found.");
            if (CurrentUserId.Equals(Guid.Empty))
                return BadRequest("User not logged in");
            var trip = new TripPost
            {
                AuthorUserId = CurrentUserId,
                DestinationId = request.DestinationId,
                WishlistId = request.WishlistId,
                FreeTextDestination = request.FreeTextDestination?.Trim(),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsDateFlexible = request.IsDateFlexible,
                Caption = request.Caption?.Trim() ?? string.Empty,
                CoverImageUrl = request.CoverImageUrl,
                MaxGroupSize = request.MaxGroupSize
            };

            _db.TripPosts.Add(trip);
            await _db.SaveChangesAsync();

            var loaded = await LoadForDtoAsync(trip.Id);
            return CreatedAtAction(nameof(GetTrip), new { id = trip.Id }, TripPostDto.FromEntity(loaded!));
        }

        // ---- Feed / browse / detail ----

        private IQueryable<TripPost> WithDtoIncludes() => _db.TripPosts
            .Include(t => t.Destination!).ThenInclude(d => d.Country)
            .Include(t => t.Destination!).ThenInclude(d => d.Images)
            .Include(t => t.Wishlist!).ThenInclude(w => w.WishlistDestinations)
                .ThenInclude(wd => wd.Destination).ThenInclude(d => d.Images);

        private Task<TripPost?> LoadForDtoAsync(Guid id) =>
            WithDtoIncludes().FirstOrDefaultAsync(t => t.Id == id);

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TripPostDto>> GetTrip(Guid id)
        {
            var trip = await LoadForDtoAsync(id);
            return trip is null ? NotFound() : TripPostDto.FromEntity(trip);
        }

        // Trips from people the current user follows. Assumes a Follows
        // table/service already exists elsewhere in Hodrac — wire that in
        // where marked below.
        [HttpGet("feed")]
        public async Task<ActionResult<List<TripPostDto>>> GetFeed()
        {
            // var followedIds = await _follows.GetFollowedUserIdsAsync(CurrentUserId);
            var followedIds = new List<Guid>(); // TODO: wire up real follow graph

            var trips = await WithDtoIncludes()
                .Where(t => followedIds.Contains(t.AuthorUserId) && t.Status == TripPostStatus.Open)
                .OrderByDescending(t => t.CreatedAt)
                .Take(50)
                .ToListAsync();

            return trips.Select(TripPostDto.FromEntity).ToList();
        }

        // Public browse/search index - not limited to people you follow.
        // Matches on either a single Destination's name OR any stop inside
        // a Wishlist's itinerary, so a wishlist trip through Kyoto shows up
        // when someone searches "Kyoto" even though the post itself is
        // anchored to the Wishlist, not that Destination directly.
        [HttpGet("browse")]
        public async Task<ActionResult<List<TripPostDto>>> Browse(
            [FromQuery] string? destination,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to)
        {
            var query = WithDtoIncludes().Where(t => t.Status == TripPostStatus.Open);

            if (!string.IsNullOrWhiteSpace(destination))
            {
                var term = $"%{destination.Trim()}%";
                query = query.Where(t =>
                    (t.Destination != null && EF.Functions.ILike(t.Destination.DestinationName, term))
                    || (t.Wishlist != null && t.Wishlist.WishlistDestinations
                        .Any(wd => EF.Functions.ILike(wd.Destination.DestinationName, term)))
                    || (t.FreeTextDestination != null && EF.Functions.ILike(t.FreeTextDestination, term)));
            }

            if (from is not null)
                query = query.Where(t => t.EndDate == null || t.EndDate >= from);

            if (to is not null)
                query = query.Where(t => t.StartDate == null || t.StartDate <= to);

            var trips = await query
                .OrderBy(t => t.StartDate ?? DateOnly.MaxValue)
                .Take(100)
                .ToListAsync();

            return trips.Select(TripPostDto.FromEntity).ToList();
        }

        [HttpPatch("{id:guid}")]
        public async Task<IActionResult> UpdateTrip(Guid id, [FromBody] TripPostStatus? status)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is null) return NotFound();
            if (trip.AuthorUserId != CurrentUserId) return Forbid();

            if (status is not null) trip.Status = status.Value;
            trip.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---- Interests (the "Interested" button + approve/decline) ----

        public record RequestInterestBody(string? Message);

        [HttpPost("{id:guid}/interests")]
        public async Task<ActionResult<TripInterest>> RequestToJoin(Guid id, RequestInterestBody body)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is null || trip.Status != TripPostStatus.Open) return NotFound();
            if (trip.AuthorUserId == CurrentUserId) return BadRequest("You can't join your own trip.");
            if (CurrentUserId.Equals(Guid.Empty)) return BadRequest("User not logged in");
            var existing = await _db.TripInterests
                .FirstOrDefaultAsync(i => i.TripPostId == id && i.RequesterUserId == CurrentUserId);

            if (existing is not null)
            {
                if (existing.Status == TripInterestStatus.Withdrawn)
                {
                    existing.Status = TripInterestStatus.Requested;
                    existing.Message = body.Message;
                    existing.RequestedAt = DateTime.UtcNow;
                    existing.RespondedAt = null;
                    await _db.SaveChangesAsync();
                    await _notifications.NotifyInterestReceived(trip, CurrentUserId);
                    return Ok(existing);
                }
                return Conflict("You've already requested to join this trip.");
            }

            var interest = new TripInterest
            {
                TripPostId = id,
                RequesterUserId = CurrentUserId,
                Message = body.Message
            };

            _db.TripInterests.Add(interest);
            await _db.SaveChangesAsync();

            await _notifications.NotifyInterestReceived(trip, CurrentUserId);

            return Ok(interest);
        }

        // Poster-only: see everyone who's asked to join.
        [HttpGet("{id:guid}/interests")]
        public async Task<ActionResult<List<TripInterest>>> GetInterests(Guid id)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is null) return NotFound();
            if (trip.AuthorUserId != CurrentUserId) return Forbid();

            var interests = await _db.TripInterests
                .Where(i => i.TripPostId == id && i.Status != TripInterestStatus.Withdrawn)
                .OrderBy(i => i.RequestedAt)
                .ToListAsync();

            return interests;
        }

        public record RespondToInterestBody(bool Approve);

        [HttpPatch("{id:guid}/interests/{interestId:guid}")]
        public async Task<IActionResult> RespondToInterest(Guid id, Guid interestId, RespondToInterestBody body)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is null) return NotFound();
            if (trip.AuthorUserId != CurrentUserId) return Forbid();

            var interest = await _db.TripInterests.FindAsync(interestId);
            if (interest is null || interest.TripPostId != id) return NotFound();
            if (interest.Status != TripInterestStatus.Requested) return Conflict("Already responded to.");

            interest.Status = body.Approve ? TripInterestStatus.Approved : TripInterestStatus.Declined;
            interest.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (body.Approve)
            {
                await AddToThreadAsync(trip, interest.RequesterUserId);
                await _notifications.NotifyInterestApproved(trip, interest.RequesterUserId);
            }
            else
            {
                await _notifications.NotifyInterestDeclined(trip, interest.RequesterUserId);
            }

            return NoContent();
        }

        [HttpDelete("{id:guid}/interests/{interestId:guid}")]
        public async Task<IActionResult> WithdrawInterest(Guid id, Guid interestId)
        {
            var interest = await _db.TripInterests.FindAsync(interestId);
            if (interest is null || interest.TripPostId != id) return NotFound();
            if (interest.RequesterUserId != CurrentUserId) return Forbid();

            interest.Status = TripInterestStatus.Withdrawn;
            interest.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private async Task AddToThreadAsync(TripPost trip, Guid userId)
        {
            var thread = await _db.TripThreads.FirstOrDefaultAsync(t => t.TripPostId == trip.Id);
            if (thread is null)
            {
                thread = new TripThread { TripPostId = trip.Id };
                _db.TripThreads.Add(thread);

                // Poster is always a participant of their own trip thread.
                _db.TripThreadParticipants.Add(new TripThreadParticipant
                {
                    TripThreadId = thread.Id,
                    UserId = trip.AuthorUserId
                });
            }

            var alreadyIn = await _db.TripThreadParticipants
                .AnyAsync(p => p.TripThreadId == thread.Id && p.UserId == userId);

            if (!alreadyIn)
            {
                _db.TripThreadParticipants.Add(new TripThreadParticipant
                {
                    TripThreadId = thread.Id,
                    UserId = userId
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
