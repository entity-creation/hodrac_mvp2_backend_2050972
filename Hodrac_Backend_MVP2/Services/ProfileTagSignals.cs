using System.Text.Json;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.Services;

// ─── Profile tag signals ──────────────────────────────────────────────────────
// Shared helpers for the "matching-user" endpoints and behaviour-event publishing.
// Static (no DI registration needed). Every Mongo access is best-effort: a missing
// profile or an unavailable Mongo yields "no signal" so callers fall back to the
// generic popular ranking instead of failing the request.

public static class ProfileTagSignals
{
    /// <summary>
    /// Loads the caller's psychographic profile. Returns null for anonymous callers,
    /// users without a profile, or when Mongo is unreachable.
    /// </summary>
    public static async Task<UserPsychographicProfile?> TryLoadProfileAsync(
        IProfileRepository profiles, ICurrentUserService currentUser, ILogger? logger = null)
    {
        if (!currentUser.IsAuthenticated) return null;
        try
        {
            return await profiles.GetByAuthUserIdAsync(currentUser.UserIdString);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not load psychographic profile for {UserId}", currentUser.UserIdString);
            return null;
        }
    }

    /// <summary>
    /// The tags that describe what a user is into: their interaction-history tags
    /// (vibe tags of wishlists they saved) plus their onboarding answers
    /// (trip type preference and primary priority). Distinct, case-insensitive.
    /// </summary>
    public static List<string> ProfileTags(UserPsychographicProfile? profile)
    {
        if (profile is null) return new List<string>();

        var tags = new List<string>();
        tags.AddRange(profile.InteractionAggregations?.TopInteractedWishlistTags ?? new List<string>());
        var answers = profile.ExplicitOnboardingAnswers;
        if (answers is not null)
        {
            tags.Add(answers.TripTypePreference);
            tags.Add(answers.PrimaryPriority);
        }

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Parses a jsonb string-array column ("[\"Foodie\",\"Culture\"]") safely.</summary>
    public static List<string> ParseTagJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Normalises a tag to the Tag.Key form used by the taxonomy table:
    /// "Hidden Gem" / "hidden-gem" → "hidden_gem".
    /// </summary>
    public static string ToKey(string tag)
        => tag.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>Number of item tags that match a profile tag (case/separator-insensitive).</summary>
    public static int Overlap(IEnumerable<string> itemTags, IEnumerable<string> profileTags)
    {
        var wanted = profileTags.Select(ToKey).ToHashSet();
        return itemTags.Select(ToKey).Distinct().Count(wanted.Contains);
    }
}
