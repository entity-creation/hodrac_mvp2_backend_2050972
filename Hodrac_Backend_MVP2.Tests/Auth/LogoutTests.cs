using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Tests.Infrastructure;

namespace Hodrac_Backend_MVP2.Tests.Auth;

/// <summary>Spec S1: logout never trusts a client-sent user id.</summary>
[Collection(IntegrationCollection.Name)]
public class LogoutTests
{
    private readonly HodracApiFactory _factory;

    public LogoutTests(HodracApiFactory factory) => _factory = factory;

    [Fact]
    public async Task User_B_cannot_revoke_user_A_tokens_by_sending_A_user_id()
    {
        var anon = _factory.CreateClient();
        var userA = await AuthHelper.RegisterAsync(anon, "User A");
        var userB = await AuthHelper.RegisterAsync(anon, "User B");

        // The old attack: A's user id in the body plus "log out everywhere".
        var clientB = _factory.CreateClient().Authenticated(userB);
        var response = await clientB.PostAsJsonAsync("/api/auth/logout", new
        {
            userId = userA.UserId,
            logoutAllDevices = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A's refresh token still works.
        (await AuthHelper.RefreshAsync(anon, userA.UserId, userA.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The logout applied to B (the authenticated caller) instead.
        (await AuthHelper.RefreshAsync(anon, userB.UserId, userB.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_user_B_cannot_revoke_user_A_refresh_token()
    {
        var anon = _factory.CreateClient();
        var userA = await AuthHelper.RegisterAsync(anon, "User A");
        var userB = await AuthHelper.RegisterAsync(anon, "User B");

        // Signed in as B, presenting A's raw token: only B's own tokens are in scope.
        var clientB = _factory.CreateClient().Authenticated(userB);
        var response = await clientB.PostAsJsonAsync("/api/auth/logout", new { refreshToken = userA.RefreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AuthHelper.RefreshAsync(anon, userA.UserId, userA.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_logout_revokes_only_the_presented_token()
    {
        var anon = _factory.CreateClient();
        var user = await AuthHelper.RegisterAsync(anon);
        var secondDevice = await AuthHelper.LoginAsync(anon, user.Email);

        (await anon.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AuthHelper.RefreshAsync(anon, user.UserId, user.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AuthHelper.RefreshAsync(anon, user.UserId, secondDevice.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_logout_validates_input_and_does_not_enumerate_tokens()
    {
        var anon = _factory.CreateClient();

        (await anon.PostAsJsonAsync("/api/auth/logout", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await anon.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "x", logoutAllDevices = true }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anon.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "not-a-real-token" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
