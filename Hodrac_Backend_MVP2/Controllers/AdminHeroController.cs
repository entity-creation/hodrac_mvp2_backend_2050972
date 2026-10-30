using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin-chosen landing-page hero wishlist (separate from the featured pool).
///
/// GET    /api/admin/hero   — current hero (204 when none)
/// PUT    /api/admin/hero   — { wishlistId, heroUntil? } replaces it
/// DELETE /api/admin/hero   — clears it (204, idempotent)
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/hero")]
public class AdminHeroController : ControllerBase
{
    private readonly IHeroWishlistService _hero;
    private readonly ICurrentUserService _currentUser;

    public AdminHeroController(IHeroWishlistService hero, ICurrentUserService currentUser)
    {
        _hero = hero;
        _currentUser = currentUser;
    }

    public record SetHeroRequest(Guid? WishlistId, DateTimeOffset? HeroUntil);

    [HttpGet]
    public async Task<ActionResult<AdminHeroDto>> Get(CancellationToken ct = default)
    {
        var hero = await _hero.GetAdminAsync(ct);
        return hero is null ? NoContent() : Ok(hero);
    }

    [HttpPut]
    public async Task<ActionResult<AdminHeroDto>> Set(SetHeroRequest request, CancellationToken ct = default)
    {
        var (hero, errors) = await _hero.SetAsync(request.WishlistId, request.HeroUntil, _currentUser.UserId, ct);
        if (errors.Count > 0)
        {
            foreach (var (field, message) in errors)
                ModelState.AddModelError(field, message);
            return ValidationProblem(ModelState);
        }
        return Ok(hero);
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct = default)
    {
        await _hero.ClearAsync(ct);
        return NoContent();
    }
}
