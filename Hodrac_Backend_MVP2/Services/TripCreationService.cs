using Hodrac_Backend_MVP2.Controllers;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Hodrac_Backend_MVP2.Controllers.TripsController;

namespace Hodrac_Backend_MVP2.Services;

public enum TripCreateOutcome
{
    Created,
    /// <summary>400 with <see cref="TripCreateResult.Error"/> as the body.</summary>
    BadRequest,
    /// <summary>400 validation problem keyed by request field (<see cref="TripCreateResult.FieldErrors"/>).</summary>
    Invalid,
    /// <summary>The host profile (admin create-for-host) doesn't exist: 404.</summary>
    NotFound,
    /// <summary>The host can't take a hosted trip yet (admin create-for-host: no payout subaccount, or closed): 409.</summary>
    Conflict,
    /// <summary>The caller's own host account was closed by Hodrac: 403.</summary>
    Forbidden
}

public record TripCreateResult(
    TripCreateOutcome Outcome,
    string? Error = null,
    IReadOnlyList<(string Key, string Message)>? FieldErrors = null,
    TripPost? Trip = null,
    TripPostDto? Dto = null)
{
    public static TripCreateResult Bad(string error) => new(TripCreateOutcome.BadRequest, error);
}

/// <summary>
/// Creates trips for <c>POST /api/trips</c> (community or hosted, authored by
/// the caller) and <c>POST /api/admin/hosts/{id}/trips</c> (a hosted trip an
/// admin builds on a host's behalf, authored by the host). Both paths share
/// the same validation, the host row lock and the founding free departure
/// claim, so they can't drift apart.
/// </summary>
public class TripCreationService
{
    private readonly HodracDbContext _db;
    private readonly IHubContext<TripPostHub> _tripPostHub;
    private readonly FeeCalculator _fees;
    private readonly HodracHostOptions _hostOptions;

    public TripCreationService(
        HodracDbContext db,
        IHubContext<TripPostHub> tripPostHub,
        FeeCalculator fees,
        IOptions<HodracHostOptions> hostOptions)
    {
        _db = db;
        _tripPostHub = tripPostHub;
        _fees = fees;
        _hostOptions = hostOptions.Value;
    }

    // ── Shared rules (also used by TripsController's edit path) ───────────────

    // The hosted-only fields of a create/edit request. Blank text and an
    // empty instalment list count as "not sent".
    public record HostedFieldsInput(
        long? PricePerSeatKobo,
        long? DepositKobo,
        List<TripInstalmentRequest>? Instalments,
        FeeMode? FeeMode,
        int? SeatHoldHours,
        string? TermsText,
        string? Inclusions,
        string? Exclusions)
    {
        public bool AnySent =>
            PricePerSeatKobo is not null
            || DepositKobo is not null
            || Instalments is { Count: > 0 }
            || FeeMode is not null
            || SeatHoldHours is not null
            || !string.IsNullOrWhiteSpace(TermsText)
            || !string.IsNullOrWhiteSpace(Inclusions)
            || !string.IsNullOrWhiteSpace(Exclusions);
    }

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static List<TripInstalment> ToInstalments(IEnumerable<TripInstalmentRequest?>? lines) =>
        (lines ?? Enumerable.Empty<TripInstalmentRequest?>())
            .Where(l => l is not null)
            .Select(l => new TripInstalment { Sequence = l!.Sequence, AmountKobo = l.AmountKobo, DueDate = l.DueDate })
            .ToList();

    public static string? ValidateHostedText(int? seatHoldHours, string? termsText, string? inclusions, string? exclusions)
    {
        if (seatHoldHours is not null && (seatHoldHours < MinSeatHoldHours || seatHoldHours > MaxSeatHoldHours))
            return $"Seat hold must be between {MinSeatHoldHours} and {MaxSeatHoldHours} hours.";
        if (termsText is not null && termsText.Trim().Length > MaxTermsTextLength)
            return $"Terms can't be longer than {MaxTermsTextLength} characters.";
        if (inclusions is not null && inclusions.Trim().Length > MaxInclusionsLength)
            return $"Inclusions can't be longer than {MaxInclusionsLength} characters.";
        if (exclusions is not null && exclusions.Trim().Length > MaxInclusionsLength)
            return $"Exclusions can't be longer than {MaxInclusionsLength} characters.";
        return null;
    }

