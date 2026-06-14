using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // COLLABORATION CONTROLLER
    // POST   /api/user-wishlists/{id}/collaborators         — add collaborator
    // DELETE /api/user-wishlists/{id}/collaborators/{uid}   — remove collaborator
    // PUT    /api/user-wishlists/{id}/collaborators/{uid}   — change role
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api/user-wishlists/{wishlistId:guid}/collaborators")]
    public class CollaborationController : ControllerBase
    {
        private readonly ICollaboratorRepository _collaborators;
        private readonly IUserRepository _users;
        private readonly ICurrentUserService _currentUser;

        public CollaborationController(
            ICollaboratorRepository collaborators,
            IUserRepository users,
            ICurrentUserService currentUser)
        {
            _collaborators = collaborators;
            _users = users;
            _currentUser = currentUser;
        }

        [HttpPost]
        public async Task<IActionResult> Add(
            Guid wishlistId,
            [FromBody] AddCollaboratorRequestDto request,
            CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var callerRole = await _collaborators.GetRoleAsync(wishlistId, _currentUser.UserId, ct);
            if (callerRole != "Owner") return Forbid();

            if (request.Role is not "Editor" and not "Viewer")
                return BadRequest("Role must be 'Editor' or 'Viewer'.");

            // Resolve the target user by email — returns null if no account exists
            var targetUserId = await _users.ResolveUserIdByEmailAsync(request.Email, ct);
            if (targetUserId is null)
                return NotFound($"No Hodrac account found for email '{request.Email}'.");

            try
            {
                var collaborator = await _collaborators.AddAsync(
                    wishlistId, targetUserId.Value, request.Email, request.Role, ct);

                return Ok(new CollaboratorDto(
                    collaborator.UserId,
                    collaborator.SharedUserEmail,
                    collaborator.Role,
                    collaborator.JoinedAt));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpDelete("{userId:guid}")]
        public async Task<IActionResult> Remove(
            Guid wishlistId,
            Guid userId,
            CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var callerRole = await _collaborators.GetRoleAsync(wishlistId, _currentUser.UserId, ct);
            if (callerRole != "Owner" && _currentUser.UserId != userId) return Forbid();

            var removed = await _collaborators.RemoveAsync(wishlistId, userId, ct);
            return removed ? NoContent() : NotFound("Collaborator not found.");
        }

        [HttpPut("{userId:guid}")]
        public async Task<IActionResult> ChangeRole(
            Guid wishlistId,
            Guid userId,
            [FromBody] ChangeRoleRequest request,
            CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var callerRole = await _collaborators.GetRoleAsync(wishlistId, _currentUser.UserId, ct);
            if (callerRole != "Owner") return Forbid();

            if (request.NewRole is not "Editor" and not "Viewer")
                return BadRequest("Role must be 'Editor' or 'Viewer'.");

            var changed = await _collaborators.ChangeRoleAsync(wishlistId, userId, request.NewRole, ct);
            return changed ? Ok(new { role = request.NewRole }) : NotFound("Collaborator not found.");
        }
    }
}

public record ChangeRoleRequest(string NewRole);
