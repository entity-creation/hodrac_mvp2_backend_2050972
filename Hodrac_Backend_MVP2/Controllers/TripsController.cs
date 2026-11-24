using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Images;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static Hodrac_Backend_MVP2.Services.TripCreationService;

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
        private readonly FeeCalculator _fees;
        private readonly HodracHostOptions _hostOptions;
        private readonly ITripPageViewCounter _pageViews;
        private readonly ITripThreadService _threads;
        private readonly TripCreationService _tripCreation;
        private readonly ImageUrlNormalizer _images;

        public TripsController(HodracDbContext db, INotificationService notifications, ICurrentUserService currentUser, IUserRepository userRepo, ITripsRepository tripRepo, IHubContext<TripPostHub> tripPostHub,
            FeeCalculator fees, IOptions<HodracHostOptions> hostOptions, ITripPageViewCounter pageViews, ITripThreadService threads,
            TripCreationService tripCreation, ImageUrlNormalizer images)
        {
            _images = images;
            _db = db;
            _notifications = notifications;
            _currentUser = currentUser;
            _userRepo = userRepo;
            _tripsRepo = tripRepo;
            _tripPostHub = tripPostHub;
            _fees = fees;
            _hostOptions = hostOptions.Value;
            _pageViews = pageViews;
            _threads = threads;
            _tripCreation = tripCreation;
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
            int? MaxGroupSize,
            // Hodrac Host. All optional: without IsHostedTrip = true this is a
            // community trip, and sending any hosted field is a 400. Money in kobo.
            bool? IsHostedTrip = null,
            bool? IsListed = null,
            long? PricePerSeatKobo = null,
            long? DepositKobo = null,
            List<TripInstalmentRequest>? Instalments = null,
            FeeMode? FeeMode = null,
            int? SeatHoldHours = null,
            string? TermsText = null,
            string? Inclusions = null,
            string? Exclusions = null,
            // Who picks the instalment dates: "HostSet" (default; every
            // instalment has a dueDate) or "TravellerChooses" (instalments
            // have no dueDate; payInFullByDate is required).
            InstalmentDatesMode? InstalmentDatesMode = null,
            DateOnly? PayInFullByDate = null);

        // DueDate: required when the trip is HostSet, omitted (null) when TravellerChooses.
        public record TripInstalmentRequest(int Sequence, long AmountKobo, DateOnly? DueDate);

        public const int MaxCaptionLength = 2000;
        public const int MaxFreeTextDestinationLength = 200;
        public const int MaxCoverImageUrlLength = 2048;

        // ---- Hodrac Host rules ----
        public const int MaxTermsTextLength = 20000;
        public const int MaxInclusionsLength = 4000;
        public const int MinSeatHoldHours = 1;
        public const int MaxSeatHoldHours = 336; // 14 days
        public const string HostedFieldsOnCommunityMessage = "Price and payments are only for hosted trips";
        public const string CommunityUnlistedMessage = "Community trips always appear in the public feed; only hosted trips can be private.";
        public const string PaymentTermsLockedMessage = "Price, deposit, instalments, fee mode and who sets the instalment dates can't change once a traveller has paid.";

        // The shared create/edit rules (HostedFieldsInput, field validation,
        // the host lock and the founding claim) live in TripCreationService,
        // which the admin create-for-host path uses too.

        // A payment that went through at some point (later refunds/disputes
        // included) locks the money terms and the trip type.
        private static readonly SeatPaymentStatus[] SucceededPaymentStatuses =
        {
            SeatPaymentStatus.Success,
            SeatPaymentStatus.Refunded,
            SeatPaymentStatus.PartiallyRefunded,
            SeatPaymentStatus.Disputed
        };

        // Price / deposit / instalments (InstalmentValidator) as a 400
        // validation problem keyed by request field, or null when valid.
        private ActionResult? ValidateSchedule(
            long? price, long? deposit, IReadOnlyCollection<TripInstalment> instalments, DateOnly? startDate,
            InstalmentDatesMode mode, DateOnly? payInFullByDate, DateOnly? today)
        {
            var errors = InstalmentValidator.Validate(price, deposit, instalments, startDate, mode, payInFullByDate, today);
            if (errors.Count == 0) return null;
            foreach (var (key, message) in errors) ModelState.AddModelError(key, message);
            return ValidationProblem(ModelState);
        }

        // Row-locks (inside the caller's transaction) and loads the caller's
        // host profile, or says what's missing before a hosted trip can exist.
        private Task<(HostProfile? Host, string? Error, bool Closed)> LoadHostForHostedTripAsync() =>
            _tripCreation.LoadHostForUserAsync(CurrentUserId);

        // The first hosted trip of a founding host (up to the founding
        // deadline) is their free departure. Caller holds the host row lock.
        private void ClaimFoundingFreeDeparture(TripPost trip, HostProfile host) =>
            _tripCreation.ClaimFoundingFreeDeparture(trip, host);

        private Task<bool> AnySuccessfulPaymentAsync(Guid tripId) =>
            _db.SeatPayments.AnyAsync(p => p.TripPostId == tripId && SucceededPaymentStatuses.Contains(p.Status));

        // Seats taken: approved, non-expired members excluding the author.
        // Expired seat holds (hosted trips) don't count; community trips are
        // always NotRequired, so for them this is today's approved count.
        private Task<int> CountSeatsTakenAsync(TripPost trip) =>
            _db.TripInterests.CountAsync(i =>
                i.TripPostId == trip.Id &&
                i.Status == TripInterestStatus.Approved &&
                i.PaymentStatus != InterestPaymentStatus.Expired &&
                i.RequesterUserId != trip.AuthorUserId);

        [HttpPost]
        public async Task<ActionResult<TripPostDto>> CreateTrip(CreateTripRequest request)
        {
            if (CurrentUserId.Equals(Guid.Empty))
                return BadRequest("User not logged in");

            var result = await _tripCreation.CreateAsync(CurrentUserId, request);
            switch (result.Outcome)
            {
                case TripCreateOutcome.Created:
                    return CreatedAtAction(nameof(GetTrip), new { id = result.Trip!.Id }, result.Dto);
                case TripCreateOutcome.Invalid:
                    foreach (var (key, message) in result.FieldErrors!) ModelState.AddModelError(key, message);
                    return ValidationProblem(ModelState);
                case TripCreateOutcome.Forbidden:
                    return HostClosure.HostForbidden();
                default:
                    return BadRequest(result.Error);
            }
        }

        // ---- Feed / browse / detail ----

        private IQueryable<TripPost> WithDtoIncludes() => TripCreationService.WithDtoIncludes(_db.TripPosts);

        // A trip plus, in the same query, the caller's own non-withdrawn
        // interest (status + payment state for the my* DTO fields) and the
        // approved, non-expired member count (author excluded).
        private sealed class TripWithViewer
        {
            public TripPost Trip { get; init; } = null!;
            public TripViewerInterest? Mine { get; init; }
            public int ApprovedCount { get; init; }

            public TripPostDto ToDto(FeeCalculator fees) =>
                TripPostDto.FromEntity(Trip, Mine?.Status.ToString(), ApprovedCount, fees, Mine);
        }

        private IQueryable<TripWithViewer> ProjectForViewer(IQueryable<TripPost> trips)
        {
            var userId = CurrentUserId;
            return trips.Select(t => new TripWithViewer
            {
                Trip = t,
                Mine = _db.TripInterests
                    .Where(i =>
                        i.TripPostId == t.Id &&
                        i.RequesterUserId == userId &&
                        i.Status != TripInterestStatus.Withdrawn)
                    .Select(i => new TripViewerInterest
                    {
                        Id = i.Id,
                        Status = i.Status,
                        PaymentStatus = i.PaymentStatus,
                        SeatHoldExpiresAt = i.SeatHoldExpiresAt,
                        AmountPaidKobo = i.AmountPaidKobo,
                        TermsVersionAccepted = i.TermsVersionAccepted,
                        InstalmentDates = _db.TripInterestInstalmentDates
                            .Where(d => d.TripInterestId == i.Id)
                            .Select(d => new TripViewerInstalmentDate(d.Sequence, d.DueDate))
                            .ToList()
                    })
                    .FirstOrDefault(),
                ApprovedCount = _db.TripInterests
                    .Count(i =>
                        i.TripPostId == t.Id &&
                        i.Status == TripInterestStatus.Approved &&
                        i.PaymentStatus != InterestPaymentStatus.Expired &&
                        i.RequesterUserId != t.AuthorUserId)
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TripPostDto>> GetTrip(Guid id)
        {
            // Trip, the viewer's own interest and the approved-member count in one query.
            var row = await ProjectForViewer(WithDtoIncludes().Where(t => t.Id == id)).FirstOrDefaultAsync();

            // Moderator-hidden trips are invisible to everyone but the author.
            if (row is null || (row.Trip.IsHidden && row.Trip.AuthorUserId != CurrentUserId))
                return NotFound();

            return row.ToDto(_fees);
        }

        // Trips from people the current user follows. Assumes a Follows
        // table/service already exists elsewhere in Hodrac — wire that in
        // where marked below.
        [HttpGet("feed")]
        public async Task<ActionResult<List<TripPostDto>>> GetFeed()
        {
            // var followedIds = await _follows.GetFollowedUserIdsAsync(CurrentUserId);
            var followedIds = new List<Guid>(); // TODO: wire up real follow graph

            var result = await ProjectForViewer(WithDtoIncludes()
                    .Where(t => followedIds.Contains(t.AuthorUserId) && t.Status == TripPostStatus.Open && !t.IsHidden && t.IsListed)
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(50))
                .ToListAsync();

            return result.Select(x => x.ToDto(_fees)).ToList();
        }

        // Shared by Browse (signed-in) and BrowsePublic (anonymous). Only
        // Open, non-hidden (moderation), listed trips are ever listed
        // (private hosted trips are link-only).
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
            query = query.Where(t => t.Status == TripPostStatus.Open && !t.IsHidden && t.IsListed);

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

            var result = await ProjectForViewer(query
                    .OrderBy(t => t.StartDate ?? DateOnly.MaxValue)
                    .Take(100))
                .ToListAsync();

            return result.Select(x => x.ToDto(_fees)).ToList();
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

            // Seats left is shown for hosted trips only.
            var hostedIds = trips.Where(t => t.IsHostedTrip).Select(t => t.Id).ToList();
            var seatsTaken = hostedIds.Count == 0
                ? new Dictionary<Guid, int>()
                : await _db.TripInterests
                    .Where(i => hostedIds.Contains(i.TripPostId)
                        && i.Status == TripInterestStatus.Approved
                        && i.PaymentStatus != InterestPaymentStatus.Expired
                        && i.RequesterUserId != i.TripPost!.AuthorUserId)
                    .GroupBy(i => i.TripPostId)
                    .Select(g => new { TripPostId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.TripPostId, x => x.Count);

            return Ok(new PagedResult<PublicTripPostDto>(
                Page: page,
                PageSize: pageSize,
                TotalCount: total,
                TotalPages: (int)Math.Ceiling(total / (double)pageSize),
                Items: trips.Select(t => PublicTripPostDto.FromEntity(t, seatsTaken.GetValueOrDefault(t.Id), _fees)).ToList()));
        }

        // Logged-out single-trip view for shared links. Same projection as
        // the public list (no author id, no participant counts; hosted trips
        // show seats left). Hidden (moderated) trips 404; Closed/Cancelled
        // trips still resolve so the link shows the trip's status instead of
        // breaking. Unlisted (private hosted) trips resolve too: link-only.
        // Each human view is counted for the host's weekly report.
        [HttpGet("public/{id:guid}")]
        [AllowAnonymous]
        [EnableRateLimiting("public-browse")]
        public async Task<ActionResult<PublicTripPostDto>> GetPublicTrip(Guid id)
        {
            var trip = await WithDtoIncludes()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsHidden);

            if (trip is null) return NotFound();

            var seatsTaken = trip.IsHostedTrip ? await CountSeatsTakenAsync(trip) : 0;
            await _pageViews.RecordAsync(trip.Id, TripPageViewSources.PublicApi, Request);
            return PublicTripPostDto.FromEntity(trip, seatsTaken, _fees);
        }

        // Partial update - every field is optional; only provided ones change.
        // Status: Open <-> Closed, anything -> Cancelled; Cancelled is terminal
        // (no further edits at all).
        //
        // Hodrac Host:
        //  - IsHostedTrip switches the type. Community -> hosted needs a host
        //    profile with a payout account and no Approved interests yet (409);
        //    the full payment setup must be sent with it. Hosted -> community
        //    needs no succeeded (or in-flight) payment (409) and clears every
        //    hosted field.
        //  - Community trips: hosted fields -> 400, isListed false -> 400.
        //  - Once a payment has succeeded, price/deposit/instalments/feeMode
        //    are locked (409). Instalments, when sent, replace the schedule.
        //  - Any change to TermsText bumps TermsVersion.
        //  - Inclusions/Exclusions: "" clears.
        public record UpdateTripRequest(
            string? Caption,
            DateOnly? StartDate,
            DateOnly? EndDate,
            bool? IsDateFlexible,
            string? CoverImageUrl,
            int? MaxGroupSize,
            TripPostStatus? Status,
            bool? IsHostedTrip = null,
            bool? IsListed = null,
            long? PricePerSeatKobo = null,
            long? DepositKobo = null,
            List<TripInstalmentRequest>? Instalments = null,
            FeeMode? FeeMode = null,
            int? SeatHoldHours = null,
            string? TermsText = null,
            string? Inclusions = null,
            string? Exclusions = null,
            InstalmentDatesMode? InstalmentDatesMode = null,
            DateOnly? PayInFullByDate = null);

        [HttpPatch("{id:guid}")]
        public async Task<IActionResult> UpdateTrip(Guid id, [FromBody] UpdateTripRequest request)
        {
            // Old image hosts are swapped for the primary one before validating and storing.
            request = request with { CoverImageUrl = _images.Normalize(request.CoverImageUrl) };
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
                // Group size counts the author plus every approved member
                // (expired hosted seat holds don't hold a place).
                var approvedCount = await _db.TripInterests
                    .CountAsync(i => i.TripPostId == id
                        && i.Status == TripInterestStatus.Approved
                        && i.PaymentStatus != InterestPaymentStatus.Expired);
                if (request.MaxGroupSize.Value < approvedCount + 1)
                    return BadRequest($"Max group size can't be below the current group size ({approvedCount + 1}, including you).");
            }

            // ---- Hodrac Host: type, visibility and payment setup (validate all first) ----
            var hostedFields = new HostedFieldsInput(
                request.PricePerSeatKobo, request.DepositKobo, request.Instalments, request.FeeMode,
                request.SeatHoldHours, request.TermsText, request.Inclusions, request.Exclusions,
                request.InstalmentDatesMode, request.PayInFullByDate);
            var becomingHosted = request.IsHostedTrip == true && !trip.IsHostedTrip;
            var becomingCommunity = request.IsHostedTrip == false && trip.IsHostedTrip;
            var willBeHosted = becomingHosted || (trip.IsHostedTrip && !becomingCommunity);
            var newInstalments = request.Instalments is null ? null : ToInstalments(request.Instalments);
            HostProfile? newHost = null;

            // Instalment dates mode. Switching to TravellerChooses without
            // sending a schedule drops the host's dates from the stored one;
            // HostSet never keeps a pay-in-full date.
            var currentMode = becomingHosted ? InstalmentDatesMode.HostSet : trip.InstalmentDatesMode;
            var newMode = request.InstalmentDatesMode ?? currentMode;
            var modeChanging = willBeHosted && newMode != trip.InstalmentDatesMode;
            if (willBeHosted && newInstalments is null && newMode == InstalmentDatesMode.TravellerChooses
                && trip.Instalments.Any(i => i.DueDate is not null))
                newInstalments = trip.Instalments
                    .Select(i => new TripInstalment { Sequence = i.Sequence, AmountKobo = i.AmountKobo, DueDate = null })
                    .ToList();
            var newPayInFullBy = newMode == InstalmentDatesMode.HostSet
                ? request.PayInFullByDate   // only non-null when sent: a 400 below
                : request.PayInFullByDate ?? trip.PayInFullByDate;
            // "After today" only when the deadline is being set or changed, so
            // unrelated edits still work once it has passed.
            var checkDeadlineFuture = (request.PayInFullByDate is not null && request.PayInFullByDate != trip.PayInFullByDate)
                || modeChanging;

            if (!willBeHosted)
            {
                if (hostedFields.AnySent) return BadRequest(HostedFieldsOnCommunityMessage);
                if (request.IsListed == false) return BadRequest(CommunityUnlistedMessage);
            }

            if (becomingCommunity)
            {
                var paymentStatuses = await _db.SeatPayments
                    .Where(p => p.TripPostId == id)
                    .Select(p => p.Status)
                    .ToListAsync();
                if (paymentStatuses.Any(s => SucceededPaymentStatuses.Contains(s)))
                    return Conflict("A traveller has already paid for this trip, so it can't become a community trip.");
                if (paymentStatuses.Contains(SeatPaymentStatus.Initialized))
                    return Conflict("A payment for this trip is still in progress, so it can't become a community trip yet.");
            }

            if (willBeHosted)
            {
                if (becomingHosted)
                {
                    var (loadedHost, hostError, hostClosed) = await LoadHostForHostedTripAsync();
                    if (hostClosed) return HostClosure.HostForbidden();
                    if (hostError is not null) return BadRequest(hostError);
                    if (await _db.TripInterests.AnyAsync(i => i.TripPostId == id && i.Status == TripInterestStatus.Approved))
                        return Conflict("Someone has already been approved to join this trip, so it can't become a hosted trip.");
                    newHost = loadedHost!;
                }
                else if (PaymentTermsChanged(trip, request.PricePerSeatKobo, request.DepositKobo, request.FeeMode, newInstalments,
                             request.InstalmentDatesMode, request.PayInFullByDate))
                {
                    // A host closed by Hodrac can't change money terms (HostClosure).
                    if (trip.HostProfileId is Guid termsHostId
                        && await _db.HostProfiles.AnyAsync(h => h.Id == termsHostId && h.ClosedAt != null))
                        return HostClosure.HostForbidden();
                    if (await AnySuccessfulPaymentAsync(id))
                        return Conflict(PaymentTermsLockedMessage);
                }

                if ((request.MaxGroupSize ?? trip.MaxGroupSize) is null)
                    return BadRequest("Hosted trips need a max group size.");
                if (string.IsNullOrWhiteSpace(request.TermsText ?? trip.TermsText))
                    return BadRequest("Hosted trips need written terms (refunds and dropouts).");
                var textError = ValidateHostedText(request.SeatHoldHours, request.TermsText, request.Inclusions, request.Exclusions);
                if (textError is not null) return BadRequest(textError);
                var scheduleError = ValidateSchedule(
                    request.PricePerSeatKobo ?? trip.PricePerSeatKobo,
                    request.DepositKobo ?? trip.DepositKobo,
                    newInstalments ?? trip.Instalments,
                    newStart,
                    newMode,
                    newPayInFullBy,
                    checkDeadlineFuture ? HostTripService.TodayWat(DateTime.UtcNow) : null);
                if (scheduleError is not null) return scheduleError;
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

            if (becomingCommunity)
            {
                await ConvertToCommunityAsync(trip);
            }
            else if (willBeHosted)
            {
                if (becomingHosted)
                {
                    // Hosted trips are private (link-only) unless the host lists them.
                    trip.IsHostedTrip = true;
                    trip.HostProfileId = newHost!.Id;
                    trip.IsListed = request.IsListed ?? false;
                    trip.FeeMode = request.FeeMode ?? FeeMode.PassToTraveller;
                    trip.SeatHoldHours = request.SeatHoldHours ?? _hostOptions.DefaultSeatHoldHours;
                    ClaimFoundingFreeDeparture(trip, newHost);
                }
                else
                {
                    if (request.IsListed is not null) trip.IsListed = request.IsListed.Value;
                    if (request.FeeMode is not null) trip.FeeMode = request.FeeMode.Value;
                    if (request.SeatHoldHours is not null) trip.SeatHoldHours = request.SeatHoldHours.Value;
                }

                // Any change to who sets the dates, the deadline or the schedule
                // of a TravellerChooses trip (only possible before anyone has
                // paid) drops the travellers' chosen dates: they pick again.
                var datesSetupChanged = modeChanging
                    || (newMode == InstalmentDatesMode.TravellerChooses
                        && (newPayInFullBy != trip.PayInFullByDate
                            || (newInstalments is not null && InstalmentsChanged(trip, newInstalments))));

                if (request.PricePerSeatKobo is not null) trip.PricePerSeatKobo = request.PricePerSeatKobo;
                if (request.DepositKobo is not null) trip.DepositKobo = request.DepositKobo;
                if (newInstalments is not null) SyncInstalments(trip, newInstalments);
                trip.InstalmentDatesMode = newMode;
                trip.PayInFullByDate = newMode == InstalmentDatesMode.TravellerChooses ? newPayInFullBy : null;
                if (datesSetupChanged) await DeleteTripTravellerDatesAsync(trip.Id);

                // Every terms change bumps the version (travellers who already
                // accepted keep theirs; new acceptances must match the current one).
                // The very first terms on a trip stay at version 1.
                if (request.TermsText is not null)
                {
                    var terms = request.TermsText.Trim();
                    if (terms != trip.TermsText)
                    {
                        if (trip.TermsUpdatedAt is not null) trip.TermsVersion++;
                        trip.TermsText = terms;
                        trip.TermsUpdatedAt = DateTime.UtcNow;
                    }
                }

                if (request.Inclusions is not null) trip.Inclusions = NullIfBlank(request.Inclusions);
                if (request.Exclusions is not null) trip.Exclusions = NullIfBlank(request.Exclusions);
            }

            trip.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Converting to community adds approved travellers to the chat;
                // a concurrent thread change can collide. Nothing was committed.
                _db.ChangeTracker.Clear();
                return Conflict("This trip was updated at the same time - please retry.");
            }
            return NoContent();
        }

        private static bool PaymentTermsChanged(
            TripPost trip, long? price, long? deposit, FeeMode? feeMode, List<TripInstalment>? instalments,
            InstalmentDatesMode? datesMode, DateOnly? payInFullByDate)
        {
            if (price is not null && price != trip.PricePerSeatKobo) return true;
            if (deposit is not null && deposit != trip.DepositKobo) return true;
            if (feeMode is not null && feeMode != trip.FeeMode) return true;
            if (datesMode is not null && datesMode != trip.InstalmentDatesMode) return true;
            if (payInFullByDate is not null && payInFullByDate != trip.PayInFullByDate) return true;
            if (instalments is null) return false;
            return InstalmentsChanged(trip, instalments);
        }

        private static bool InstalmentsChanged(TripPost trip, List<TripInstalment> instalments)
        {
            var current = trip.Instalments.OrderBy(i => i.Sequence).Select(i => (i.Sequence, i.AmountKobo, i.DueDate));
            var next = instalments.OrderBy(i => i.Sequence).Select(i => (i.Sequence, i.AmountKobo, i.DueDate));
            return !current.SequenceEqual(next);
        }

        // Replaces the owned schedule in place: lines with an existing
        // Sequence are updated (not deleted and re-added under the same key).
        private static void SyncInstalments(TripPost trip, List<TripInstalment> lines)
        {
            var sequences = lines.Select(l => l.Sequence).ToHashSet();
            trip.Instalments.RemoveAll(i => !sequences.Contains(i.Sequence));
            foreach (var line in lines)
            {
                var existing = trip.Instalments.FirstOrDefault(i => i.Sequence == line.Sequence);
                if (existing is null)
                {
                    trip.Instalments.Add(line);
                }
                else
                {
                    existing.AmountKobo = line.AmountKobo;
                    existing.DueDate = line.DueDate;
                }
            }
        }

        // Hosted -> community (no payment has gone through). Clears every
        // hosted field, lists the trip, releases the host's founding free
        // departure if this trip held it, and moves approved travellers onto
        // the community flow: unpaid-but-held seats join the chat now (as an
        // approval would on a community trip); expired holds go back to
        // Requested so the author can decide again. Caller saves.
        private async Task ConvertToCommunityAsync(TripPost trip)
        {
            if (trip.HostProfileId is Guid hostId)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"HostProfiles\" WHERE \"Id\" = {hostId} FOR UPDATE");
                var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == hostId);
                if (host is not null && host.FoundingFreeDepartureTripId == trip.Id)
                {
                    host.FoundingFreeDepartureTripId = null;
                    host.UpdatedAt = DateTime.UtcNow;
                }
            }

            trip.IsHostedTrip = false;
            trip.HostProfileId = null;
            trip.IsListed = true;
            trip.PricePerSeatKobo = null;
            trip.DepositKobo = null;
            trip.Instalments.Clear();
            trip.FeeMode = FeeMode.PassToTraveller;
            trip.InstalmentDatesMode = InstalmentDatesMode.HostSet;
            trip.PayInFullByDate = null;
            await DeleteTripTravellerDatesAsync(trip.Id);
            trip.SeatHoldHours = _hostOptions.DefaultSeatHoldHours;
            trip.TermsText = null;
            trip.Inclusions = null;
            trip.Exclusions = null;
            trip.IsFoundingFreeDeparture = false;
            // TermsVersion / TermsUpdatedAt are kept so terms set later (if it
            // becomes hosted again) get a new version, never a reused one.

            var approved = await _db.TripInterests
                .Where(i => i.TripPostId == trip.Id && i.Status == TripInterestStatus.Approved)
                .ToListAsync();
            foreach (var interest in approved)
            {
                if (interest.PaymentStatus == InterestPaymentStatus.Expired)
                {
                    interest.Status = TripInterestStatus.Requested;
                    interest.RespondedAt = null;
                }
                else
                {
                    await StageAddToThreadAsync(trip, interest.RequesterUserId);
                }

                interest.PaymentStatus = InterestPaymentStatus.NotRequired;
                interest.SeatHoldExpiresAt = null;
            }
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
            // Hidden (moderated) trips drop out of other people's request lists.
            var trips = await WithDtoIncludes()
                .Where(t => tripIds.Contains(t.Id) && !t.IsHidden)
                .Select(t => new
                {
                    Trip = t,
                    ApprovedCount = _db.TripInterests
                        .Count(i =>
                            i.TripPostId == t.Id &&
                            i.Status == TripInterestStatus.Approved &&
                            i.PaymentStatus != InterestPaymentStatus.Expired &&
                            i.RequesterUserId != t.AuthorUserId)
                })
                .ToListAsync();
            var tripById = trips.ToDictionary(t => t.Trip.Id);
            var myDates = await TravellerInstalmentDates.LoadAsync(_db, interests.Select(i => i.Id).ToList());

            var result = interests
    .Where(i => tripById.ContainsKey(i.TripPostId))
    .Select(i => new MyTripRequestDto(
        i.Id,
        i.Status,
        i.Message,
        i.RequestedAt,
        i.RespondedAt,
        TripPostDto.FromEntity(
            tripById[i.TripPostId].Trip,
            i.Status.ToString(),
            tripById[i.TripPostId].ApprovedCount,
            _fees,
            TripViewerInterest.From(i, myDates.GetValueOrDefault(i.Id))
        )))
    .ToList();

            return result;
        }

        // MyTripPostDto plus the moderation flag, so authors can see that a
        // post of theirs has been hidden by an admin.
        public record MyTripPostWithModerationDto(
            TripPostDto Trip,
            int PendingInterestCount,
            bool HasThread,
            bool IsHidden);

        // Every trip the current user has posted (hidden ones included, with
        // IsHidden set), with a pending-request count (for the badge) and
        // whether a thread exists yet, so the page can show "Open chat" vs.
        // nothing without a thread lookup per card.
        [HttpGet("mine/posts")]
        public async Task<ActionResult<List<MyTripPostWithModerationDto>>> GetMyPosts()
        {
            var trips = await WithDtoIncludes()
                .Where(t => t.AuthorUserId == CurrentUserId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var tripIds = trips.Select(t => t.Id).ToList();

            // Pending (badge) and approved (participant) counts in one grouped query.
            // The caller is the author of every trip here, so excluding their own
            // id keeps ApprovedCount author-free.
            var interestCounts = await _db.TripInterests
                .Where(i => tripIds.Contains(i.TripPostId)
                    && (i.Status == TripInterestStatus.Requested || i.Status == TripInterestStatus.Approved))
                .GroupBy(i => i.TripPostId)
                .Select(g => new
                {
                    TripPostId = g.Key,
                    Pending = g.Count(i => i.Status == TripInterestStatus.Requested),
                    Approved = g.Count(i => i.Status == TripInterestStatus.Approved
                        && i.PaymentStatus != InterestPaymentStatus.Expired
                        && i.RequesterUserId != CurrentUserId)
                })
                .ToDictionaryAsync(x => x.TripPostId);

            var threadTripIds = (await _db.TripThreads
                .Where(t => tripIds.Contains(t.TripPostId))
                .Select(t => t.TripPostId)
                .ToListAsync())
                .ToHashSet();

            return trips
                .Select(t => new MyTripPostWithModerationDto(
                    TripPostDto.FromEntity(t, null, interestCounts.GetValueOrDefault(t.Id)?.Approved ?? 0, _fees),
                    interestCounts.GetValueOrDefault(t.Id)?.Pending ?? 0,
                    threadTripIds.Contains(t.Id),
                    t.IsHidden))
                .ToList();
        }

        // ---- Interests (the "Interested" button + approve/decline) ----

        // ContactPhone: optional; the host of a hosted trip uses it for WhatsApp
        // reminders (7–15 digits, optionally starting with +; spaces, dashes and
        // brackets are ignored).
        public record RequestInterestBody(string? Message, string? ContactPhone = null);

        [HttpPost("{id:guid}/interests")]
        public async Task<ActionResult<TripInterest>> RequestToJoin(Guid id, RequestInterestBody body)
        {
            if (CurrentUserId.Equals(Guid.Empty)) return BadRequest("User not logged in");

            string? contactPhone = null;
            if (!string.IsNullOrWhiteSpace(body.ContactPhone))
            {
                contactPhone = System.Text.RegularExpressions.Regex.Replace(body.ContactPhone.Trim(), @"[\s\-().]", "");
                if (!System.Text.RegularExpressions.Regex.IsMatch(contactPhone, @"^\+?[0-9]{7,15}$"))
                    return BadRequest("Phone number must be 7–15 digits, optionally starting with +.");
            }

            TripPost? trip;
            TripInterest interest;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id);
                if (trip is null || trip.Status != TripPostStatus.Open || trip.IsHidden) return NotFound();
                if (trip.AuthorUserId == CurrentUserId) return BadRequest("You can't join your own trip.");

                var existing = await _db.TripInterests
                    .FirstOrDefaultAsync(i => i.TripPostId == id && i.RequesterUserId == CurrentUserId);

                if (existing is not null)
                {
                    // Only a withdrawn request, or one the host removed after a
                    // missed payment, may be re-sent (the host decides again).
                    // Declined stays declined; Requested/Approved are in flight.
                    if (existing.Status == TripInterestStatus.Declined)
                        return Conflict("Your request to join this trip was declined.");
                    if (existing.Status is not (TripInterestStatus.Withdrawn or TripInterestStatus.Removed))
                        return Conflict("You've already requested to join this trip.");

                    existing.Status = TripInterestStatus.Requested;
                    existing.RemovedReason = null;
                    existing.RemovedAt = null;
                    await DeleteTravellerDatesAsync(existing.Id);
                    existing.Message = body.Message;
                    existing.RequestedAt = DateTime.UtcNow;
                    existing.RespondedAt = null;
                    if (contactPhone is not null) existing.ContactPhone = contactPhone;
                    interest = existing;
                }
                else
                {
                    interest = new TripInterest
                    {
                        TripPostId = id,
                        RequesterUserId = CurrentUserId,
                        Message = body.Message,
                        ContactPhone = contactPhone
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
        //  - if MaxGroupSize is set it counts the author + approved members
        //    (not expired hosted seat holds), so approval is refused once
        //    approvedCount + 1 >= MaxGroupSize.
        //  - community trips: approval adds the requester to the chat now.
        //  - hosted trips: approval holds the seat (AwaitingDeposit until
        //    now + SeatHoldHours); the chat comes with the first payment.
        //  - hosted trips: an Approved interest whose hold expired unpaid
        //    (PaymentStatus Expired) can be approved again (a new hold, same
        //    capacity rule; a terms acceptance is kept unless the terms
        //    changed since) or declined. Any other non-Requested interest: 409.
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

                // Besides a pending request, the host may decide again on an
                // approved hosted seat whose hold expired unpaid: approve
                // starts a fresh hold, decline closes it.
                var expiredHold = IsExpiredHold(trip, interest);
                if (interest.Status != TripInterestStatus.Requested && !expiredHold)
                    return Conflict("Already responded to.");

                if (body.Approve)
                {
                    if (trip.Status != TripPostStatus.Open)
                        return Conflict($"This trip is {trip.Status.ToString().ToLowerInvariant()}; requests can't be approved.");

                    if (trip.IsHidden)
                        return Conflict("This trip has been hidden by a moderator; requests can't be approved.");

                    if (trip.MaxGroupSize is not null)
                    {
                        var approvedCount = await _db.TripInterests
                            .CountAsync(i => i.TripPostId == id
                                && i.Status == TripInterestStatus.Approved
                                && i.PaymentStatus != InterestPaymentStatus.Expired);
                        if (approvedCount + 1 >= trip.MaxGroupSize.Value)
                            return Conflict("This trip's group is already full.");
                    }
                }

                interest.Status = body.Approve ? TripInterestStatus.Approved : TripInterestStatus.Declined;
                interest.RespondedAt = DateTime.UtcNow;

                if (body.Approve)
                {
                    if (trip.IsHostedTrip && interest.AmountPaidKobo == 0)
                    {
                        // Seat held until the deposit is paid; no chat access yet.
                        interest.PaymentStatus = InterestPaymentStatus.AwaitingDeposit;
                        interest.SeatHoldExpiresAt = DateTime.UtcNow.AddHours(trip.SeatHoldHours);

                        // A terms acceptance from before the host changed the
                        // terms no longer counts; a current one is kept.
                        if (interest.TermsVersionAccepted is not null && interest.TermsVersionAccepted != trip.TermsVersion)
                        {
                            ClearTermsAcceptance(interest);
                            await DeleteTravellerDatesAsync(interest.Id);
                        }

                        // A fresh hold gets a fresh "hold ends soon" warning.
                        await _db.PaymentReminderLogs
                            .Where(r => r.TripInterestId == interest.Id && r.Kind == PaymentReminderKind.SeatHoldExpiring)
                            .ExecuteDeleteAsync();
                    }
                    else
                    {
                        // Community trips (and a hosted traveller who already
                        // paid before withdrawing and re-requesting).
                        await StageAddToThreadAsync(trip, interest.RequesterUserId);
                    }
                }

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

        // Hosted trips: approved, but the seat hold ran out before the deposit.
        private static bool IsExpiredHold(TripPost trip, TripInterest interest) =>
            trip.IsHostedTrip
            && interest.Status == TripInterestStatus.Approved
            && interest.PaymentStatus == InterestPaymentStatus.Expired;

        private static void ClearTermsAcceptance(TripInterest interest)
        {
            interest.TermsAcceptedAt = null;
            interest.TermsVersionAccepted = null;
            interest.TermsTextSha256 = null;
            interest.TermsAcceptedIp = null;
            interest.TermsAcceptedUserAgent = null;
            interest.TermsInstalmentDatesJson = null;
        }

        // A traveller's chosen instalment dates (TravellerChooses trips) go
        // with their terms acceptance: they pick again when they next accept.
        private Task DeleteTravellerDatesAsync(Guid interestId) =>
            _db.TripInterestInstalmentDates.Where(d => d.TripInterestId == interestId).ExecuteDeleteAsync();

        private Task DeleteTripTravellerDatesAsync(Guid tripId) =>
            _db.TripInterestInstalmentDates.Where(d => d.TripInterest!.TripPostId == tripId).ExecuteDeleteAsync();

        // Requester only, hosted trips only: after an unpaid seat hold expired
        // (Approved + Expired) the traveller asks the host again. The interest
        // goes back to Requested (no hold, no payment state) and the host is
        // notified as for a new request. 404 for anyone but the requester and
        // on community trips; 409 from any other state.
        [HttpPost("{id:guid}/interests/{interestId:guid}/request-again")]
        public async Task<ActionResult<TripInterestDto>> RequestAgain(Guid id, Guid interestId)
        {
            TripPost? trip;
            TripInterest? interest;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id);
                if (trip is null || !trip.IsHostedTrip || trip.IsHidden) return NotFound();

                interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
                if (interest is null || interest.TripPostId != id || interest.RequesterUserId != CurrentUserId)
                    return NotFound();

                // Never from Removed (or anything else): only an expired seat hold.
                if (!IsExpiredHold(trip, interest))
                    return Conflict("Only a request whose seat hold expired can be sent again.");
                if (trip.Status != TripPostStatus.Open)
                    return Conflict($"This trip is {trip.Status.ToString().ToLowerInvariant()} and isn't taking requests.");

                interest.Status = TripInterestStatus.Requested;
                interest.PaymentStatus = InterestPaymentStatus.NotRequired;
                interest.SeatHoldExpiresAt = null;
                interest.RequestedAt = DateTime.UtcNow;
                interest.RespondedAt = null;
                await DeleteTravellerDatesAsync(interest.Id);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            await _notifications.NotifyInterestReceived(trip, CurrentUserId);

            return Ok(await PushInterestChangedAsync(trip, interest));
        }

        // Only a pending (Requested) or Approved interest can be withdrawn.
        // Withdrawing an approval also removes the requester from the trip
        // chat (never the author).
        // Hosted trips: payment records (SeatPayments, amounts paid) are kept,
        // refunds follow the host's own terms, the seat hold is released, and
        // the host is notified when an approved traveller leaves.
        [HttpDelete("{id:guid}/interests/{interestId:guid}")]
        public async Task<IActionResult> WithdrawInterest(Guid id, Guid interestId)
        {
            TripInterest? interest;
            var notifyHost = false;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                await LockTripAsync(id);

                interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
                if (interest is null || interest.TripPostId != id) return NotFound();
                if (interest.RequesterUserId != CurrentUserId) return Forbid();

                if (interest.Status is not (TripInterestStatus.Requested or TripInterestStatus.Approved))
                    return Conflict($"A {interest.Status.ToString().ToLowerInvariant()} request can't be withdrawn.");

                var isHostedTrip = await _db.TripPosts
                    .Where(t => t.Id == id)
                    .Select(t => t.IsHostedTrip)
                    .FirstOrDefaultAsync();
                if (isHostedTrip)
                {
                    notifyHost = interest.Status == TripInterestStatus.Approved;
                    interest.SeatHoldExpiresAt = null;
                    if (interest.PaymentStatus is InterestPaymentStatus.AwaitingDeposit or InterestPaymentStatus.Expired)
                        interest.PaymentStatus = InterestPaymentStatus.NotRequired;
                }

                if (interest.Status == TripInterestStatus.Approved)
                {
                    var authorId = await _db.TripPosts
                        .Where(t => t.Id == id)
                        .Select(t => t.AuthorUserId)
                        .FirstOrDefaultAsync();

                    if (interest.RequesterUserId != authorId)
                        await _threads.StageRemoveFromThreadAsync(id, interest.RequesterUserId);
                }

                interest.Status = TripInterestStatus.Withdrawn;
                interest.RespondedAt = DateTime.UtcNow;
                await DeleteTravellerDatesAsync(interest.Id);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            var trip = await _db.TripPosts.FindAsync(id);
            if (trip is not null)
            {
                if (notifyHost) await _notifications.NotifyHostedSeatWithdrawn(trip, interest.RequesterUserId);
                await PushInterestChangedAsync(trip, interest);
            }

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
                Status = interest.Status.ToString(),
                PaymentStatus = interest.PaymentStatus.ToString(),
                SeatHoldExpiresAt = interest.SeatHoldExpiresAt
            };

            await _tripPostHub.Clients
                .Group($"user:{interest.RequesterUserId}")
                .SendAsync("TripInterestStatusChanged", dto);

            var interestList = await _tripsRepo.GetAllInterests(trip.Id);

            await _tripPostHub.Clients
                .Group($"user:{trip.AuthorUserId}")
                .SendAsync("TripInterestUpdated", interestList);

            // Live manifest (hosted trips): request, approval, decline, withdrawal.
            if (trip.IsHostedTrip)
            {
                await _tripPostHub.Clients
                    .Group(TripPostHub.TripHostGroup(trip.Id))
                    .SendAsync("ManifestUpdated", new { tripId = trip.Id, interestId = interest.Id });
            }

            return dto;
        }

        // Stages (does not save) the thread and the user's participant row;
        // shared with PaymentService through ITripThreadService. Caller saves
        // inside its transaction.
        private Task StageAddToThreadAsync(TripPost trip, Guid userId) =>
            _threads.StageAddToThreadAsync(trip, userId);

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
