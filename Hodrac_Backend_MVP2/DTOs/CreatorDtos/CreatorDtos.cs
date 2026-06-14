namespace Hodrac.DTOs;

// ─── Creator ──────────────────────────────────────────────────────────────────

/// <summary>Public-facing creator card — safe to expose in API responses.</summary>
public record CreatorDto(
    Guid    CreatorId,
    string  DisplayName,
    string  Handle,
    string  PlatformName,
    string  ProfileUrl,
    string? AvatarUrl,
    string? Bio,
    bool    IsVerified
);

/// <summary>Request body for registering a new creator.</summary>
public record CreateCreatorRequest(
    string  DisplayName,
    string  Handle,
    string  PlatformName,
    string  ProfileUrl,
    string? AvatarUrl,
    string? Bio,
    string? ContactEmail  // stored internally, never returned in CreatorDto
);

/// <summary>Request body for updating an existing creator's profile.</summary>
public record UpdateCreatorRequest(
    string? DisplayName,
    string? Handle,
    string? PlatformName,
    string? ProfileUrl,
    string? AvatarUrl,
    string? Bio,
    string? ContactEmail,
    bool?   IsVerified    // only settable by admin in practice
);

// ─── WishlistCreatorAttribution ───────────────────────────────────────────────

/// <summary>Full attribution record returned on the wishlist detail page.</summary>
public record WishlistCreatorAttributionDto(
    Guid    WishlistCreatorAttributionId,
    Guid    WishlistId,
    CreatorDto Creator,
    string  OriginalContentUrl,
    string  PermissionType,
    DateTimeOffset PermissionGrantedAt,
    string? PermissionEvidenceUrl,
    bool    IsActive,
    string? AttributionNote,
    DateTimeOffset CreatedAt
);

/// <summary>Request body for attaching a creator attribution to a wishlist.</summary>
public record AttachCreatorAttributionRequest(
    Guid    CreatorId,
    string  OriginalContentUrl,

    /// <summary>
    /// One of: "Verbal", "Email", "WrittenContract", "OpenLicense", "SelfAuthored"
    /// </summary>
    string  PermissionType,

    DateTimeOffset PermissionGrantedAt,
    string? PermissionEvidenceUrl,
    string? AttributionNote
);

/// <summary>Request body for updating an existing attribution (e.g. adding evidence URL later).</summary>
public record UpdateCreatorAttributionRequest(
    string? OriginalContentUrl,
    string? PermissionType,
    DateTimeOffset? PermissionGrantedAt,
    string? PermissionEvidenceUrl,
    bool?   IsActive,
    string? AttributionNote
);
