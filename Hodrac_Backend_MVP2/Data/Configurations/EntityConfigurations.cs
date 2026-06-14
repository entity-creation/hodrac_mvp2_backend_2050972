using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hodrac_Backend_MVP2.Data.Configurations;

// ─── Wishlist ─────────────────────────────────────────────────────────────────

public class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.HasKey(w => w.WishlistId);

        // Self-referencing FK: ForkedFromId → WishlistId
        // NoAction on delete prevents cascade cycle errors.
        builder.HasOne(w => w.ForkedFrom)
               .WithMany()
               .HasForeignKey(w => w.ForkedFromId)
               .OnDelete(DeleteBehavior.NoAction)
               .IsRequired(false);

        // Optimistic concurrency via PostgreSQL's built-in xmin system column.
        // xmin is a hidden uint column that PostgreSQL increments automatically on
        // every row write — no trigger, no default value, no NOT NULL violation.
        // Npgsql's EF Core provider maps this directly via UseXminAsConcurrencyToken().
        builder.UseXminAsConcurrencyToken();

        // jsonb columns (no additional config needed — Column attribute handles type)
        builder.Property(w => w.GlobalInclusionsJson).HasColumnType("jsonb");
        builder.Property(w => w.PsychologicalVibeTagsJson).HasColumnType("jsonb");

        // Useful indexes
        builder.HasIndex(w => w.IsTemplate);
        builder.HasIndex(w => w.IsFeatured);
        builder.HasIndex(w => w.OwnerUserId);
        builder.HasIndex(w => w.TotalGlobalSaveCount);
        builder.HasIndex(w => w.CreatedAt);
    }
}

// ─── TransitRoute ─────────────────────────────────────────────────────────────
// Two FKs to City — must disable cascade on one to prevent multiple cascade paths.

