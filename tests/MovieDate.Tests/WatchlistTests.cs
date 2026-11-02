using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class WatchlistTests(PostgresAppFixture factory)
{
    private const string Godfather = "Q47703";
    private const string Inception = "Q25188";

    private record MovieBody(int Id, string Title, string? OriginalTitle, int? Year);

    private record WatchlistBody(MovieBody Movie, DateTimeOffset AddedAt);

    private record WatchedBody(MovieBody Movie, int Rating, DateTimeOffset UpdatedAt);

    private async Task<int> MovieId(string wikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Movies.Where(m => m.WikidataId == wikidataId).Select(m => m.Id).SingleAsync();
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, int movieId) =>
        client.PostAsJsonAsync("/api/watchlist", new { movieId });

    private static Task<HttpResponseMessage> Remove(HttpClient client, int movieId) =>
        client.DeleteAsync($"/api/watchlist/{movieId}");

    private static Task<HttpResponseMessage> MarkWatched(HttpClient client, int movieId, int rating) =>
        client.PostAsJsonAsync($"/api/watchlist/{movieId}/watched", new { rating });

    private static Task<HttpResponseMessage> Rate(HttpClient client, int movieId, int rating) =>
        client.PostAsJsonAsync("/api/watched", new { movieId, rating });

    private static async Task<List<WatchlistBody>> List(HttpClient client)
    {
        var response = await client.GetAsync("/api/watchlist");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<WatchlistBody>>() ?? [];
    }

    private static async Task<List<WatchedBody>> Watched(HttpClient client)
    {
        var response = await client.GetAsync("/api/watched");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<WatchedBody>>() ?? [];
    }

    [Fact]
    public async Task Add_then_list_contains_it()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);

        var add = await Add(client, movieId);

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Null(add.Headers.Location);
        var created = await add.Content.ReadFromJsonAsync<WatchlistBody>();
        Assert.Equal(movieId, created?.Movie.Id);
        Assert.Equal("Ojciec chrzestny", created?.Movie.Title);

        var entry = Assert.Single(await List(client));
        Assert.Equal(movieId, entry.Movie.Id);
        Assert.Equal(created?.AddedAt, entry.AddedAt);
    }

    [Fact]
    public async Task List_is_most_recently_added_first()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var godfather = await MovieId(Godfather);
        var inception = await MovieId(Inception);
        await Add(client, godfather);
        await Add(client, inception);

        Assert.Equal([inception, godfather], (await List(client)).Select(w => w.Movie.Id));
    }

    [Fact]
    public async Task Add_duplicate_returns_409()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        Assert.Equal(HttpStatusCode.Created, (await Add(client, movieId)).StatusCode);

        var response = await Add(client, movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await List(client));
    }

    [Fact]
    public async Task Add_unknown_movie_returns_404()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var response = await Add(client, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Remove_deletes_it_and_second_remove_returns_404()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId);

        Assert.Equal(HttpStatusCode.NoContent, (await Remove(client, movieId)).StatusCode);
        Assert.Empty(await List(client));

        Assert.Equal(HttpStatusCode.NotFound, (await Remove(client, movieId)).StatusCode);
    }

    [Fact]
    public async Task Other_user_cannot_see_remove_or_mark_watched_entry()
    {
        using var alice = await TestUsers.NewSignedInClient(factory);
        using var bob = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        Assert.Equal(HttpStatusCode.Created, (await Add(alice, movieId)).StatusCode);

        Assert.Empty(await List(bob));
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(bob, movieId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MarkWatched(bob, movieId, 5)).StatusCode);

        Assert.Equal(movieId, Assert.Single(await List(alice)).Movie.Id);
        Assert.Empty(await Watched(bob));
    }

    [Fact]
    public async Task Movie_can_be_rated_and_on_the_watchlist()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);

        Assert.Equal(HttpStatusCode.Created, (await Rate(client, movieId, 8)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Add(client, movieId)).StatusCode);

        Assert.Equal(movieId, Assert.Single(await List(client)).Movie.Id);
        Assert.Equal(8, Assert.Single(await Watched(client)).Rating);
    }

    [Fact]
    public async Task Mark_watched_moves_entry_to_watched_list()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId);

        var response = await MarkWatched(client, movieId, 9);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WatchedBody>();
        Assert.Equal(movieId, body?.Movie.Id);
        Assert.Equal(9, body?.Rating);

        Assert.Empty(await List(client));
        var watched = Assert.Single(await Watched(client));
        Assert.Equal(movieId, watched.Movie.Id);
        Assert.Equal(9, watched.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Mark_watched_with_rating_out_of_range_returns_400_and_keeps_entry(int rating)
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId);

        var response = await MarkWatched(client, movieId, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(movieId, Assert.Single(await List(client)).Movie.Id);
        Assert.Empty(await Watched(client));
    }

    [Fact]
    public async Task Mark_watched_already_rated_overwrites_rating_and_removes_entry()
    {
        // Re-watch: the movie is rated (4) and wanted again; marking it watched with 9 keeps one entry with 9.
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        Assert.Equal(HttpStatusCode.Created, (await Rate(client, movieId, 4)).StatusCode);
        await Add(client, movieId);

        var response = await MarkWatched(client, movieId, 9);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await List(client));
        var entry = Assert.Single(await Watched(client));
        Assert.Equal(movieId, entry.Movie.Id);
        Assert.Equal(9, entry.Rating);
    }

    [Fact]
    public async Task Mark_watched_movie_not_on_watchlist_returns_404_before_rating_validation()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);

        Assert.Equal(HttpStatusCode.NotFound, (await MarkWatched(client, movieId, 9)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MarkWatched(client, movieId, 0)).StatusCode);
        Assert.Empty(await Watched(client));
    }

    [Fact]
    public async Task Endpoints_without_session_return_401()
    {
        using var client = TestUsers.NewClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/watchlist")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Add(client, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remove(client, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MarkWatched(client, 1, 5)).StatusCode);
    }
}
