using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

/// <summary>A signed-in test user with a complete profile (display name + city) and their resolved AspNetUsers.Id.</summary>
internal sealed record PairUser(HttpClient Client, string Id, string DisplayName) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

/// <summary>
/// Helpers for friends/conversations tests: profile-complete users created through the API, and mirrored match
/// rows seeded straight into the database. Meant for the collection without the background recompute worker, so
/// no recompute ever adds a match the test did not seed (a silently matched "stranger" would turn a 404 into 200).
/// </summary>
internal static class TestPairs
{
    // Kraków: not reserved by MatchesTests and far from its top-up scenario (Hel), so these users never tie there.
    private const string CityWikidataId = "Q31487";

    private record MeBody(string Email);

    /// <summary>Registers and signs in a user, sets display name and city through the API, resolves the id.</summary>
    public static async Task<PairUser> NewUser(PostgresAppFixture factory, string displayName)
    {
        var client = await TestUsers.NewSignedInClient(factory);

        var name = await client.PutAsJsonAsync("/api/profile/display-name", new { displayName });
        Assert.Equal(HttpStatusCode.OK, name.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cityId = await db.Cities.Where(c => c.WikidataId == CityWikidataId).Select(c => c.Id).SingleAsync();
        var city = await client.PutAsJsonAsync("/api/profile/city", new { cityId });
        Assert.Equal(HttpStatusCode.OK, city.StatusCode);

        // Test-only lookup: the API never exposes the caller's own id, so resolve it from the session's email.
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        var id = await db.Users.Where(u => u.Email == me!.Email).Select(u => u.Id).SingleAsync();
        return new PairUser(client, id, displayName);
    }

    /// <summary>Seeds the mirrored match rows a → b and b → a, as a recompute would write them.</summary>
    public static async Task SeedMatch(PostgresAppFixture factory, PairUser a, PairUser b)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Matches.AddRange(
            new Match { UserId = a.Id, CandidateId = b.Id, Score = 50, FirstMatchedAt = now, UpdatedAt = now },
            new Match { UserId = b.Id, CandidateId = a.Id, Score = 50, FirstMatchedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    /// <summary>Two profile-complete users with mirrored match rows and no friendship yet.</summary>
    public static async Task<(PairUser A, PairUser B)> NewMatchedPair(
        PostgresAppFixture factory, string nameA, string nameB)
    {
        var a = await NewUser(factory, nameA);
        var b = await NewUser(factory, nameB);
        await SeedMatch(factory, a, b);
        return (a, b);
    }

    /// <summary>Adds <paramref name="friend"/> to <paramref name="user"/>'s friends through the API.</summary>
    public static async Task AddFriend(PairUser user, PairUser friend)
    {
        var response = await user.Client.PostAsJsonAsync("/api/friends", new { userId = friend.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