public class TransitRouteConfiguration : IEntityTypeConfiguration<TransitRoute>
{
    public void Configure(EntityTypeBuilder<TransitRoute> builder)
    {
        builder.HasKey(t => t.TransitRouteId);

        builder.HasOne(t => t.OriginCity)
               .WithMany(c => c.OriginRoutes)
               .HasForeignKey(t => t.OriginCityId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DestinationCity)
               .WithMany(c => c.DestinationRoutes)
               .HasForeignKey(t => t.DestinationCityId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

// ─── ItineraryDay ─────────────────────────────────────────────────────────────
// Three optional FKs to City — all must be Restrict to avoid cascade fan-out.

public class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>
{
    public void Configure(EntityTypeBuilder<ItineraryDay> builder)
    {
        builder.HasKey(d => d.ItineraryDayId);

        builder.HasOne(d => d.MorningCity)
               .WithMany()
               .HasForeignKey(d => d.MorningCityId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(d => d.AfternoonCity)
               .WithMany()
               .HasForeignKey(d => d.AfternoonCityId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(d => d.EveningCity)
               .WithMany()
               .HasForeignKey(d => d.EveningCityId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(d => d.TransitFromPreviousDayRoute)
               .WithMany()
               .HasForeignKey(d => d.TransitFromPreviousDayRouteId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);

        builder.HasIndex(d => new { d.WishlistId, d.DayNumber });
    }
}

// ─── AggregatedSearchRegistry ─────────────────────────────────────────────────

public class AggregatedSearchRegistryConfiguration : IEntityTypeConfiguration<AggregatedSearchRegistry>
{
    public void Configure(EntityTypeBuilder<AggregatedSearchRegistry> builder)
    {
        builder.HasKey(a => a.AggregatedSearchRegistryId);

        // pgvector column — 384 dimensions (all-MiniLM-L6-v2 / ada-002 truncated)
        builder.Property(a => a.SemanticEmbedding)
               .HasColumnType("vector(384)");

        // Exact-match lookup on normalized phrase
        builder.HasIndex(a => a.MasterSearchPhrase);

        // Cluster group lookup (used by nightly merge job)
        builder.HasIndex(a => a.SemanticClusterId);

        // IVFFlat index for cosine similarity ANN search.
        // lists=100 is a sensible default for < 1M rows; tune after production load.
        // Requires pgvector >= 0.5.0.
        builder.HasIndex(a => a.SemanticEmbedding)
               .HasMethod("ivfflat")
               .HasOperators("vector_cosine_ops")
               .HasStorageParameter("lists", 100);
    }
}

// ─── Join table composite PKs ─────────────────────────────────────────────────

public class DestinationCategoryConfiguration : IEntityTypeConfiguration<DestinationCategory>
{
    public void Configure(EntityTypeBuilder<DestinationCategory> builder)
        => builder.HasKey(dc => new { dc.DestinationId, dc.CategoryId });
}

public class DestinationTagConfiguration : IEntityTypeConfiguration<DestinationTag>
{
    public void Configure(EntityTypeBuilder<DestinationTag> builder)
        => builder.HasKey(dt => new { dt.DestinationId, dt.TagId });
}

public class DestinationCurrencyConfiguration : IEntityTypeConfiguration<DestinationCurrency>
{
    public void Configure(EntityTypeBuilder<DestinationCurrency> builder)
        => builder.HasKey(dc => new { dc.DestinationId, dc.CurrencyId });
}

public class DestinationLanguageConfiguration : IEntityTypeConfiguration<DestinationLanguage>
{
    public void Configure(EntityTypeBuilder<DestinationLanguage> builder)
        => builder.HasKey(dl => new { dl.DestinationId, dl.LanguageId });
}

public class DestinationCityConfiguration : IEntityTypeConfiguration<DestinationCity>
{
    public void Configure(EntityTypeBuilder<DestinationCity> builder)
        => builder.HasKey(dc => new { dc.DestinationId, dc.CityId });
}

public class CountryLanguageConfiguration : IEntityTypeConfiguration<CountryLanguage>
{
    public void Configure(EntityTypeBuilder<CountryLanguage> builder)
        => builder.HasKey(cl => new { cl.CountryId, cl.LanguageId });
}

public class WishlistDestinationConfiguration : IEntityTypeConfiguration<WishlistDestination>
{
    public void Configure(EntityTypeBuilder<WishlistDestination> builder)
    {
        builder.HasKey(wd => new { wd.WishlistId, wd.DestinationId });

        builder.HasOne(wd => wd.Wishlist)
               .WithMany(w => w.WishlistDestinations)
               .HasForeignKey(wd => wd.WishlistId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(wd => wd.Destination)
               .WithMany(d => d.WishlistDestinations)
               .HasForeignKey(wd => wd.DestinationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

// ─── SavedWishlist / SavedDestination ─────────────────────────────────────────

public class SavedWishlistConfiguration : IEntityTypeConfiguration<SavedWishlist>
{
    public void Configure(EntityTypeBuilder<SavedWishlist> builder)
    {
        builder.HasKey(sw => sw.SavedWishlistId);

        // One user can only save a wishlist once
        builder.HasIndex(sw => new { sw.UserId, sw.WishlistId }).IsUnique();
    }
}

public class SavedDestinationConfiguration : IEntityTypeConfiguration<SavedDestination>
{
    public void Configure(EntityTypeBuilder<SavedDestination> builder)
    {
        builder.HasKey(sd => sd.SavedDestinationId);
        builder.HasIndex(sd => new { sd.UserId, sd.DestinationId }).IsUnique();
    }
}

// ─── Destination ──────────────────────────────────────────────────────────────

public class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.HasKey(d => d.DestinationId);

        // Full-text / phonetic search indexes
        builder.HasIndex(d => d.CleanNormalizedSearchName);
        builder.HasIndex(d => d.DoubleMetaphonePrimary);
        builder.HasIndex(d => d.MetaphoneCode);

        // Ordering indexes
        builder.HasIndex(d => d.SearchHitCount);
        builder.HasIndex(d => d.AverageCostPerDay);
        builder.HasIndex(d => d.CountryId);
    }
}

// ─── WishlistCollaborator ─────────────────────────────────────────────────────

public class WishlistCollaboratorConfiguration : IEntityTypeConfiguration<WishlistCollaborator>
{
    public void Configure(EntityTypeBuilder<WishlistCollaborator> builder)
    {
        builder.HasKey(c => c.WishlistCollaboratorId);

        // A user can only appear once per wishlist
        builder.HasIndex(c => new { c.WishlistId, c.UserId }).IsUnique();
    }
}

// ─── Creator ──────────────────────────────────────────────────────────────────

public class CreatorConfiguration : IEntityTypeConfiguration<Creator>
{
    public void Configure(EntityTypeBuilder<Creator> builder)
    {
        builder.HasKey(c => c.CreatorId);

        // ContactEmail is internal-only; enforce max length and index for lookups
        builder.Property(c => c.ContactEmail).HasMaxLength(320);
        builder.HasIndex(c => c.ContactEmail);

        // Platform + handle combination should be unique — the same creator
        // on the same platform cannot be registered twice.
        builder.HasIndex(c => new { c.PlatformName, c.Handle }).IsUnique();

        builder.HasIndex(c => c.IsVerified);
    }
}

// ─── WishlistCreatorAttribution ───────────────────────────────────────────────

public class WishlistCreatorAttributionConfiguration
    : IEntityTypeConfiguration<WishlistCreatorAttribution>
{
    public void Configure(EntityTypeBuilder<WishlistCreatorAttribution> builder)
    {
        builder.HasKey(a => a.WishlistCreatorAttributionId);

        // A specific creator can only be attributed to a specific wishlist once.
        builder.HasIndex(a => new { a.WishlistId, a.CreatorId }).IsUnique();

        // Wishlist → many attributions.
        // Use NoAction on wishlist delete to prevent cascade conflict with other
        // FK chains on the Wishlist table (mirrors the ForkedFrom pattern).
        builder.HasOne(a => a.Wishlist)
               .WithMany(w => w.CreatorAttributions)
               .HasForeignKey(a => a.WishlistId)
               .OnDelete(DeleteBehavior.NoAction);

        // Creator → many attributions. Cascade is fine here — if a Creator
        // record is deleted (rare), remove all their attributions.
        builder.HasOne(a => a.Creator)
               .WithMany(c => c.WishlistAttributions)
               .HasForeignKey(a => a.CreatorId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.IsActive);
        builder.HasIndex(a => a.PermissionGrantedAt);
    }
}