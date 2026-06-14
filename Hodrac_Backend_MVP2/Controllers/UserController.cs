using Hodrac_Backend_MVP2.DTOs.UserDtos;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // USER CONTROLLER
    // GET  /api/user/profile
    // POST /api/events/interaction
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api")]
    public class UserController : ControllerBase
    {
        private readonly IProfileRepository _profiles;
        private readonly IInteractionEventRepository _events;
        private readonly ICurrentUserService _currentUser;

        public UserController(
            IProfileRepository profiles,
            IInteractionEventRepository events,
            ICurrentUserService currentUser)
        {
            _profiles = profiles;
            _events = events;
            _currentUser = currentUser;
        }

        [HttpGet("user/profile")]
        public async Task<IActionResult> GetProfile(CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var profile = await _profiles.GetByAuthUserIdAsync(_currentUser.UserIdString);
            if (profile is null) return NotFound("No behavioral profile found for this user.");

            return Ok(new UserProfileDto(
                UserId: profile.AnonymizedMarketplaceId,
                TravelGroup: profile.ExplicitOnboardingAnswers.WhoTheyTravelWith,
                BudgetProfile: profile.ExplicitOnboardingAnswers.BudgetProfile,
                PrimaryPriority: profile.ExplicitOnboardingAnswers.PrimaryPriority,
                TopTags: profile.InteractionAggregations.TopInteractedWishlistTags,
                PrimaryTravelerType: profile.InferredTravelerPrototypes.PrimaryType,
                SecondaryTravelerTypes: profile.InferredTravelerPrototypes.SecondaryTypes
            ));
        }

        [HttpPost("events/interaction")]
        public async Task<IActionResult> LogInteraction(
            [FromBody] InteractionBeaconRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.EventType) ||
                string.IsNullOrWhiteSpace(request.EntityId))
                return BadRequest("EventType and EntityId are required.");

            var userId = _currentUser.IsAuthenticated
                ? _currentUser.UserIdString
                : "anonymous";

            await _events.InsertAsync(new NoSql.Models.InteractionEvent
            {
                EventId = $"evt_{Guid.NewGuid():N}",
                UserId = userId,
                EventType = request.EventType,
                EntityId = request.EntityId,
                EntityTags = request.EntityTags ?? new List<string>(),
                Timestamp = DateTime.UtcNow,
                Context = new NoSql.Models.EventContext
                {
                    Page = request.Page ?? string.Empty,
                    Position = request.Position ?? 0,
                }
            });

            return Accepted();
        }
    }

    public record InteractionBeaconRequest(
        string EventType,
        string EntityId,
        List<string>? EntityTags,
        string? Page,
        int? Position
    );
}
