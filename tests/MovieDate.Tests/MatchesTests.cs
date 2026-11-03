using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Tests;

/// <summary>
/// End-to-end match rules through the API. The Postgres container is shared across the collection and every
/// user with a city and a display name is a candidate for everyone, so each test places its users in cities
/// no other test uses (and distance-0 ties stay inside the test), rates movies no other test uses, and asserts
/// only on users it created.
/// </summary>
[Collection(PostgresAppCollection.Name)]
public sealed class MatchesTests(PostgresAppFixture factory)
{
    // Seed cities (Wikidata QIDs), each reserved for one test below.
    private const string Zakopane = "Q144786";
    private const string NowyTarg = "Q231593";
    private const string Hel = "Q848973";
    private const string Jastarnia = "Q497154";
    private const string UstrzykiDolne = "Q1906346";
    private const string Suwalki = "Q236083";
    private const string Sejny = "Q826076";
    private const string Przemysl = "Q208473";
    private const string Sanok = "Q201729";
    private const string Krynica = "Q259497";
    private const string Leba = "Q345339";
    private const string Ustka = "Q321010";
    private const string Swinoujscie = "Q41599";

    // Hands out distinct catalog movies (from the end of the catalog) so no two tests share a movie.
    private static int _movieCursor;

    private record CityBody(int Id, string Name, string Voivodeship);

    private record MovieBody(int Id, string Title, string? OriginalTitle, int? Year);

    private record MatchBody(
        string UserId,
        string DisplayName,
        CityBody City,
        int Score,
        int SharedFavoritesCount,
        int SharedWatchlistCount,
        List<MovieBody> SharedFavorites,
        List<MovieBody> SharedWatchlist,
        DateTimeOffset FirstMatchedAt);

    private record MeBody(string Email);

