using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class ProfileTests(PostgresAppFixture factory)
{
    private const string Warszawa = "Q270";
    private const string Krakow = "Q31487";

    private record CityBody(int Id, string Name, string Voivodeship);

    private record ProfileBody(string Email, CityBody? City);

    private record MeBody(string Email);

    private async Task<int> CityId(string wikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.WikidataId == wikidataId).Select(c => c.Id).SingleAsync();
    }

    private static async Task<ProfileBody> Profile(HttpClient client)
    {
        var response = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ProfileBody>()
            ?? throw new InvalidOperationException("Empty profile body.");
    }

    private static Task<HttpResponseMessage> SetCity(HttpClient client, int cityId) =>
        client.PutAsJsonAsync("/api/profile/city", new { cityId });

    [Fact]
    public async Task New_user_profile_has_email_and_no_city()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");

        var profile = await Profile(client);

        Assert.Equal(me?.Email, profile.Email);
        Assert.Null(profile.City);
    }

    [Fact]
    public async Task Set_city_rotates_concurrency_stamp()
    {
        // A stale Identity UpdateAsync (loaded before the city save) must then fail its concurrency check
        // instead of silently writing the old CityId back.
        using var client = await TestUsers.NewSignedInClient(factory);
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");

        async Task<string?> Stamp()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Users.Where(u => u.Email == me!.Email).Select(u => u.ConcurrencyStamp).SingleAsync();
        }

        var before = await Stamp();
        Assert.Equal(HttpStatusCode.OK, (await SetCity(client, await CityId(Warszawa))).StatusCode);

        Assert.NotEqual(before, await Stamp());
    }

    [Fact]
    public async Task Set_city_then_profile_shows_it()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var warszawa = await CityId(Warszawa);

        var set = await SetCity(client, warszawa);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var returned = await set.Content.ReadFromJsonAsync<ProfileBody>();
        Assert.Equal(new CityBody(warszawa, "Warszawa", "mazowieckie"), returned?.City);
        Assert.Equal(new CityBody(warszawa, "Warszawa", "mazowieckie"), (await Profile(client)).City);
    }

    [Fact]
    public async Task Change_city_replaces_it()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await SetCity(client, await CityId(Warszawa))).StatusCode);
        var krakow = await CityId(Krakow);

        var change = await SetCity(client, krakow);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(new CityBody(krakow, "Kraków", "małopolskie"), (await Profile(client)).City);
    }

    [Fact]
    public async Task Unknown_city_returns_404_and_keeps_current_city()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var warszawa = await CityId(Warszawa);
        Assert.Equal(HttpStatusCode.OK, (await SetCity(client, warszawa)).StatusCode);

        var response = await SetCity(client, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(warszawa, (await Profile(client)).City?.Id);
    }

    [Fact]
    public async Task Other_user_does_not_see_my_city()
    {
        using var alice = await TestUsers.NewSignedInClient(factory);
        using var bob = await TestUsers.NewSignedInClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await SetCity(alice, await CityId(Warszawa))).StatusCode);

        Assert.Null((await Profile(bob)).City);
    }

    [Fact]
    public async Task Profile_response_has_no_coordinates_or_population()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var put = await SetCity(client, await CityId(Warszawa));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putJson = await put.Content.ReadAsStringAsync();
        Assert.Contains("Warszawa", putJson);
        CitiesTests.AssertNoCoordinatesOrPopulation(putJson);

        var response = await client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Warszawa", json);
        CitiesTests.AssertNoCoordinatesOrPopulation(json);
    }

    [Fact]
    public async Task Profile_routes_without_session_return_401()
    {
        using var client = TestUsers.NewClient(factory);

        var get = await client.GetAsync("/api/profile");
        var put = await SetCity(client, 1);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }
}
