using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MovieDate.Tests;

/// <summary>Helpers for tests that need an anonymous or a signed-in (cookie) client.</summary>
internal static class TestUsers
{
    public const string Password = "password1";

    private record Credentials(string Email, string Password);

    // Each client gets its own cookie container; redirects are not followed so a 302 would surface as-is.
    public static HttpClient NewClient(PostgresAppFixture factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    // The Postgres container is shared across the collection, so emails must be unique per test.
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>Registers a fresh user and signs in with a cookie; returns the authenticated client.</summary>
    public static async Task<HttpClient> NewSignedInClient(PostgresAppFixture factory)
    {
        var client = NewClient(factory);
        var credentials = new Credentials(UniqueEmail(), Password);

        var register = await client.PostAsJsonAsync("/api/auth/register", credentials);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }
}