    private sealed record User(HttpClient Client, string Id, string DisplayName) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    [Fact]
    public async Task Match_routes_without_session_return_401()
    {
        using var client = TestUsers.NewClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/matches/recompute", null)).StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Recompute_with_incomplete_profile_returns_409_and_writes_nothing(bool withName, bool withCity)
    {
        using var user = await NewUser(withName ? "Niepełna" : null, withCity ? Leba : null);

        var response = await Recompute(user);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("profile_incomplete", problem.RootElement.GetProperty("title").GetString());
        Assert.Empty(await List(user));
        Assert.Equal(0, await RowCount(m => m.UserId == user.Id || m.CandidateId == user.Id));
    }

    [Fact]
    public async Task Strong_match_ranks_above_closer_non_strong_users_and_favorites_need_7_on_both_sides()
    {
        var movies = await UniqueMovies(3);
        using var a = await NewUser("Ala", Zakopane);
        using var strong = await NewUser("Silny", NowyTarg);
        using var near = await NewUser("Bliski", Zakopane);
        using var lowRated = await NewUser("Niski", Zakopane);
        await Rate(a, movies[0], 7);
        await Rate(a, movies[1], 9);
        await Rate(a, movies[2], 6);
        await Rate(strong, movies[0], 7); // 7/7 counts
        await Rate(strong, movies[1], 8);
        await Rate(lowRated, movies[2], 9); // 6/9 does not
        await Rate(lowRated, movies[0], 6); // 7/6 does not

        var list = await RecomputeOk(a);

        var strongEntry = Assert.Single(list, m => m.UserId == strong.Id);
        var nearEntry = Assert.Single(list, m => m.UserId == near.Id);
        var lowEntry = Assert.Single(list, m => m.UserId == lowRated.Id);

        Assert.Equal(2, strongEntry.SharedFavoritesCount);
        Assert.Equal(0, strongEntry.SharedWatchlistCount);
        Assert.Equal(movies.Take(2).Order(), strongEntry.SharedFavorites.Select(f => f.Id).Order());
        var km = await DistanceKm(Zakopane, NowyTarg);
        Assert.Equal((int)Math.Round(MatchScoringService.Score(2, 0, km), MidpointRounding.AwayFromZero), strongEntry.Score);
        Assert.Equal("Silny", strongEntry.DisplayName);
        Assert.Equal("Nowy Targ", strongEntry.City.Name);

        Assert.Equal(0, lowEntry.SharedFavoritesCount);
        Assert.Empty(lowEntry.SharedFavorites);
        Assert.Equal(50, lowEntry.Score);
        Assert.Equal(50, nearEntry.Score);

        Assert.True(strongEntry.Score > nearEntry.Score);
        Assert.True(list.IndexOf(strongEntry) < list.IndexOf(nearEntry));
        Assert.True(list.IndexOf(strongEntry) < list.IndexOf(lowEntry));
    }

    [Fact]
    public async Task Without_strong_candidates_the_closest_users_top_the_list_up_to_5()
    {
        using var a = await NewUser("Hela", Hel);
        var sameCity = new List<User>();
        for (var i = 0; i < 4; i++)
        {
            sameCity.Add(await NewUser($"Hel {i}", Hel));
        }

        using var nearby = await NewUser("Jastarnia", Jastarnia);
        using var far = await NewUser("Daleko", UstrzykiDolne);

        try
        {
            var list = await RecomputeOk(a);

            Assert.Equal(MatchService.MinListSize, list.Count);
            Assert.All(sameCity, u => Assert.Contains(list, m => m.UserId == u.Id));
            Assert.Contains(list, m => m.UserId == nearby.Id);
            Assert.DoesNotContain(list, m => m.UserId == far.Id);
            Assert.Equal(nearby.Id, list[^1].UserId); // same-city users (score 50) rank above the farther one
            Assert.All(list, m => Assert.Equal(0, m.SharedFavoritesCount + m.SharedWatchlistCount));
        }
        finally
        {
            sameCity.ForEach(u => u.Dispose());
        }
    }

    [Fact]
    public async Task Recompute_mirrors_the_entry_with_the_same_score()
    {
        var movies = await UniqueMovies(2);
        using var a = await NewUser("Lustro A", Suwalki);
        using var b = await NewUser("Lustro B", Sejny);
        await Rate(a, movies[0], 8);
        await Rate(b, movies[0], 10);
        await AddToWatchlist(a, movies[1]);
        await AddToWatchlist(b, movies[1]);

        var aList = await RecomputeOk(a);
        var bList = await List(b);

        var aToB = Assert.Single(aList, m => m.UserId == b.Id);
        var bToA = Assert.Single(bList, m => m.UserId == a.Id);
        Assert.Equal(aToB.Score, bToA.Score);
        Assert.Equal((1, 1), (aToB.SharedFavoritesCount, aToB.SharedWatchlistCount));
        Assert.Equal((1, 1), (bToA.SharedFavoritesCount, bToA.SharedWatchlistCount));
        Assert.Equal(movies[0], Assert.Single(bToA.SharedFavorites).Id);
        Assert.Equal(movies[1], Assert.Single(bToA.SharedWatchlist).Id);
        Assert.Equal("Lustro A", bToA.DisplayName);
        Assert.Equal("Suwałki", bToA.City.Name);
        Assert.Equal("podlaskie", bToA.City.Voivodeship);
    }

    [Fact]
    public async Task Entries_are_never_removed_only_rescored_after_the_shared_movie_goes()
    {
        var movies = await UniqueMovies(1);
        using var a = await NewUser("Stały A", Przemysl);
        using var b = await NewUser("Stały B", Sanok);
        await Rate(a, movies[0], 8);
        await Rate(b, movies[0], 9);
        var before = Assert.Single(await RecomputeOk(a), m => m.UserId == b.Id);
        Assert.Equal(1, before.SharedFavoritesCount);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/watched/{movies[0]}")).StatusCode);
        var after = Assert.Single(await RecomputeOk(a), m => m.UserId == b.Id);
        var mirror = Assert.Single(await List(b), m => m.UserId == a.Id);

        Assert.True(after.Score < before.Score);
        Assert.Equal(0, after.SharedFavoritesCount);
        Assert.Empty(after.SharedFavorites);
        Assert.Equal(before.FirstMatchedAt, after.FirstMatchedAt);
        Assert.Equal(after.Score, mirror.Score);
        Assert.Equal(0, mirror.SharedFavoritesCount);
    }

    [Fact]
    public async Task Concurrent_recomputes_by_both_sides_succeed_without_duplicates()
    {
        var movies = await UniqueMovies(1);
        using var a = await NewUser("Równo A", Krynica);
        using var b = await NewUser("Równo B", Krynica);
        await Rate(a, movies[0], 7);
        await Rate(b, movies[0], 7);

        for (var round = 0; round < 5; round++)
        {
            var responses = await Task.WhenAll(Recompute(a), Recompute(b), Recompute(a), Recompute(b));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }

        Assert.Single(await List(a), m => m.UserId == b.Id);
        Assert.Single(await List(b), m => m.UserId == a.Id);
        Assert.Equal(1, await RowCount(m => m.UserId == a.Id && m.CandidateId == b.Id));
        Assert.Equal(1, await RowCount(m => m.UserId == b.Id && m.CandidateId == a.Id));
    }

    [Fact]
    public async Task Responses_expose_no_email_coordinates_or_distance()
    {
        var movies = await UniqueMovies(1);
        using var a = await NewUser("Prywatny A", Swinoujscie);
        using var b = await NewUser("Prywatny B", Swinoujscie);
        await Rate(a, movies[0], 9);
        await Rate(b, movies[0], 9);

        var recompute = await Recompute(a);
        Assert.Equal(HttpStatusCode.OK, recompute.StatusCode);
        var recomputeJson = await recompute.Content.ReadAsStringAsync();
        var listJson = await (await b.Client.GetAsync("/api/matches")).Content.ReadAsStringAsync();

        foreach (var json in new[] { recomputeJson, listJson })
        {
            Assert.Contains("Prywatny", json);
            Assert.DoesNotContain("@", json);
            CitiesTests.AssertNoCoordinatesOrPopulation(json);
            var names = PropertyNames(json);
            foreach (var forbidden in new[] { "email", "latitude", "longitude", "distance", "distanceKm" })
            {
                Assert.DoesNotContain(names, n => string.Equals(n, forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public async Task Deleting_a_candidate_account_removes_them_from_lists()
    {
        var movies = await UniqueMovies(1);
        using var a = await NewUser("Zostaje", Ustka);
        using var b = await NewUser("Odchodzi", Ustka);
        await Rate(a, movies[0], 8);
        await Rate(b, movies[0], 8);
        Assert.Contains(await RecomputeOk(a), m => m.UserId == b.Id);

        await DeleteAccount(b.Id);

        Assert.DoesNotContain(await List(a), m => m.UserId == b.Id);
        Assert.Equal(0, await RowCount(m => m.UserId == b.Id || m.CandidateId == b.Id));
    }

    [Fact]
    public async Task Recompute_for_a_deleted_account_returns_404()
    {
        // The cookie stays valid until the next security-stamp validation, so the user id still resolves.
        using var user = await NewUser("Usunięty", Leba);
        await DeleteAccount(user.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await Recompute(user)).StatusCode);
    }

    [Fact]
    public async Task List_of_a_new_user_is_empty()
    {
        using var user = await NewUser(null, null);

        Assert.Empty(await List(user));
    }

    private async Task<User> NewUser(string? displayName, string? cityWikidataId)
    {
        var client = await TestUsers.NewSignedInClient(factory);
        if (displayName is not null)
        {
            var set = await client.PutAsJsonAsync("/api/profile/display-name", new { displayName });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        if (cityWikidataId is not null)
        {
            var set = await client.PutAsJsonAsync("/api/profile/city", new { cityId = await CityId(cityWikidataId) });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        // Test-only lookup: the API never exposes the caller's own id, so resolve it from the session's email.
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.Email == me!.Email).Select(u => u.Id).SingleAsync();
        return new User(client, id, displayName ?? string.Empty);
    }

    private async Task<int> CityId(string wikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.WikidataId == wikidataId).Select(c => c.Id).SingleAsync();
    }

    private async Task<double> DistanceKm(string fromWikidataId, string toWikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = await db.Cities.SingleAsync(c => c.WikidataId == fromWikidataId);
        var to = await db.Cities.SingleAsync(c => c.WikidataId == toWikidataId);
        return MatchScoringService.DistanceKm(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
    }

    private async Task<int[]> UniqueMovies(int count)
    {
        var skip = Interlocked.Add(ref _movieCursor, count) - count;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Movies.OrderByDescending(m => m.Id).Skip(skip).Take(count).Select(m => m.Id).ToArrayAsync();
        Assert.Equal(count, ids.Length);
        return ids;
    }

    private async Task<int> RowCount(System.Linq.Expressions.Expression<Func<Match, bool>> predicate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Matches.CountAsync(predicate);
    }

    private async Task DeleteAccount(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("User not found.");
        Assert.True((await users.DeleteAsync(user)).Succeeded);
    }

    private static async Task Rate(User user, int movieId, int rating)
    {
        var response = await user.Client.PostAsJsonAsync("/api/watched", new { movieId, rating });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AddToWatchlist(User user, int movieId)
    {
        var response = await user.Client.PostAsJsonAsync("/api/watchlist", new { movieId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> Recompute(User user) =>
        user.Client.PostAsync("/api/matches/recompute", null);

    private static async Task<List<MatchBody>> RecomputeOk(User user)
    {
        var response = await Recompute(user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<MatchBody>>() ?? [];
    }

    private static async Task<List<MatchBody>> List(User user)
    {
        var response = await user.Client.GetAsync("/api/matches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<MatchBody>>() ?? [];
    }

    private static List<string> PropertyNames(string json)
    {
        var names = new List<string>();
        using var document = JsonDocument.Parse(json);
        Collect(document.RootElement);
        return names;

        void Collect(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        names.Add(property.Name);
                        Collect(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item);
                    }

                    break;
            }
        }
    }
}
