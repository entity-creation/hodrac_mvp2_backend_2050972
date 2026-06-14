using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Data;

// ─── DbContext ────────────────────────────────────────────────────────────────
// Now inherits IdentityDbContext<ApplicationUser> instead of plain DbContext.
// This adds all ASP.NET Core Identity tables to the same PostgreSQL database.
//
// Identity tables added by EF migration:
//   AspNetUsers           ← ApplicationUser (+ our added columns)
//   AspNetRoles, AspNetUserRoles, AspNetUserClaims,
//   AspNetUserLogins, AspNetUserTokens, AspNetRoleClaims
//
// Our additions to the same database:
//   UserRefreshTokens     ← JWT refresh token rotation

public class HodracDbContext : IdentityDbContext<ApplicationUser>
{
    public HodracDbContext(DbContextOptions<HodracDbContext> options) : base(options) { }

    // ── Core platform DbSets ──────────────────────────────────────────────────

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<TransitRoute> TransitRoutes => Set<TransitRoute>();

    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<DestinationImage> DestinationImages => Set<DestinationImage>();

    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();
    public DbSet<WishlistPricingSnapshot> WishlistPricingSnapshots => Set<WishlistPricingSnapshot>();
    public DbSet<WishlistCollaborator> WishlistCollaborators => Set<WishlistCollaborator>();
    public DbSet<SavedWishlist> SavedWishlists => Set<SavedWishlist>();
    public DbSet<SavedDestination> SavedDestinations => Set<SavedDestination>();
    public DbSet<FeaturedWishlistPool> FeaturedWishlistPool => Set<FeaturedWishlistPool>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<AggregatedSearchRegistry> AggregatedSearchRegistry => Set<AggregatedSearchRegistry>();

    public DbSet<DestinationCategory> DestinationCategories => Set<DestinationCategory>();
    public DbSet<DestinationTag> DestinationTags => Set<DestinationTag>();
    public DbSet<DestinationCurrency> DestinationCurrencies => Set<DestinationCurrency>();
    public DbSet<DestinationLanguage> DestinationLanguages => Set<DestinationLanguage>();
    public DbSet<DestinationCity> DestinationCities => Set<DestinationCity>();
    public DbSet<CountryLanguage> CountryLanguages => Set<CountryLanguage>();
    public DbSet<WishlistDestination> WishlistDestinations => Set<WishlistDestination>();
    public DbSet<Creator> Creators => Set<Creator>();
    public DbSet<WishlistCreatorAttribution> WishlistCreatorAttributions => Set<WishlistCreatorAttribution>();

    // ── Identity extensions ───────────────────────────────────────────────────

    /// <summary>Custom refresh token table — not part of default Identity schema.</summary>
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    /// <summary>
    /// Direct DbSet for ApplicationUser — lets repositories use ExecuteUpdateAsync
    /// on the Identity user table without going through UserManager for bulk updates.
    /// </summary>
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

    // ── Model configuration ───────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // Must be first — registers Identity mappings
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HodracDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(o => o.UseVector());
    }
}

// ─── UserRefreshToken EF configuration ───────────────────────────────────────

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.HasKey(t => t.UserRefreshTokenId);

        builder.HasOne(t => t.User)
               .WithMany(u => u.RefreshTokens)
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.UserId, t.IsRevoked });
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.ExpiresAt);
    }
}

// ─── ApplicationUser EF configuration ────────────────────────────────────────

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasIndex(u => u.DisplayName);
        builder.HasIndex(u => u.CreatedAt);
    }
}
