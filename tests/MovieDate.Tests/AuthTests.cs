using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class AuthTests(PostgresAppFixture factory)
{
    private const string Password = "password1";

    private record Credentials(string Email, string Password);

    private record MeBody(string Email);

    // Each test gets its own cookie container; redirects are not followed so a 302 would surface as-is.
    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    // The Postgres container is shared across the collection, so emails must be unique per test.
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new Credentials(email, password));

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true", new Credentials(email, password));

    [Fact]
    public async Task Register_login_then_me_returns_email()
    {
        using var client = NewClient();
        var email = UniqueEmail();

        Assert.Equal(HttpStatusCode.OK, (await Register(client, email, Password)).StatusCode);

        var login = await Login(client, email, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<MeBody>();
        Assert.Equal(email, body?.Email);
    }

    [Fact]
    public async Task Me_without_cookie_returns_401_not_redirect()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Logout_without_cookie_returns_401()
    {
        using var client = NewClient();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_then_me_returns_401()
    {
        using var client = NewClient();
        var email = UniqueEmail();
        await Register(client, email, Password);
        await Login(client, email, Password);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Register_with_7_character_password_returns_400()
    {
        using var client = NewClient();

        var response = await Register(client, UniqueEmail(), "passwor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        using var client = NewClient();
        var email = UniqueEmail();
        await Register(client, email, Password);

        var response = await Login(client, email, "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_400()
    {
        using var client = NewClient();
        var email = UniqueEmail();
        Assert.Equal(HttpStatusCode.OK, (await Register(client, email, Password)).StatusCode);

        var response = await Register(client, email, Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
