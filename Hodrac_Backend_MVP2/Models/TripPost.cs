using System;
using System.Collections.Generic;

namespace Hodrac_Backend_MVP2.Models
{
    public enum TripPostStatus
    {
        Open,
        Closed,
        Cancelled
    }

    // A trip post is anchored to exactly ONE of:
    //   - a single Destination (simple "I'm going to Kyoto" post), or
    //   - a Wishlist (a multi-stop itinerary template/fork the user is
    //     actually planning to travel).
    // Enforced by a DB check constraint (see AppDbContext) plus validation
    // in TripsController on create/update — never trust the client to only
    // send one.
    public class TripPost
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AuthorUserId { get; set; }

        public Guid? DestinationId { get; set; }
        public Destination? Destination { get; set; }

        public Guid? WishlistId { get; set; }
        public Wishlist? Wishlist { get; set; }

        // Fallback for when the poster's destination isn't in the catalog
        // yet, or they don't want to search for it. Optional, and only
        // meaningful when DestinationId and WishlistId are both null — see
        // TripPostDto.FromEntity for how these three are reconciled into
        // one label.
        public string? FreeTextDestination { get; set; }

        // These are the poster's actual planned travel dates for THIS trip,
        // independent of Wishlist.TotalDays (which is just template length).
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public bool IsDateFlexible { get; set; }

        public string Caption { get; set; } = string.Empty;

        // Optional override. If null, the UI falls back to the first
        // Destination image or the Wishlist's WishlistHeroImage — see
        // TripPostDto.FromEntity.
        public string? CoverImageUrl { get; set; }

        public int? MaxGroupSize { get; set; }

        public TripPostStatus Status { get; set; } = TripPostStatus.Open;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TripInterest> Interests { get; set; } = new List<TripInterest>();
        public TripThread? Thread { get; set; }
    }
}
