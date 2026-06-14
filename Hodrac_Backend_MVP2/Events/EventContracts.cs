namespace Hodrac_Backend_MVP2.Events;

// ─── Event contracts ──────────────────────────────────────────────────────────
// These are the message shapes published to Azure Service Bus / RabbitMQ.
// The Behavioral Engine subscribes to these and updates MongoDB accordingly.

/// <summary>Published by Postgres layer when a user saves a wishlist.</summary>
public record WishlistSavedEvent(
    string UserId,
    string WishlistId,
    List<string> WishlistTags,
    string PeopleType,
    string BudgetProfile,
    DateTimeOffset OccurredAt
);

/// <summary>Published when a user clicks through to a destination detail page.</summary>
public record DestinationClickedEvent(
    string UserId,
    string DestinationId,
    List<string> DestinationTags,
    int LuxuryRating,
    DateTimeOffset OccurredAt
);

/// <summary>Published when a search is executed (after phonetic/semantic pipeline).</summary>
public record SearchExecutedEvent(
    string UserId,
    string RawQuery,
    string CanonicalPhrase,
    string SemanticClusterId,
    DateTimeOffset OccurredAt
);

/// <summary>Published when a user completes the personalization modal.</summary>
public record OnboardingCompletedEvent(
    string UserId,
    string WhoTheyTravelWith,
    string TripTypePreference,
    string BudgetProfile,
    string PrimaryPriority,
    DateTimeOffset OccurredAt
);
