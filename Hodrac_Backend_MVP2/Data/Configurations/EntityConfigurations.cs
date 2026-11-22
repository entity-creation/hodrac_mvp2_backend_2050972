using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Reflection.Emit;
using System.Text.Json;

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

        // Public share links: token lookups must be fast and unambiguous.
        builder.Property(w => w.ShareToken).HasMaxLength(64);
        builder.HasIndex(w => w.ShareToken)
               .IsUnique()
               .HasFilter("\"ShareToken\" IS NOT NULL");
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


public class TripPostConfiguration : IEntityTypeConfiguration<TripPost>
{
    public void Configure(EntityTypeBuilder<TripPost> builder)
    {
        builder.HasOne(t => t.Destination)
            .WithMany()
            .HasForeignKey(t => t.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Wishlist)
            .WithMany()
            .HasForeignKey(t => t.WishlistId)
            .OnDelete(DeleteBehavior.Restrict);

        // DestinationId and WishlistId can't BOTH be set.
        // Either can be null for freeform trips.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TripPost_NotBothDestinationAndWishlist",
            "NOT (\"DestinationId\" IS NOT NULL AND \"WishlistId\" IS NOT NULL)"));

        builder.HasIndex(t => new { t.DestinationId, t.StartDate });

        builder.HasIndex(t => new { t.WishlistId, t.StartDate });

        builder.Property(t => t.HiddenReason)
            .HasMaxLength(TripPost.MaxHiddenReasonLength);

        // Browse/public listings filter on Status = Open AND NOT IsHidden and
        // order by StartDate.
        builder.HasIndex(t => new { t.Status, t.IsHidden, t.StartDate });

        builder.HasMany(t => t.Interests)
            .WithOne(i => i.TripPost!)
            .HasForeignKey(i => i.TripPostId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Hodrac Host ──
        // A host profile can't be deleted while it has hosted trips.
        builder.HasOne(t => t.HostProfile)
            .WithMany(h => h.Trips)
            .HasForeignKey(t => t.HostProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sentinel true: EF omits the column (DB default true) only when the
        // value is true, so an explicit false (private hosted trip) is written.
        // The DB default also lists every pre-existing trip.
        builder.Property(t => t.IsListed).HasDefaultValue(true).HasSentinel(true);

        builder.Property(t => t.Currency)
            .HasMaxLength(TripPost.CurrencyLength)
            .HasDefaultValue(TripPost.DefaultCurrency);

        builder.Property(t => t.FeeMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(FeeMode.PassToTraveller);

        builder.Property(t => t.SeatHoldHours).HasDefaultValue(TripPost.DefaultSeatHoldHours);
        builder.Property(t => t.TermsVersion).HasDefaultValue(1);

        builder.OwnsMany(t => t.Instalments, i =>
        {
            i.ToTable("TripInstalments");
            i.WithOwner().HasForeignKey("TripPostId");
            i.HasKey("TripPostId", nameof(TripInstalment.Sequence));
            i.Property(x => x.Sequence).ValueGeneratedNever();
        });

        builder.HasIndex(t => t.HostProfileId);
        builder.HasIndex(t => new { t.IsHostedTrip, t.Status });
    }
}


public class TripInterestConfiguration : IEntityTypeConfiguration<TripInterest>
{
    public void Configure(EntityTypeBuilder<TripInterest> builder)
    {
        builder.HasIndex(i => new
        {
            i.TripPostId,
            i.RequesterUserId
        })
        .IsUnique();

        builder.HasIndex(i => new
        {
            i.TripPostId,
            i.Status
        });

        // ── Hodrac Host ──
        // DB default NotRequired: every pre-existing interest is a community-trip interest.
        builder.Property(i => i.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(InterestPaymentStatus.NotRequired);

        builder.Property(i => i.TermsTextSha256).HasMaxLength(TripInterest.TermsTextSha256Length);
        builder.Property(i => i.TermsAcceptedIp).HasMaxLength(TripInterest.MaxTermsAcceptedIpLength);
        builder.Property(i => i.TermsAcceptedUserAgent).HasMaxLength(TripInterest.MaxTermsAcceptedUserAgentLength);
        builder.Property(i => i.ContactPhone).HasMaxLength(TripInterest.MaxContactPhoneLength);

        // Seat-hold expiry job: AwaitingDeposit past SeatHoldExpiresAt.
        builder.HasIndex(i => new { i.PaymentStatus, i.SeatHoldExpiresAt });
    }
}


public class TripThreadConfiguration : IEntityTypeConfiguration<TripThread>
{
    public void Configure(EntityTypeBuilder<TripThread> builder)
    {
        builder.HasIndex(t => t.TripPostId)
            .IsUnique();

        builder.HasMany(t => t.Participants)
            .WithOne()
            .HasForeignKey(p => p.TripThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Messages)
            .WithOne()
            .HasForeignKey(m => m.TripThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


public class TripThreadParticipantConfiguration
    : IEntityTypeConfiguration<TripThreadParticipant>
{
    public void Configure(EntityTypeBuilder<TripThreadParticipant> builder)
    {
        builder.HasIndex(p => new
        {
            p.TripThreadId,
            p.UserId
        })
        .IsUnique();
    }
}


public class NotificationConfiguration
    : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasIndex(n => new
        {
            n.RecipientUserId,
            n.IsRead
        });

        // Email digest: unread, not-yet-emailed per recipient.
        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.EmailedAt });
    }
}

public class NewsLetterEmailConfiguration : IEntityTypeConfiguration<NewsLetterEmail>
{
    public void Configure(EntityTypeBuilder<NewsLetterEmail> builder)
    {
        builder.HasKey(n => n.EmailId);
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
// ─── Partners ─────────────────────────────────────────────────────────────────

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.HasKey(p => p.PartnerId);

        builder.HasMany(p => p.Links)
            .WithOne(l => l.Partner)
            .HasForeignKey(l => l.PartnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PartnerLinkConfiguration : IEntityTypeConfiguration<PartnerLink>
{
    public void Configure(EntityTypeBuilder<PartnerLink> builder)
    {
        builder.HasKey(l => l.LinkId);

        builder.HasOne(l => l.Destination)
            .WithMany()
            .HasForeignKey(l => l.DestinationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Wishlist)
            .WithMany()
            .HasForeignKey(l => l.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);

        // Exactly one target per link.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PartnerLink_ExactlyOneTarget",
            "(\"DestinationId\" IS NULL) <> (\"WishlistId\" IS NULL)"));

        builder.HasIndex(l => new { l.DestinationId, l.IsActive });
        builder.HasIndex(l => new { l.WishlistId, l.IsActive });

        // Sentinel true: EF only omits the column (letting the DB default apply)
        // when the value is true, so an explicit false is always written.
        builder.Property(l => l.AppendUtm).HasDefaultValue(true).HasSentinel(true);

        builder.HasMany(l => l.Clicks)
            .WithOne(c => c.Link)
            .HasForeignKey(c => c.LinkId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PartnerLinkClickConfiguration : IEntityTypeConfiguration<PartnerLinkClick>
{
    public void Configure(EntityTypeBuilder<PartnerLinkClick> builder)
    {
        builder.HasIndex(c => new { c.LinkId, c.ClickedAt });
        builder.HasIndex(c => new { c.LinkId, c.IsBot, c.ClickedAt });

        builder.Property(c => c.Source).HasDefaultValue(PartnerClickSources.Unknown);
        builder.Property(c => c.IsBot).HasDefaultValue(false);
    }
}

public class HeroWishlistSettingConfiguration : IEntityTypeConfiguration<HeroWishlistSetting>
{
    public void Configure(EntityTypeBuilder<HeroWishlistSetting> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Single row only.
        builder.ToTable(t => t.HasCheckConstraint("CK_HeroWishlistSetting_Singleton", "\"Id\" = 1"));

        // Deleting the wishlist clears the hero instead of blocking the delete.
        builder.HasOne(s => s.HeroWishlist)
            .WithMany()
            .HasForeignKey(s => s.HeroWishlistId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PartnerLeadConfiguration : IEntityTypeConfiguration<PartnerLead>
{
    public void Configure(EntityTypeBuilder<PartnerLead> builder)
    {
        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => new { l.Status, l.CreatedAt });
        builder.Property(l => l.Status).HasDefaultValue(PartnerLeadStatuses.New);
    }
}

// ─── Hodrac Host ──────────────────────────────────────────────────────────────
// Money tables (SeatPayment, ManualPayment, OperatorPlanPayment) are never
// cascade-deleted: their parents use Restrict so a trip/interest/host delete
// fails instead of silently erasing payment history.

public class HostProfileConfiguration : IEntityTypeConfiguration<HostProfile>
{
    public void Configure(EntityTypeBuilder<HostProfile> builder)
    {
        builder.HasKey(h => h.Id);

        builder.HasIndex(h => h.UserId).IsUnique();
        builder.HasIndex(h => h.Slug).IsUnique();
        builder.HasIndex(h => h.VerificationStatus);

        builder.Property(h => h.DisplayName).HasMaxLength(HostProfile.MaxDisplayNameLength).IsRequired();
        builder.Property(h => h.Slug).HasMaxLength(HostProfile.MaxSlugLength).IsRequired();
        builder.Property(h => h.Bio).HasMaxLength(HostProfile.MaxBioLength);
        builder.Property(h => h.LogoUrl).HasMaxLength(HostProfile.MaxUrlLength);
        builder.Property(h => h.InstagramHandle).HasMaxLength(HostProfile.MaxInstagramHandleLength);
        builder.Property(h => h.WhatsAppNumber).HasMaxLength(HostProfile.MaxWhatsAppNumberLength);

        builder.Property(h => h.VerificationStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(HostVerificationStatus.Unverified);
        builder.Property(h => h.RejectionReason).HasMaxLength(HostProfile.MaxRejectionReasonLength);
        builder.Property(h => h.CacNumber).HasMaxLength(HostProfile.MaxCacNumberLength);
        builder.Property(h => h.IdDocumentStorageKey).HasMaxLength(HostProfile.MaxStorageKeyLength);

        // jsonb string array.
        builder.Property(h => h.PastTripProofUrls)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    v => v.ToList()));

        builder.Property(h => h.PaystackSubaccountCode).HasMaxLength(HostProfile.MaxSubaccountCodeLength);
        builder.Property(h => h.SettlementBankCode).HasMaxLength(HostProfile.MaxBankCodeLength);
        builder.Property(h => h.SettlementBankName).HasMaxLength(HostProfile.MaxBankNameLength);
        builder.Property(h => h.SettlementAccountName).HasMaxLength(HostProfile.MaxAccountNameLength);
        builder.Property(h => h.SettlementAccountLast4).HasMaxLength(HostProfile.AccountLast4Length);
        builder.Property(h => h.ClosedReason).HasMaxLength(HostProfile.MaxClosedReasonLength);

        builder.Property(h => h.Plan)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(HostPlan.Host);

        builder.HasMany(h => h.Documents)
            .WithOne(d => d.HostProfile)
            .HasForeignKey(d => d.HostProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(h => h.OperatorPlanPayments)
            .WithOne(p => p.HostProfile)
            .HasForeignKey(p => p.HostProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class HostDocumentConfiguration : IEntityTypeConfiguration<HostDocument>
{
    public void Configure(EntityTypeBuilder<HostDocument> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Kind).HasMaxLength(HostDocument.MaxKindLength).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(HostDocument.MaxFileNameLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(HostDocument.MaxContentTypeLength).IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(HostDocument.Sha256Length).IsRequired();
        builder.Property(d => d.Content).HasColumnType("bytea").IsRequired();

        builder.HasIndex(d => new { d.HostProfileId, d.Kind });
        builder.HasIndex(d => d.UploadedAt);
    }
}

public class HostReportLogConfiguration : IEntityTypeConfiguration<HostReportLog>
{
    public void Configure(EntityTypeBuilder<HostReportLog> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasOne(r => r.HostProfile)
            .WithMany()
            .HasForeignKey(r => r.HostProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // A week's report is emailed at most once per host (the job inserts
        // the row before sending; a unique violation means "already sent").
        builder.HasIndex(r => new { r.HostProfileId, r.WeekStart }).IsUnique();
    }
}

public class SeatPaymentConfiguration : IEntityTypeConfiguration<SeatPayment>
{
    public void Configure(EntityTypeBuilder<SeatPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Reference).HasMaxLength(SeatPayment.MaxReferenceLength).IsRequired();
        builder.HasIndex(p => p.Reference).IsUnique();

        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Channel).HasMaxLength(SeatPayment.MaxChannelLength);
        builder.Property(p => p.Currency).HasMaxLength(TripPost.CurrencyLength).IsRequired();
        builder.Property(p => p.SubaccountCode).HasMaxLength(HostProfile.MaxSubaccountCodeLength);
        builder.Property(p => p.AuthorizationUrl).HasMaxLength(SeatPayment.MaxAuthorizationUrlLength);
        builder.Property(p => p.VerifyResponseJson).HasColumnType("jsonb");

        // Never cascade-delete money records from a trip/interest delete.
        builder.HasOne(p => p.TripPost)
            .WithMany()
            .HasForeignKey(p => p.TripPostId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.TripInterest)
            .WithMany()
            .HasForeignKey(p => p.TripInterestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.TripInterestId, p.Status });
        builder.HasIndex(p => new { p.TripPostId, p.Status });
        builder.HasIndex(p => new { p.Status, p.CreatedAt });   // reconcile job
        builder.HasIndex(p => p.PayerUserId);
    }
}

public class ManualPaymentConfiguration : IEntityTypeConfiguration<ManualPayment>
{
    public void Configure(EntityTypeBuilder<ManualPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Note).HasMaxLength(ManualPayment.MaxNoteLength);

        builder.HasOne(p => p.TripInterest)
            .WithMany()
            .HasForeignKey(p => p.TripInterestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.TripInterestId);
    }
}

public class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).HasMaxLength(PaymentWebhookEvent.MaxProviderLength).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(PaymentWebhookEvent.MaxEventTypeLength).IsRequired();
        builder.Property(e => e.Reference).HasMaxLength(SeatPayment.MaxReferenceLength);
        builder.Property(e => e.PayloadSha256).HasMaxLength(PaymentWebhookEvent.PayloadSha256Length).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(e => e.PayloadSha256).IsUnique();
        builder.HasIndex(e => e.Reference);
        builder.HasIndex(e => new { e.ProcessedAt, e.ReceivedAt });
    }
}

public class PaymentReminderLogConfiguration : IEntityTypeConfiguration<PaymentReminderLog>
{
    public void Configure(EntityTypeBuilder<PaymentReminderLog> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Channel).HasMaxLength(PaymentReminderLog.MaxChannelLength).IsRequired();

        builder.HasOne(r => r.TripInterest)
            .WithMany()
            .HasForeignKey(r => r.TripInterestId)
            .OnDelete(DeleteBehavior.Cascade);

        // NULLS NOT DISTINCT (PG15+): a deposit/seat-hold reminder (null
        // sequence) of one kind is still logged only once per interest.
        builder.HasIndex(r => new { r.TripInterestId, r.InstalmentSequence, r.Kind })
            .IsUnique()
            .AreNullsDistinct(false);
    }
}

public class OperatorPlanPaymentConfiguration : IEntityTypeConfiguration<OperatorPlanPayment>
{
    public void Configure(EntityTypeBuilder<OperatorPlanPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Reference).HasMaxLength(OperatorPlanPayment.MaxReferenceLength).IsRequired();
        builder.HasIndex(p => p.Reference).IsUnique();
        builder.HasIndex(p => p.HostProfileId);
    }
}

public class TripPageViewDailyConfiguration : IEntityTypeConfiguration<TripPageViewDaily>
{
    public void Configure(EntityTypeBuilder<TripPageViewDaily> builder)
    {
        builder.HasKey(v => new { v.TripPostId, v.Date, v.Source });

        builder.Property(v => v.Source).HasMaxLength(TripPageViewDaily.MaxSourceLength);

        builder.HasOne<TripPost>()
            .WithMany()
            .HasForeignKey(v => v.TripPostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// ─── Events ───────────────────────────────────────────────────────────────────

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasIndex(e => e.StartsAt);
        builder.HasIndex(e => e.CityId);
        builder.HasIndex(e => e.DestinationId);

        // Deleting the destination keeps the event (venue just becomes unknown).
        builder.HasOne(e => e.Destination)
            .WithMany()
            .HasForeignKey(e => e.DestinationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.City)
            .WithMany()
            .HasForeignKey(e => e.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.TimeZone).HasDefaultValue(Event.DefaultTimeZone);
        builder.Property(e => e.Currency).HasDefaultValue(Event.DefaultCurrency);

        // Sentinel true: an explicit false (draft) is always written.
        builder.Property(e => e.IsPublished).HasDefaultValue(true).HasSentinel(true);
    }
}

// ─── Blog ─────────────────────────────────────────────────────────────────────
// Deleting a post removes its links and slug history. Deleting a linked
// destination / wishlist / event / tag removes just the link; deleting the
// post's city or country clears the reference.

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        builder.ToTable("BlogPosts");
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.Status, p.PublishedAt });
        builder.HasIndex(p => p.CityId);
        builder.HasIndex(p => p.CountryId);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne(p => p.City)
            .WithMany()
            .HasForeignKey(p => p.CityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Country)
            .WithMany()
            .HasForeignKey(p => p.CountryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class BlogPostDestinationConfiguration : IEntityTypeConfiguration<BlogPostDestination>
{
    public void Configure(EntityTypeBuilder<BlogPostDestination> builder)
    {
        builder.ToTable("BlogPostDestinations");
        builder.HasKey(x => new { x.BlogPostId, x.DestinationId });
        builder.HasIndex(x => x.DestinationId);

        builder.HasOne(x => x.BlogPost).WithMany(p => p.Destinations)
            .HasForeignKey(x => x.BlogPostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Destination).WithMany()
            .HasForeignKey(x => x.DestinationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlogPostWishlistConfiguration : IEntityTypeConfiguration<BlogPostWishlist>
{
    public void Configure(EntityTypeBuilder<BlogPostWishlist> builder)
    {
        builder.ToTable("BlogPostWishlists");
        builder.HasKey(x => new { x.BlogPostId, x.WishlistId });
        builder.HasIndex(x => x.WishlistId);

        builder.HasOne(x => x.BlogPost).WithMany(p => p.Wishlists)
            .HasForeignKey(x => x.BlogPostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Wishlist).WithMany()
            .HasForeignKey(x => x.WishlistId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlogPostEventConfiguration : IEntityTypeConfiguration<BlogPostEvent>
{
    public void Configure(EntityTypeBuilder<BlogPostEvent> builder)
    {
        builder.ToTable("BlogPostEvents");
        builder.HasKey(x => new { x.BlogPostId, x.EventId });
        builder.HasIndex(x => x.EventId);

        builder.HasOne(x => x.BlogPost).WithMany(p => p.Events)
            .HasForeignKey(x => x.BlogPostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Event).WithMany()
            .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlogPostTagConfiguration : IEntityTypeConfiguration<BlogPostTag>
{
    public void Configure(EntityTypeBuilder<BlogPostTag> builder)
    {
        builder.ToTable("BlogPostTags");
        builder.HasKey(x => new { x.BlogPostId, x.TagId });
        builder.HasIndex(x => x.TagId);

        builder.HasOne(x => x.BlogPost).WithMany(p => p.Tags)
            .HasForeignKey(x => x.BlogPostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Tag).WithMany()
            .HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlogPostSlugHistoryConfiguration : IEntityTypeConfiguration<BlogPostSlugHistory>
{
    public void Configure(EntityTypeBuilder<BlogPostSlugHistory> builder)
    {
        builder.ToTable("BlogPostSlugHistory");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OldSlug).IsUnique();
        builder.HasIndex(x => x.BlogPostId);

        builder.HasOne(x => x.BlogPost).WithMany(p => p.SlugHistory)
            .HasForeignKey(x => x.BlogPostId).OnDelete(DeleteBehavior.Cascade);
    }
}
