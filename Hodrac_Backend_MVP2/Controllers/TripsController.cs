using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

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
        private readonly ITripsRepository _tripsRepo;
        private readonly IHubContext<TripPostHub> _tripPostHub;

        public TripsController(HodracDbContext db, INotificationService notifications, ICurrentUserService currentUser, IUserRepository userRepo, ITripsRepository tripRepo, IHubContext<TripPostHub> tripPostHub)
        {
            _db = db;
            _notifications = notifications;
            _currentUser = currentUser;
            _userRepo = userRepo;
            _tripsRepo = tripRepo;
            _tripPostHub = tripPostHub;
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

        public const int MaxCaptionLength = 2000;
        public const int MaxFreeTextDestinationLength = 200;
        public const int MaxCoverImageUrlLength = 2048;

        // Shared by create + edit. Null fields are "not provided" and skipped.
        private static string? ValidateTripFields(
            string? caption, DateOnly? startDate, DateOnly? endDate, int? maxGroupSize, string? coverImageUrl)
        {
            if (caption is not null && caption.Trim().Length > MaxCaptionLength)
                return $"Caption can't be longer than {MaxCaptionLength} characters.";

            if (startDate is not null && endDate is not null && startDate > endDate)
                return "Start date must be on or before end date.";

            if (maxGroupSize is not null && maxGroupSize <= 0)
                return "Max group size must be greater than 0.";

            if (!string.IsNullOrWhiteSpace(coverImageUrl)
                && (coverImageUrl.Length > MaxCoverImageUrlLength
                    || !Uri.TryCreate(coverImageUrl.Trim(), UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                return "Cover image must be an absolute http(s) URL.";

            return null;
        }

        [HttpPost]
        public async Task<ActionResult<TripPostDto>> CreateTrip(CreateTripRequest request)
        {
            if (CurrentUserId.Equals(Guid.Empty))
                return BadRequest("User not logged in");

            if (request.DestinationId is not null && request.WishlistId is not null)
                return BadRequest("A trip can reference a destination or a wishlist, not both.");

            var validationError = ValidateTripFields(
                request.Caption, request.StartDate, request.EndDate, request.MaxGroupSize, request.CoverImageUrl);
            if (validationError is not null) return BadRequest(validationError);

            if (request.FreeTextDestination is not null
                && request.FreeTextDestination.Trim().Length > MaxFreeTextDestinationLength)
                return BadRequest($"Destination text can't be longer than {MaxFreeTextDestinationLength} characters.");

            if (request.DestinationId is not null
                && !await _db.Set<Destination>().AnyAsync(d => d.DestinationId == request.DestinationId))
                return BadRequest("Destination not found.");

            // A trip may run on a platform template, or on a wishlist the
            // caller owns or has been shared on - never on someone else's
            // private fork. Unauthorized looks the same as missing so we
            // don't leak which private wishlist ids exist.
            if (request.WishlistId is not null
                && !await _db.Set<Wishlist>().AnyAsync(w =>
                    w.WishlistId == request.WishlistId
                    && (w.IsTemplate
                        || w.OwnerUserId == CurrentUserId
                        || _db.WishlistCollaborators.Any(c => c.WishlistId == w.WishlistId && c.UserId == CurrentUserId))))
                return BadRequest("Wishlist not found.");
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
                CoverImageUrl = string.IsNullOrWhiteSpace(request.CoverImageUrl) ? null : request.CoverImageUrl.Trim(),
                MaxGroupSize = request.MaxGroupSize
            };

            _db.TripPosts.Add(trip);
            await _db.SaveChangesAsync();

            var loaded = await LoadForDtoAsync(trip.Id);
            await _tripPostHub.Clients
                .All
                .SendAsync("TripPostCreated", TripPostDto.FromEntity(loaded!));
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

        // Shared by Browse (signed-in) and BrowsePublic (anonymous). Only
        // Open trips are ever listed.
        //
        // Matches on either a single Destination's name OR any stop inside
        // a Wishlist's itinerary, so a wishlist trip through Kyoto shows up
        // when someone searches "Kyoto" even though the post itself is
        // anchored to the Wishlist, not that Destination directly.
        private static IQueryable<TripPost> ApplyBrowseFilters(
            IQueryable<TripPost> query,
            string? destination,
            Guid? destinationId,
            Guid? wishlistId,
            DateOnly? from,
            DateOnly? to)
        {
            query = query.Where(t => t.Status == TripPostStatus.Open);

            // A destination matches if it's the trip's own destination or any
            // stop on the trip's wishlist.
            if (destinationId is not null)
                query = query.Where(t =>
                    t.DestinationId == destinationId
                    || (t.Wishlist != null && t.Wishlist.WishlistDestinations
                        .Any(wd => wd.DestinationId == destinationId)));

            // Trips usually run on a user's fork, so match forks of the
            // requested wishlist too.
            if (wishlistId is not null)
                query = query.Where(t =>
                    t.WishlistId == wishlistId
                    || (t.Wishlist != null && t.Wishlist.ForkedFromId == wishlistId));

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

            return query;
        }

        // Public browse/search index - not limited to people you follow.
        [HttpGet("browse")]
        public async Task<ActionResult<List<TripPostDto>>> Browse(
            [FromQuery] string? destination,
            [FromQuery] Guid? destinationId,
            [FromQuery] Guid? wishlistId,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to)
        {
            var query = ApplyBrowseFilters(WithDtoIncludes(), destination, destinationId, wishlistId, from, to);

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

            return result
    .Select(x => TripPostDto.FromEntity(x.Trip, x.MyInterestStatus))
    .ToList();
        }

        // Logged-out browse for the landing page. Same filters as Browse,
        // paged (1-based, like ExploreController), rate-limited per IP, and
        // projected to PublicTripPostDto (no author id / per-viewer state).
        [HttpGet("public")]
        [AllowAnonymous]
        [EnableRateLimiting("public-browse")]
        public async Task<ActionResult<PagedResult<PublicTripPostDto>>> BrowsePublic(
            [FromQuery] string? destination,
            [FromQuery] Guid? destinationId,
            [FromQuery] Guid? wishlistId,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var total = await ApplyBrowseFilters(_db.TripPosts, destination, destinationId, wishlistId, from, to)
                .CountAsync();

            var trips = await ApplyBrowseFilters(WithDtoIncludes(), destination, destinationId, wishlistId, from, to)
                .AsNoTracking()
                .OrderBy(t => t.StartDate ?? DateOnly.MaxValue)
                .ThenByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<PublicTripPostDto>(
                Page: page,
                PageSize: pageSize,
                TotalCount: total,
                TotalPages: (int)Math.Ceiling(total / (double)pageSize),
                Items: trips.Select(PublicTripPostDto.FromEntity).ToList()));
        }

        // Partial update - every field is optional; only provided ones change.
        // Status: Open <-> Closed, anything -> Cancelled; Cancelled is terminal
        // (no further edits at all).
        public record UpdateTripRequest(
            string? Caption,
            DateOnly? StartDate,
            DateOnly? EndDate,
            bool? IsDateFlexible,
            string? CoverImageUrl,
            int? MaxGroupSize,
            TripPostStatus? Status);

        [HttpPatch("{id:guid}")]
        public async Task<IActionResult> UpdateTrip(Guid id, [FromBody] UpdateTripRequest request)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await LockTripAsync(id);

            var trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id);
            if (trip is null) return NotFound();
            if (trip.AuthorUserId != CurrentUserId) return Forbid();

            if (trip.Status == TripPostStatus.Cancelled)
                return Conflict("This trip has been cancelled and can no longer be edited.");

            // Validate against the merged (existing + incoming) values so a
            // lone startDate can't be pushed past the stored endDate.
            var newStart = request.StartDate ?? trip.StartDate;
            var newEnd = request.EndDate ?? trip.EndDate;
            var validationError = ValidateTripFields(
                request.Caption, newStart, newEnd, request.MaxGroupSize, request.CoverImageUrl);
            if (validationError is not null) return BadRequest(validationError);

            if (request.MaxGroupSize is not null)
            {
                // Group size counts the author plus every approved member.
                var approvedCount = await _db.TripInterests
                    .CountAsync(i => i.TripPostId == id && i.Status == TripInterestStatus.Approved);
                if (request.MaxGroupSize.Value < approvedCount + 1)
                    return BadRequest($"Max group size can't be below the current group size ({approvedCount + 1}, including you).");
            }

            if (request.Status is not null && request.Status.Value != trip.Status)
            {
                var allowed = request.Status.Value switch
                {
                    TripPostStatus.Cancelled => true,
                    TripPostStatus.Open => trip.Status == TripPostStatus.Closed,
                    TripPostStatus.Closed => trip.Status == TripPostStatus.Open,
                    _ => false
                };
                if (!allowed)
                    return Conflict($"Can't change a trip from {trip.Status} to {request.Status.Value}.");
                trip.Status = request.Status.Value;
            }

            if (request.Caption is not null) trip.Caption = request.Caption.Trim();
            if (request.StartDate is not null) trip.StartDate = request.StartDate;
            if (request.EndDate is not null) trip.EndDate = request.EndDate;
            if (request.IsDateFlexible is not null) trip.IsDateFlexible = request.IsDateFlexible.Value;
            if (request.CoverImageUrl is not null)
                trip.CoverImageUrl = string.IsNullOrWhiteSpace(request.CoverImageUrl) ? null : request.CoverImageUrl.Trim();
            if (request.MaxGroupSize is not null) trip.MaxGroupSize = request.MaxGroupSize;
            trip.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
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
            if (CurrentUserId.Equals(Guid.Empty)) return BadRequest("User not logged in");

            TripPost? trip;
            TripInterest interest;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id);
                if (trip is null || trip.Status != TripPostStatus.Open) return NotFound();
                if (trip.AuthorUserId == CurrentUserId) return BadRequest("You can't join your own trip.");

                var existing = await _db.TripInterests
                    .FirstOrDefaultAsync(i => i.TripPostId == id && i.RequesterUserId == CurrentUserId);

                if (existing is not null)
                {
                    // Only a withdrawn request may be re-sent. Declined stays
                    // declined; Requested/Approved are already in flight.
                    if (existing.Status == TripInterestStatus.Declined)
                        return Conflict("Your request to join this trip was declined.");
                    if (existing.Status != TripInterestStatus.Withdrawn)
                        return Conflict("You've already requested to join this trip.");

                    existing.Status = TripInterestStatus.Requested;
                    existing.Message = body.Message;
                    existing.RequestedAt = DateTime.UtcNow;
                    existing.RespondedAt = null;
                    interest = existing;
                }
                else
                {
                    interest = new TripInterest
                    {
                        TripPostId = id,
                        RequesterUserId = CurrentUserId,
                        Message = body.Message
                    };
                    _db.TripInterests.Add(interest);
                }

                try
                {
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // A concurrent request for the same (trip, user) won.
                    _db.ChangeTracker.Clear();
                    return Conflict("You've already requested to join this trip.");
                }
            }

            await _notifications.NotifyInterestReceived(trip, CurrentUserId);

            return Ok(await PushInterestChangedAsync(trip, interest));
        }

        // Poster-only: see everyone who's asked to join.
        [HttpGet("{id:guid}/interests")]
        public async Task<ActionResult<List<TripInterestDto>>> GetInterests(Guid id)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is null) return NotFound();
            if (trip.AuthorUserId != CurrentUserId) return Forbid();

            return Ok(await _tripsRepo.GetAllInterests(id));
        }

        public record RespondToInterestBody(bool Approve);

        // Approval rules:
        //  - the trip must be Open (Closed/Cancelled trips can still decline);
        //  - if MaxGroupSize is set it counts the author + approved members,
        //    so approval is refused once approvedCount + 1 >= MaxGroupSize.
        // The interest status change and thread/participant creation are
        // saved in one SaveChanges inside a transaction that holds a row
        // lock on the trip, so concurrent approvals serialize and an interest
        // can never end up Approved without a thread.
        [HttpPatch("{id:guid}/interests/{interestId:guid}")]
        public async Task<IActionResult> RespondToInterest(Guid id, Guid interestId, RespondToInterestBody body)
        {
            TripPost? trip;
            TripInterest? interest;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id);
                if (trip is null) return NotFound();
                if (trip.AuthorUserId != CurrentUserId) return Forbid();

                interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
                if (interest is null || interest.TripPostId != id) return NotFound();
                if (interest.Status != TripInterestStatus.Requested) return Conflict("Already responded to.");

                if (body.Approve)
                {
                    if (trip.Status != TripPostStatus.Open)
                        return Conflict($"This trip is {trip.Status.ToString().ToLowerInvariant()}; requests can't be approved.");

                    if (trip.MaxGroupSize is not null)
                    {
                        var approvedCount = await _db.TripInterests
                            .CountAsync(i => i.TripPostId == id && i.Status == TripInterestStatus.Approved);
                        if (approvedCount + 1 >= trip.MaxGroupSize.Value)
                            return Conflict("This trip's group is already full.");
                    }
                }

                interest.Status = body.Approve ? TripInterestStatus.Approved : TripInterestStatus.Declined;
                interest.RespondedAt = DateTime.UtcNow;

                if (body.Approve)
                    await StageAddToThreadAsync(trip, interest.RequesterUserId);

                try
                {
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Another approval created the thread/participant at the
                    // same moment. Nothing was committed; the caller can retry.
                    _db.ChangeTracker.Clear();
                    return Conflict("This trip was updated at the same time - please retry.");
                }
            }

            if (body.Approve)
                await _notifications.NotifyInterestApproved(trip, interest.RequesterUserId);
            else
                await _notifications.NotifyInterestDeclined(trip, interest.RequesterUserId);

            await PushInterestChangedAsync(trip, interest);
            return NoContent();
        }

        // Only a pending (Requested) or Approved interest can be withdrawn.
        // Withdrawing an approval also removes the requester from the trip
        // chat (never the author).
        [HttpDelete("{id:guid}/interests/{interestId:guid}")]
        public async Task<IActionResult> WithdrawInterest(Guid id, Guid interestId)
        {
            TripInterest? interest;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
                if (interest is null || interest.TripPostId != id) return NotFound();
                if (interest.RequesterUserId != CurrentUserId) return Forbid();

                if (interest.Status is not (TripInterestStatus.Requested or TripInterestStatus.Approved))
                    return Conflict($"A {interest.Status.ToString().ToLowerInvariant()} request can't be withdrawn.");

                if (interest.Status == TripInterestStatus.Approved)
                {
                    var authorId = await _db.TripPosts
                        .Where(t => t.Id == id)
                        .Select(t => t.AuthorUserId)
                        .FirstOrDefaultAsync();

                    if (interest.RequesterUserId != authorId)
                    {
                        var participants = await _db.TripThreadParticipants
                            .Where(p => p.UserId == interest.RequesterUserId
                                && _db.TripThreads.Any(t => t.Id == p.TripThreadId && t.TripPostId == id))
                            .ToListAsync();
                        _db.TripThreadParticipants.RemoveRange(participants);
                    }
                }

                interest.Status = TripInterestStatus.Withdrawn;
                interest.RespondedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is not null) await PushInterestChangedAsync(trip, interest);

            return NoContent();
        }

        // Live-updates both sides of an interest: the requester sees their
        // new status, the poster gets the refreshed interest list.
        private async Task<TripInterestDto> PushInterestChangedAsync(TripPost trip, TripInterest interest)
        {
            var dto = new TripInterestDto
            {
                Id = interest.Id,
                TripPostId = interest.TripPostId,
                RequesterUserId = interest.RequesterUserId,
                Message = interest.Message,
                Status = interest.Status.ToString()
            };

            await _tripPostHub.Clients
                .Group($"user:{interest.RequesterUserId}")
                .SendAsync("TripInterestStatusChanged", dto);

            var interestList = await _tripsRepo.GetAllInterests(trip.Id);

            await _tripPostHub.Clients
                .Group($"user:{trip.AuthorUserId}")
                .SendAsync("TripInterestUpdated", interestList);

            return dto;
        }

        // Stages (does not save) the thread - created lazily on first
        // approval with the author as a participant - and the user's
        // participant row. Caller saves inside its transaction.
        private async Task StageAddToThreadAsync(TripPost trip, Guid userId)
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

                if (userId == trip.AuthorUserId) return;
            }
            else if (await _db.TripThreadParticipants
                .AnyAsync(p => p.TripThreadId == thread.Id && p.UserId == userId))
            {
                return;
            }

            _db.TripThreadParticipants.Add(new TripThreadParticipant
            {
                TripThreadId = thread.Id,
                UserId = userId
            });
        }

        // Row-locks the trip for the rest of the current transaction so
        // interest/approval/edit operations on the same trip serialize
        // (prevents double thread creation and group-size overshoot).
        private Task LockTripAsync(Guid tripId) =>
            _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR UPDATE");

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
