using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>
/// Setup helpers for the flexible-instalment and traveller-removal tests
/// (same flow as HostManifestTests / PaymentFlowTests: verified host, hosted
/// trip, request, approve, accept terms, deposit via the signed webhook).
/// </summary>
public class HostedFlow
{
    public const long Price = 25_000_000;   // ₦250,000
    public const long Deposit = 5_000_000;  // ₦50,000
    public const long Instalment = 10_000_000;

    public record Seat(
        HttpClient Host, TestUser HostUser, Guid HostId,
        HttpClient Traveller, TestUser TravellerUser,
        Guid TripId, Guid InterestId);

    private readonly HodracApiFactory _factory;

    public HostedFlow(HodracApiFactory factory) => _factory = factory;

    public RecordingEmailSender Emails => _factory.Emails;

    /// <summary>A WAT date n days from today (WAT), as the server computes "today".</summary>
    public static DateOnly WatDay(int daysFromToday) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1).AddDays(daysFromToday));

    public static DateTime EightAmWat(DateOnly day) => DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Utc);

    public async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    public async Task<TResult> RunJobAsync<TJob, TResult>(Func<TJob, Task<TResult>> run) where TJob : notnull
    {
        using var scope = _factory.Services.CreateScope();
        return await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    public async Task<(HttpClient Client, TestUser User)> UserAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    public async Task<(HttpClient Client, TestUser User, Guid HostId)> HostAsync(string name)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        return (client, user, hostId);
    }

    /// <summary>HostSet: instalments due in 30 and 60 days; trip starts in 90.</summary>
    public static object HostSetTripBody() => new
    {
        freeTextDestination = $"Zanzibar-{Guid.NewGuid():N}",
        startDate = WatDay(90),
        endDate = WatDay(95),
        isDateFlexible = false,
        caption = "Zanzibar with the crew",
        maxGroupSize = 6,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = WatDay(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = WatDay(60) },
        },
        feeMode = "PassToTraveller",
        termsText = "Deposit is non-refundable.",
    };

    /// <summary>TravellerChooses: two undated instalments, pay in full 80 days from today; starts in 90.</summary>
    public static object TravellerChoosesTripBody(int payInFullInDays = 80) => new
    {
        freeTextDestination = $"Lamu-{Guid.NewGuid():N}",
        startDate = WatDay(90),
        endDate = WatDay(95),
        isDateFlexible = false,
        caption = "Lamu, your dates",
        maxGroupSize = 6,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment },
            new { sequence = 2, amountKobo = Instalment },
        },
        instalmentDatesMode = "TravellerChooses",
        payInFullByDate = WatDay(payInFullInDays),
        feeMode = "PassToTraveller",
        termsText = "Deposit is non-refundable.",
    };

    public static async Task<Guid> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> JoinAsync(HttpClient client, Guid tripId, string? contactPhone = null)
    {
        var join = await client.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please", contactPhone });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        return (await join.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task ApproveAsync(HttpClient host, Guid tripId, Guid interestId)
    {
        var response = await host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> AcceptTermsAsync(
        HttpClient traveller, Guid tripId, Guid interestId, object? instalmentDates = null, int termsVersion = 1) =>
        traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms",
            new { termsVersion, instalmentDates });

    public static object Dates(params (int Sequence, DateOnly DueDate)[] dates) =>
        dates.Select(d => new { sequence = d.Sequence, dueDate = d.DueDate }).ToArray();

    /// <summary>
    /// Approved seat with terms accepted. travellerDates: what the traveller
    /// picks at accept-terms (TravellerChooses trips; default days 20 and 50).
    /// </summary>
    public async Task<Seat> SeatAsync(object tripBody, string travellerName = "Ada Traveller",
        object? travellerDates = null, string? contactPhone = null, bool travellerChooses = false)
    {
        var (host, hostUser, hostId) = await HostAsync("Flex Host");
        var tripId = await CreateTripAsync(host, tripBody);
        return await AddSeatAsync(host, hostUser, hostId, tripId, travellerName, travellerDates, contactPhone, travellerChooses);
    }

    public async Task<Seat> AddSeatAsync(HttpClient host, TestUser hostUser, Guid hostId, Guid tripId,
        string travellerName = "Ada Traveller", object? travellerDates = null, string? contactPhone = null,
        bool travellerChooses = false)
    {
        var (traveller, travellerUser) = await UserAsync(travellerName);
        var interestId = await JoinAsync(traveller, tripId, contactPhone);
        await ApproveAsync(host, tripId, interestId);
        var dates = travellerDates ?? (travellerChooses ? Dates((1, WatDay(20)), (2, WatDay(50))) : null);
        var accept = await AcceptTermsAsync(traveller, tripId, interestId, dates);
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return new Seat(host, hostUser, hostId, traveller, travellerUser, tripId, interestId);
    }

    public async Task<(string Reference, long Total)> CheckoutAsync(Seat seat, string kind = "Deposit", int? sequence = null)
    {
        var response = await seat.Traveller.PostAsJsonAsync(
            $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind, instalmentSequence = sequence });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.ReadJsonAsync();
        return (json.GetProperty("reference").GetString()!, json.GetProperty("totalKobo").GetInt64());
    }

    public async Task PostChargeSuccessAsync(string reference, long amount)
    {
        var body = $"{{\"event\":\"charge.success\",\"data\":{{\"id\":4000000001,\"status\":\"success\",\"reference\":\"{reference}\",\"amount\":{amount},\"currency\":\"NGN\"}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paystack/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PaystackSignature.HeaderName,
            PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey));
        var response = await _factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Deposit checkout + signed charge.success webhook (joins the trip chat).</summary>
    public async Task PayDepositAsync(Seat seat)
    {
        var (reference, total) = await CheckoutAsync(seat);
        await PostChargeSuccessAsync(reference, total);
    }

    public static async Task RecordOkAsync(HttpClient host, Guid tripId, Guid interestId, long amountKobo, string method = "Transfer")
    {
        var response = await host.PostAsJsonAsync($"/api/host/trips/{tripId}/manual-payments", new { interestId, amountKobo, method });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    public Task<TripInterest> InterestAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == interestId));

    public Task<List<TripInterestInstalmentDate>> TravellerDatesAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterestInstalmentDates.AsNoTracking()
            .Where(d => d.TripInterestId == interestId).OrderBy(d => d.Sequence).ToListAsync());

    public Task<bool> InThreadAsync(Guid tripId, string userId) => WithDbAsync(db =>
        db.TripThreadParticipants.AnyAsync(p => p.UserId == Guid.Parse(userId)
            && db.TripThreads.Any(t => t.Id == p.TripThreadId && t.TripPostId == tripId)));

    public Task<int> NotificationCountAsync(Guid tripId, NotificationType type, string recipientUserId) =>
        WithDbAsync(db => db.Notifications.CountAsync(n => n.TripPostId == tripId && n.Type == type
            && n.RecipientUserId == Guid.Parse(recipientUserId)));

    public List<RecordingEmailSender.SentEmail> EmailsTo(string email, string? subjectContains = null) =>
        Emails.Sent.Where(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase)
            && (subjectContains == null || e.Subject.Contains(subjectContains, StringComparison.Ordinal))).ToList();

    /// <summary>Moves a HostSet trip's instalment due date (backdating for overdue tests).</summary>
    public Task SetHostDueDateAsync(Guid tripId, int sequence, DateOnly due) =>
        WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"TripInstalments\" SET \"DueDate\" = {due} WHERE \"TripPostId\" = {tripId} AND \"Sequence\" = {sequence}"));

    /// <summary>Moves a traveller's chosen date (backdating for overdue tests).</summary>
    public Task SetTravellerDueDateAsync(Guid interestId, int sequence, DateOnly due) =>
        WithDbAsync(db => db.TripInterestInstalmentDates
            .Where(d => d.TripInterestId == interestId && d.Sequence == sequence)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DueDate, due)));
}
