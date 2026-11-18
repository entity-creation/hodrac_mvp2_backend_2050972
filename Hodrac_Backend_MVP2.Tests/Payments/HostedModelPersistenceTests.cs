using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>HodracHostV1 schema: round-trips, unique keys and delete rules on the real database.</summary>
[Collection(IntegrationCollection.Name)]
public class HostedModelPersistenceTests
{
    private readonly HodracApiFactory _factory;

    public HostedModelPersistenceTests(HodracApiFactory factory) => _factory = factory;

    private HodracDbContext NewDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<HodracDbContext>();

    private static async Task<(HostProfile host, TripPost trip, TripInterest interest)> SeedHostedTripAsync(HodracDbContext db)
    {
        var host = new HostProfile
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Lagos Trippers",
            Slug = $"lagos-trippers-{Guid.NewGuid():N}"[..40],
            PastTripProofUrls = new List<string> { "https://instagram.com/p/abc", "https://example.com/trip" },
        };
        var trip = new TripPost
        {
            AuthorUserId = host.UserId,
            Caption = "Zanzibar",
            IsHostedTrip = true,
            HostProfile = host,
            IsListed = false,
            PricePerSeatKobo = 50_000_000,
            DepositKobo = 10_000_000,
            Instalments =
            {
                new TripInstalment { Sequence = 1, AmountKobo = 20_000_000, DueDate = new DateOnly(2027, 2, 1) },
                new TripInstalment { Sequence = 2, AmountKobo = 20_000_000, DueDate = new DateOnly(2027, 3, 1) },
            },
            FeeMode = FeeMode.HostAbsorbs,
            TermsText = "No refunds within 14 days of departure.",
        };
        var interest = new TripInterest { TripPost = trip, RequesterUserId = Guid.NewGuid(), Status = TripInterestStatus.Approved, PaymentStatus = InterestPaymentStatus.AwaitingDeposit };
        db.AddRange(host, trip, interest);
        await db.SaveChangesAsync();
        return (host, trip, interest);
    }

    [Fact]
    public async Task Hosted_trip_instalments_enums_and_jsonb_round_trip()
    {
        Guid tripId, hostId;
        using (var scope = _factory.Services.CreateScope())
        {
            var (host, trip, _) = await SeedHostedTripAsync(NewDb(scope));
            tripId = trip.Id;
            hostId = host.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = NewDb(scope);
            var trip = await db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId);
            trip.IsListed.Should().BeFalse();
            trip.FeeMode.Should().Be(FeeMode.HostAbsorbs);
            trip.Instalments.OrderBy(i => i.Sequence).Select(i => (i.Sequence, i.AmountKobo, i.DueDate))
                .Should().Equal((1, 20_000_000L, new DateOnly(2027, 2, 1)), (2, 20_000_000L, new DateOnly(2027, 3, 1)));
            InstalmentValidator.Validate(trip.PricePerSeatKobo, trip.DepositKobo, trip.Instalments, null).Should().BeEmpty();

            var host = await db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId);
            host.PastTripProofUrls.Should().Equal("https://instagram.com/p/abc", "https://example.com/trip");
            host.VerificationStatus.Should().Be(HostVerificationStatus.Unverified);
            host.Plan.Should().Be(HostPlan.Host);

            // Enums are stored as strings.
            var stored = await db.Database
                .SqlQuery<string>($"SELECT \"FeeMode\" AS \"Value\" FROM \"TripPosts\" WHERE \"Id\" = {tripId}")
                .SingleAsync();
            stored.Should().Be("HostAbsorbs");
        }
    }

    [Fact]
    public async Task Reminder_log_is_unique_per_interest_sequence_and_kind_with_null_sequence_treated_as_equal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var (_, _, interest) = await SeedHostedTripAsync(db);

        db.PaymentReminderLogs.Add(new PaymentReminderLog { TripInterestId = interest.Id, InstalmentSequence = null, Kind = PaymentReminderKind.SeatHoldExpiring });
        db.PaymentReminderLogs.Add(new PaymentReminderLog { TripInterestId = interest.Id, InstalmentSequence = 1, Kind = PaymentReminderKind.DueIn3Days });
        db.PaymentReminderLogs.Add(new PaymentReminderLog { TripInterestId = interest.Id, InstalmentSequence = 2, Kind = PaymentReminderKind.DueIn3Days });
        await db.SaveChangesAsync();

        db.PaymentReminderLogs.Add(new PaymentReminderLog { TripInterestId = interest.Id, InstalmentSequence = null, Kind = PaymentReminderKind.SeatHoldExpiring });
        var dupNull = () => db.SaveChangesAsync();
        await dupNull.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();

        db.PaymentReminderLogs.Add(new PaymentReminderLog { TripInterestId = interest.Id, InstalmentSequence = 1, Kind = PaymentReminderKind.DueIn3Days });
        var dup = () => db.SaveChangesAsync();
        await dup.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Seat_payment_reference_is_unique_and_blocks_deleting_the_trip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var (_, trip, interest) = await SeedHostedTripAsync(db);
        var reference = PaymentReference.New();

        SeatPayment Payment() => new()
        {
            TripPostId = trip.Id,
            TripInterestId = interest.Id,
            PayerUserId = interest.RequesterUserId,
            Reference = reference,
            Kind = SeatPaymentKind.Deposit,
            SeatAmountKobo = 10_000_000,
            HodracFeeKobo = 250_000,
            TotalChargedKobo = 10_000_000,
            VerifyResponseJson = "{\"status\":true}",
        };

        db.SeatPayments.Add(Payment());
        await db.SaveChangesAsync();

        db.SeatPayments.Add(Payment());
        var dup = () => db.SaveChangesAsync();
        await dup.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();

        // Money records are never cascade-deleted with the trip.
        var delete = () => db.TripPosts.Where(t => t.Id == trip.Id).ExecuteDeleteAsync();
        await delete.Should().ThrowAsync<Exception>();
        (await db.SeatPayments.CountAsync(p => p.Reference == reference)).Should().Be(1);
    }
}