    // Shared by create + edit. Null fields are "not provided" and skipped.
    public static string? ValidateTripFields(
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

    // Row-locks (inside the caller's transaction) and loads the user's host
    // profile, or says what's missing before a hosted trip can exist.
    // Verification is NOT needed to create/preview; it's checked at checkout.
    // Closed: the host account was closed by Hodrac (Error is then
    // HostClosure.HostMessage and callers answer 403).
    public async Task<(HostProfile? Host, string? Error, bool Closed)> LoadHostForUserAsync(Guid userId)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"HostProfiles\" WHERE \"UserId\" = {userId} FOR UPDATE");
        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.UserId == userId);
        if (host is null)
            return (null, "Create a host profile before creating a hosted trip.", false);
        if (host.IsClosed)
            return (null, HostClosure.HostMessage, true);
        if (string.IsNullOrWhiteSpace(host.PaystackSubaccountCode))
            return (null, "Add a payout account to your host profile before creating a hosted trip.", false);
        return (host, null, false);
    }

    // The first hosted trip of a founding host (up to the founding
    // deadline) is their free departure. Caller holds the host row lock.
    public void ClaimFoundingFreeDeparture(TripPost trip, HostProfile host)
    {
        if (!host.IsFoundingHost || host.FoundingFreeDepartureTripId is not null) return;
        if (DateOnly.FromDateTime(DateTime.UtcNow) > _hostOptions.FoundingDeadline) return;

        trip.IsFoundingFreeDeparture = true;
        host.FoundingFreeDepartureTripId = trip.Id;
        host.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Everything <see cref="TripPostDto.FromEntity"/> reads.</summary>
    public static IQueryable<TripPost> WithDtoIncludes(IQueryable<TripPost> trips) => trips
        .Include(t => t.Destination!).ThenInclude(d => d.Country)
        .Include(t => t.Destination!).ThenInclude(d => d.Images)
        .Include(t => t.Wishlist!).ThenInclude(w => w.WishlistDestinations)
            .ThenInclude(wd => wd.Destination).ThenInclude(d => d.Images)
        // Only the public badge fields are ever projected (TripHostDto).
        .Include(t => t.HostProfile);

    // ── Create ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/trips</c>: a community or hosted trip authored by
    /// <paramref name="authorUserId"/> (a hosted trip uses the author's own host profile).
    /// </summary>
    public Task<TripCreateResult> CreateAsync(Guid authorUserId, CreateTripRequest request) =>
        CreateCoreAsync(authorUserId, request, hostProfileId: null);

    /// <summary>
    /// <c>POST /api/admin/hosts/{id}/trips</c>: a hosted trip on the host's
    /// behalf. The author is the host's user; same rules as a host creating it.
    /// No host → NotFound; no payout subaccount → Conflict.
    /// </summary>
    public async Task<TripCreateResult> CreateForHostAsync(Guid hostProfileId, CreateTripRequest request)
    {
        if (request.IsHostedTrip == false)
            return TripCreateResult.Bad("Trips created for a host are always hosted trips.");

        var authorUserId = await _db.HostProfiles
            .Where(h => h.Id == hostProfileId)
            .Select(h => (Guid?)h.UserId)
            .FirstOrDefaultAsync();
        if (authorUserId is null)
            return new TripCreateResult(TripCreateOutcome.NotFound, "Host not found.");

        return await CreateCoreAsync(authorUserId.Value, request with { IsHostedTrip = true }, hostProfileId);
    }

    private async Task<TripCreateResult> CreateCoreAsync(Guid authorUserId, CreateTripRequest request, Guid? hostProfileId)
    {
        if (request.DestinationId is not null && request.WishlistId is not null)
            return TripCreateResult.Bad("A trip can reference a destination or a wishlist, not both.");

        var validationError = ValidateTripFields(
            request.Caption, request.StartDate, request.EndDate, request.MaxGroupSize, request.CoverImageUrl);
        if (validationError is not null) return TripCreateResult.Bad(validationError);

        if (request.FreeTextDestination is not null
            && request.FreeTextDestination.Trim().Length > MaxFreeTextDestinationLength)
            return TripCreateResult.Bad($"Destination text can't be longer than {MaxFreeTextDestinationLength} characters.");

        // Community trips (the default) carry no price/payment fields and
        // are always listed. Hosted trips need the full payment setup.
        var isHosted = request.IsHostedTrip == true;
        var hostedFields = new HostedFieldsInput(
            request.PricePerSeatKobo, request.DepositKobo, request.Instalments, request.FeeMode,
            request.SeatHoldHours, request.TermsText, request.Inclusions, request.Exclusions);
        var instalments = ToInstalments(request.Instalments);
        if (!isHosted)
        {
            if (hostedFields.AnySent) return TripCreateResult.Bad(HostedFieldsOnCommunityMessage);
            if (request.IsListed == false) return TripCreateResult.Bad(CommunityUnlistedMessage);
        }
        else
        {
            if (request.MaxGroupSize is null)
                return TripCreateResult.Bad("Hosted trips need a max group size.");
            if (string.IsNullOrWhiteSpace(request.TermsText))
                return TripCreateResult.Bad("Hosted trips need written terms (refunds and dropouts).");
            var textError = ValidateHostedText(request.SeatHoldHours, request.TermsText, request.Inclusions, request.Exclusions);
            if (textError is not null) return TripCreateResult.Bad(textError);
            var scheduleErrors = InstalmentValidator.Validate(request.PricePerSeatKobo, request.DepositKobo, instalments, request.StartDate);
            if (scheduleErrors.Count > 0)
                return new TripCreateResult(TripCreateOutcome.Invalid,
                    FieldErrors: scheduleErrors.Select(e => (e.Key, e.Value)).ToList());
        }

        if (request.DestinationId is not null
            && !await _db.Set<Destination>().AnyAsync(d => d.DestinationId == request.DestinationId))
            return TripCreateResult.Bad("Destination not found.");

        // A trip may run on a platform template, or on a wishlist the
        // author owns or has been shared on - never on someone else's
        // private fork. Unauthorized looks the same as missing so we
        // don't leak which private wishlist ids exist.
        if (request.WishlistId is not null
            && !await _db.Set<Wishlist>().AnyAsync(w =>
                w.WishlistId == request.WishlistId
                && (w.IsTemplate
                    || w.OwnerUserId == authorUserId
                    || _db.WishlistCollaborators.Any(c => c.WishlistId == w.WishlistId && c.UserId == authorUserId))))
            return TripCreateResult.Bad("Wishlist not found.");

        // One transaction so a hosted trip and the host's founding free
        // departure (host row locked) are claimed together.
        await using var tx = await _db.Database.BeginTransactionAsync();
        HostProfile? host = null;
        if (isHosted)
        {
            if (hostProfileId is Guid hostId)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"HostProfiles\" WHERE \"Id\" = {hostId} FOR UPDATE");
                host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == hostId);
                if (host is null)
                    return new TripCreateResult(TripCreateOutcome.NotFound, "Host not found.");
                if (host.IsClosed)
                    return new TripCreateResult(TripCreateOutcome.Conflict, HostClosure.AdminMessage);
                if (string.IsNullOrWhiteSpace(host.PaystackSubaccountCode))
                    return new TripCreateResult(TripCreateOutcome.Conflict,
                        "This host has no payout account yet, so a hosted trip can't be created for them.");
            }
            else
            {
                var (loadedHost, hostError, closed) = await LoadHostForUserAsync(authorUserId);
                if (closed) return new TripCreateResult(TripCreateOutcome.Forbidden, hostError);
                if (hostError is not null) return TripCreateResult.Bad(hostError);
                host = loadedHost!;
            }
        }

        var trip = new TripPost
        {
            AuthorUserId = authorUserId,
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

        if (host is not null)
        {
            // Hosted trips are private (link-only) unless the host lists them.
            trip.IsHostedTrip = true;
            trip.HostProfileId = host.Id;
            trip.IsListed = request.IsListed ?? false;
            trip.PricePerSeatKobo = request.PricePerSeatKobo;
            trip.DepositKobo = request.DepositKobo;
            trip.Instalments = instalments;
            trip.FeeMode = request.FeeMode ?? FeeMode.PassToTraveller;
            trip.SeatHoldHours = request.SeatHoldHours ?? _hostOptions.DefaultSeatHoldHours;
            trip.TermsText = request.TermsText!.Trim();
            trip.TermsVersion = 1;
            trip.TermsUpdatedAt = DateTime.UtcNow;
            trip.Inclusions = NullIfBlank(request.Inclusions);
            trip.Exclusions = NullIfBlank(request.Exclusions);
            ClaimFoundingFreeDeparture(trip, host);
        }

        _db.TripPosts.Add(trip);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var loaded = await WithDtoIncludes(_db.TripPosts).FirstOrDefaultAsync(t => t.Id == trip.Id);
        // A brand-new trip has no approved members yet (just the author).
        var createdDto = TripPostDto.FromEntity(loaded!, myInterestStatus: null, approvedCount: 0, fees: _fees);
        // Private (unlisted) hosted trips are link-only: never broadcast them.
        if (trip.IsListed)
            await _tripPostHub.Clients
                .All
                .SendAsync("TripPostCreated", createdDto);

        return new TripCreateResult(TripCreateOutcome.Created, Trip: trip, Dto: createdDto);
    }
}
