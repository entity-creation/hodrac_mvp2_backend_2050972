using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Interfaces;
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
        private readonly IUserRepository _userRepo;

        public TripsController(HodracDbContext db, INotificationService notifications, ICurrentUserService currentUser, IUserRepository userRepo)
        {
            _db = db;
            _notifications = notifications;
            _currentUser = currentUser;
            _userRepo = userRepo;
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

            if (trip is null)
                return NotFound();

            var myInterestStatus = await _db.TripInterests
                .Where(i =>
                    i.TripPostId == trip.Id &&
                    i.RequesterUserId == CurrentUserId &&
                    i.Status != TripInterestStatus.Withdrawn)
                .Select(i => i.Status.ToString())
                .FirstOrDefaultAsync();

            return TripPostDto.FromEntity(trip, myInterestStatus);
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

            var result = await WithDtoIncludes()
    .Where(t => followedIds.Contains(t.AuthorUserId) && t.Status == TripPostStatus.Open)
    .OrderByDescending(t => t.CreatedAt)
    .Take(50)
    .Select(trip => new
    {
        Trip = trip,
        MyInterestStatus = _db.TripInterests
            .Where(i =>
                i.TripPostId == trip.Id &&
                i.RequesterUserId == CurrentUserId &&
                i.Status != TripInterestStatus.Withdrawn)
            .Select(i => i.Status.ToString())
            .FirstOrDefault()
    })
    .ToListAsync();

            return result
                .Select(x => TripPostDto.FromEntity(x.Trip, x.MyInterestStatus))
                .ToList();
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

            var result = await query
                .OrderBy(t => t.StartDate ?? DateOnly.MaxValue)
                .Take(100).Select(trip => new
            {
                Trip = trip,
                MyInterestStatus = _db.TripInterests
                .Where(i => i.TripPostId == trip.Id &&
                i.RequesterUserId == CurrentUserId &&
                i.Status != TripInterestStatus.Withdrawn)
            .Select(i => i.Status.ToString())
            .FirstOrDefault()
            }).ToListAsync();

            //var trips = await query
            //    .OrderBy(t => t.StartDate ?? DateOnly.MaxValue)
            //    .Take(100)
            //    .ToListAsync();

            return result
    .Select(x => TripPostDto.FromEntity(x.Trip, x.MyInterestStatus))
    .ToList();
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

        // All non-withdrawn interests the current user has sent, most
        // recent first, with each trip's flattened DTO attached so the
        // page can render status + "open chat if approved" without a
        // second fetch per card.
        [HttpGet("mine/requests")]
        public async Task<ActionResult<List<MyTripRequestDto>>> GetMyRequests()
        {
            var interests = await _db.TripInterests
                .Where(i => i.RequesterUserId == CurrentUserId && i.Status != TripInterestStatus.Withdrawn)
                .OrderByDescending(i => i.RequestedAt)
                .ToListAsync();

            var tripIds = interests.Select(i => i.TripPostId).Distinct().ToList();
            var trips = await WithDtoIncludes().Where(t => tripIds.Contains(t.Id)).ToListAsync();
            var tripById = trips.ToDictionary(t => t.Id);

            var result = interests
    .Where(i => tripById.ContainsKey(i.TripPostId))
    .Select(i => new MyTripRequestDto(
        i.Id,
        i.Status,
        i.Message,
        i.RequestedAt,
        i.RespondedAt,
        TripPostDto.FromEntity(
            tripById[i.TripPostId],
            i.Status.ToString()
        )))
    .ToList();

            return result;
        }

        // Every trip the current user has posted, with a pending-request
        // count (for the badge) and whether a thread exists yet, so the
        // page can show "Open chat" vs. nothing without a thread lookup
        // per card.
        [HttpGet("mine/posts")]
        public async Task<ActionResult<List<MyTripPostDto>>> GetMyPosts()
        {
            var trips = await WithDtoIncludes()
                .Where(t => t.AuthorUserId == CurrentUserId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var tripIds = trips.Select(t => t.Id).ToList();

            var pendingCounts = await _db.TripInterests
                .Where(i => tripIds.Contains(i.TripPostId) && i.Status == TripInterestStatus.Requested)
                .GroupBy(i => i.TripPostId)
                .Select(g => new { TripPostId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TripPostId, x => x.Count);

            var threadTripIds = (await _db.TripThreads
                .Where(t => tripIds.Contains(t.TripPostId))
                .Select(t => t.TripPostId)
                .ToListAsync())
                .ToHashSet();

            return trips
                .Select(t => new MyTripPostDto(
                    TripPostDto.FromEntity(t),
                    pendingCounts.GetValueOrDefault(t.Id, 0),
                    threadTripIds.Contains(t.Id)))
                .ToList();
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

            var result = new TripInterestDto
            {
                Id = interest.Id,
                TripPostId = interest.TripPostId,
                RequesterUserId = interest.RequesterUserId,
                Message = interest.Message,
                Status = interest.Status.ToString()
            };

            return Ok(result);
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

            var result = new List<TripInterestDto>();

            foreach (var interest in interests)
            {
                var user = await _userRepo.GetByIdAsync(
                    interest.RequesterUserId.ToString()
                );

                result.Add(new TripInterestDto
                {
                    Id = interest.Id,
                    TripPostId = interest.TripPostId,
                    RequesterUserId = interest.RequesterUserId,
                    Message = interest.Message,
                    Status = interest.Status.ToString(),
                    RequesterName = user?.DisplayName
                });
            }

            return Ok(result);
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
