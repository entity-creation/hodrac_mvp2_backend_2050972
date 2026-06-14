using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Hodrac_Backend_MVP2.Services;

/// <summary>
/// Resolves the authenticated user's identity from the validated JWT in HttpContext.
/// Injected into every controller that needs user identity — replaces the repeated
/// inline GetCallerUserId() helper that was scattered across controllers.
///
/// The JWT middleware validates the token and populates HttpContext.User before
/// any controller action runs. This service just reads those claims.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The authenticated user's ID as a Guid.
    /// Returns Guid.Empty if the request is unauthenticated.
    /// This Guid is the primary key for all Postgres FK references (SavedWishlist, WishlistCollaborator, etc.)
    /// and maps to auth_user_reference_id in MongoDB.
    /// </summary>
    Guid UserId { get; }

    /// <summary>The raw string form of UserId — used for MongoDB lookups.</summary>
    string UserIdString { get; }

    /// <summary>The user's email claim from the JWT.</summary>
    string Email { get; }

    /// <summary>True if the request carries a validated JWT with a user ID claim.</summary>
    bool IsAuthenticated { get; }
}

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContext;

    public CurrentUserService(IHttpContextAccessor httpContext)
        => _httpContext = httpContext;

    private ClaimsPrincipal? User => _httpContext.HttpContext?.User;

    public Guid UserId
    {
        get
        {
            // "sub" is the standard JWT subject claim — this is what ASP.NET Core Identity issues.
            var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User?.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }

    public string UserIdString =>
        User?.FindFirstValue(ClaimTypes.NameIdentifier)
     ?? User?.FindFirstValue("sub")
     ?? string.Empty;

    public string Email =>
        User?.FindFirstValue(ClaimTypes.Email)
     ?? User?.FindFirstValue("email")
     ?? string.Empty;

    public bool IsAuthenticated =>
        UserId != Guid.Empty;
}
