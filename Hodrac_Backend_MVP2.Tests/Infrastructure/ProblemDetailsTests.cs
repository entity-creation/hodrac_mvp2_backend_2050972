using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Infrastucture.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Infrastructure;

/// <summary>
/// Error responses are RFC 7807 ProblemDetails (status, title, detail,
/// traceId + an "error" extension with the message); validation problems
/// and non-JSON endpoints (CSV, share HTML) are unchanged.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ProblemDetailsTests
{
    private readonly HodracApiFactory _factory;

    public ProblemDetailsTests(HodracApiFactory factory) => _factory = factory;

    private static DateOnly Day(int daysFromNow) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow));

    private async Task<(HttpClient Client, TestUser User)> UserAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    private static object CommunityTripBody(object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["freeTextDestination"] = $"Problem-{Guid.NewGuid():N}",
            ["startDate"] = Day(60),
            ["endDate"] = Day(65),
            ["isDateFlexible"] = false,
            ["caption"] = "Weekend away",
            ["maxGroupSize"] = 6,
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return body;
    }

    /// <summary>Asserts a problem+json body with the given status and message (detail and error).</summary>
    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? message)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json", text);
        var body = JsonDocument.Parse(text).RootElement.Clone();
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        body.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        if (message is not null)
        {
            body.GetProperty("detail").GetString().Should().Be(message);
            body.GetProperty("error").GetString().Should().Be(message);
        }
        return body;
    }

    [Fact]
    public async Task Bare_string_error_becomes_problem_details()
    {
        var (client, _) = await UserAsync("Problem Author");

        var response = await client.PostAsJsonAsync("/api/trips", CommunityTripBody(new { pricePerSeatKobo = 100_000L }));

        var body = await ProblemAsync(response, HttpStatusCode.BadRequest, "Price and payments are only for hosted trips");
        body.GetProperty("title").GetString().Should().Be("Bad Request");
    }

    [Fact]
    public async Task Error_object_becomes_problem_details_with_error_extension()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Problem Duplicate");

        var duplicate = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            email = user.Email,
            password = AuthHelper.DefaultPassword,
            displayName = "Again",
        });
        var body = await ProblemAsync(duplicate, HttpStatusCode.Conflict, "An account with this email already exists.");
        body.GetProperty("title").GetString().Should().Be("Conflict");

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "wrong-password-1" });
        await ProblemAsync(login, HttpStatusCode.Unauthorized, "Invalid email or password.");
    }

    [Fact]
    public async Task Not_found_with_a_message_and_empty_not_found_are_problem_details()
    {
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var client = _factory.CreateClient().Authenticated(admin);

        var unknownJob = await client.GetAsync("/api/admin/insights/rebuild/no-such-job");
        await ProblemAsync(unknownJob, HttpStatusCode.NotFound,
            "Unknown rebuild job (job state is in-memory and lost on restart).");

        var missingTrip = await client.GetAsync($"/api/trips/{Guid.NewGuid()}");
        var empty = await ProblemAsync(missingTrip, HttpStatusCode.NotFound, message: null);
        empty.GetProperty("title").GetString().Should().Be("Not Found");
    }

    [Fact]
    public async Task Forbid_is_problem_details()
    {
        var (author, _) = await UserAsync("Forbid Author");
        var created = await author.PostAsJsonAsync("/api/trips", CommunityTripBody());
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var tripId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        var (other, _) = await UserAsync("Forbid Other");

        var response = await other.PatchAsJsonAsync($"/api/trips/{tripId}", new { caption = "Mine now" });

        await ProblemAsync(response, HttpStatusCode.Forbidden, message: null);
    }

    [Fact]
    public async Task Validation_problem_is_unchanged()
    {
        var (client, _) = await UserAsync("Validation Author");

        var response = await client.PostAsJsonAsync("/api/trips", new { isDateFlexible = false });

        var body = await ProblemAsync(response, HttpStatusCode.BadRequest, message: null);
        body.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        body.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        body.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Csv_and_share_html_endpoints_are_unchanged()
    {
        var (host, _) = await UserAsync("Csv Host");
        await CreatePendingHostAsync(host, "Csv Host");
        var created = await host.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = $"Csv-{Guid.NewGuid():N}",
            startDate = Day(90),
            endDate = Day(95),
            isDateFlexible = false,
            caption = "Csv trip",
            maxGroupSize = 6,
            isHostedTrip = true,
            pricePerSeatKobo = 10_000_000L,
            depositKobo = 10_000_000L,
            termsText = "No refunds.",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var tripId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        var csv = await host.GetAsync($"/api/host/trips/{tripId}/manifest.csv");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync()).Should().Contain("name,email,phone");

        var anonymous = _factory.CreateClient();
        var share = await anonymous.GetAsync($"/share/trip/{tripId}");
        share.StatusCode.Should().Be(HttpStatusCode.OK);
        share.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var missing = await anonymous.GetAsync($"/share/trip/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await missing.Content.ReadAsStringAsync()).Should().Contain("<html");
    }

    // ── The filter itself (anonymous error objects with extra properties) ──

    private async Task<ObjectResult> RunFilterAsync(IActionResult result)
    {
        var services = _factory.Services;
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object());
        var filter = new ErrorProblemDetailsFilter(
            services.GetRequiredService<ProblemDetailsFactory>(),
            services.GetRequiredService<IOptions<JsonOptions>>());

        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(actionContext, new List<IFilterMetadata>(), context.Result, new object())));
        return (ObjectResult)context.Result;
    }

    [Fact]
    public async Task Filter_keeps_extra_properties_of_error_objects_as_extensions()
    {
        var job = new { id = "abc", status = "running" };
        var result = await RunFilterAsync(new ConflictObjectResult(new
        {
            error = "Destination is in use.",
            wishlistCount = 3,
            TripPostCount = 1,
            code = "IN_USE",
            job,
        }));

        result.StatusCode.Should().Be(409);
        result.ContentTypes.Should().Contain("application/problem+json");
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(409);
        problem.Title.Should().Be("Conflict");
        problem.Detail.Should().Be("Destination is in use.");
        problem.Extensions["error"].Should().Be("Destination is in use.");
        problem.Extensions["wishlistCount"].Should().Be(3);
        problem.Extensions["tripPostCount"].Should().Be(1);
        problem.Extensions["code"].Should().Be("IN_USE");
        problem.Extensions["job"].Should().BeSameAs(job);
        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task Filter_converts_message_objects_and_503s_but_leaves_other_values()
    {
        var message = await RunFilterAsync(new NotFoundObjectResult(new { message = "No active pricing snapshot." }));
        var problem = message.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Be("No active pricing snapshot.");
        problem.Extensions["error"].Should().Be("No active pricing snapshot.");

        var unavailable = await RunFilterAsync(new ObjectResult(new { error = "Mongo is down." }) { StatusCode = 503 });
        var p503 = unavailable.Value.Should().BeOfType<ProblemDetails>().Subject;
        p503.Status.Should().Be(503);
        p503.Title.Should().Be("Service Unavailable");
        p503.Detail.Should().Be("Mongo is down.");

        var validation = new ValidationProblemDetails(new Dictionary<string, string[]> { ["x"] = new[] { "bad" } });
        (await RunFilterAsync(new BadRequestObjectResult(validation))).Value.Should().BeSameAs(validation);

        var dto = new List<string> { "a" };
        (await RunFilterAsync(new BadRequestObjectResult(dto))).Value.Should().BeSameAs(dto);

        var ok = new { error = "not an error status" };
        (await RunFilterAsync(new OkObjectResult(ok))).Value.Should().BeSameAs(ok);
    }
}
