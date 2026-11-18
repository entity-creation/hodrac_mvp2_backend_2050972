using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Hodrac_Backend_MVP2.Tests.Infrastructure;

public record TestUser(string UserId, string Email, string Password, string AccessToken, string RefreshToken);

/// <summary>Registers / logs in users through the real auth endpoints.</summary>
public static class AuthHelper
{
    public const string DefaultPassword = "testpass123";

    private record UserDtoBody(string UserId, string Email);
    private record AuthBody(string AccessToken, string RefreshToken, int ExpiresIn, UserDtoBody User);

    public static async Task<TestUser> RegisterAsync(HttpClient client, string? displayName = null, string? email = null)
    {
        email ??= $"test-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = DefaultPassword,
            displayName = displayName ?? "Test User",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<AuthBody>())!;
        return new TestUser(body.User.UserId, email, DefaultPassword, body.AccessToken, body.RefreshToken);
    }

    public static async Task<TestUser> LoginAsync(HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<AuthBody>())!;
        return new TestUser(body.User.UserId, email, password, body.AccessToken, body.RefreshToken);
    }

    private static readonly SemaphoreSlim FixedUsersLock = new(1, 1);
    private static readonly Dictionary<string, TestUser> FixedUsers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A user with a fixed email (e.g. an admin from Admin:Emails), registered once
    /// per test run and reused (logs in if the account already exists).
    /// </summary>
    public static async Task<TestUser> GetOrRegisterAsync(HttpClient client, string email, string? displayName = null)
    {
        await FixedUsersLock.WaitAsync();
        try
        {
            if (FixedUsers.TryGetValue(email, out var existing)) return existing;
            var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                email,
                password = DefaultPassword,
                displayName = displayName ?? "Test Admin",
            });
            TestUser user;
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var body = (await response.Content.ReadFromJsonAsync<AuthBody>())!;
                user = new TestUser(body.User.UserId, email, DefaultPassword, body.AccessToken, body.RefreshToken);
            }
            else
            {
                user = await LoginAsync(client, email);
            }
            FixedUsers[email] = user;
            return user;
        }
        finally
        {
            FixedUsersLock.Release();
        }
    }

    /// <summary>POST /api/auth/refresh with a user id and raw refresh token.</summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string userId, string refreshToken)
        => client.PostAsJsonAsync("/api/auth/refresh", new { userId, refreshToken });

    /// <summary>Sets the bearer token on this client.</summary>
    public static HttpClient Authenticated(this HttpClient client, TestUser user)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }
}
