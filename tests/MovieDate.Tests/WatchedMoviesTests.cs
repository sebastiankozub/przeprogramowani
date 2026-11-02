using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class WatchedMoviesTests(PostgresAppFixture factory)
{
    private const string Godfather = "Q47703";
    private const string Inception = "Q25188";

    private record MovieBody(int Id, string Title, string? OriginalTitle, int? Year);

    private record WatchedBody(MovieBody Movie, int Rating, DateTimeOffset UpdatedAt);

    private async Task<int> MovieId(string wikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Movies.Where(m => m.WikidataId == wikidataId).Select(m => m.Id).SingleAsync();
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, int movieId, int rating) =>
        client.PostAsJsonAsync("/api/watched", new { movieId, rating });

    private static Task<HttpResponseMessage> Update(HttpClient client, int movieId, int rating) =>
        client.PutAsJsonAsync($"/api/watched/{movieId}", new { rating });

    private static async Task<List<WatchedBody>> List(HttpClient client)
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

        var add = await Add(client, movieId, 9);

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var created = await add.Content.ReadFromJsonAsync<WatchedBody>();
        Assert.Equal(movieId, created?.Movie.Id);
        Assert.Equal("Ojciec chrzestny", created?.Movie.Title);
        Assert.Equal(9, created?.Rating);

        var entry = Assert.Single(await List(client));
        Assert.Equal(movieId, entry.Movie.Id);
        Assert.Equal(9, entry.Rating);
    }

    [Fact]
    public async Task List_is_most_recently_updated_first()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var godfather = await MovieId(Godfather);
        var inception = await MovieId(Inception);
        await Add(client, godfather, 8);
        await Add(client, inception, 7);

        Assert.Equal([inception, godfather], (await List(client)).Select(w => w.Movie.Id));

        await Update(client, godfather, 10);

        Assert.Equal([godfather, inception], (await List(client)).Select(w => w.Movie.Id));
    }

    [Fact]
    public async Task Add_duplicate_returns_409()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        Assert.Equal(HttpStatusCode.Created, (await Add(client, movieId, 9)).StatusCode);

        var response = await Add(client, movieId, 5);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(9, Assert.Single(await List(client)).Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Add_with_rating_out_of_range_returns_400(int rating)
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);

        var response = await Add(client, movieId, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Add_unknown_movie_returns_404()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var response = await Add(client, int.MaxValue, 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_rating_then_list_shows_it()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId, 4);

        var update = await Update(client, movieId, 10);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(10, (await update.Content.ReadFromJsonAsync<WatchedBody>())?.Rating);
        Assert.Equal(10, Assert.Single(await List(client)).Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Update_with_rating_out_of_range_returns_400(int rating)
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId, 4);

        var response = await Update(client, movieId, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(4, Assert.Single(await List(client)).Rating);
    }

    [Fact]
    public async Task Update_movie_not_on_list_returns_404()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var response = await Update(client, await MovieId(Godfather), 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_it_and_second_delete_returns_404()
    {
        using var client = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        await Add(client, movieId, 6);

        var first = await client.DeleteAsync($"/api/watched/{movieId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Empty(await List(client));

        var second = await client.DeleteAsync($"/api/watched/{movieId}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Other_user_cannot_see_update_or_delete_entry()
    {
        using var alice = await TestUsers.NewSignedInClient(factory);
        using var bob = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);
        Assert.Equal(HttpStatusCode.Created, (await Add(alice, movieId, 7)).StatusCode);

        Assert.Empty(await List(bob));
        Assert.Equal(HttpStatusCode.NotFound, (await Update(bob, movieId, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/watched/{movieId}")).StatusCode);

        Assert.Equal(7, Assert.Single(await List(alice)).Rating);
    }

    [Fact]
    public async Task Same_movie_can_be_rated_by_two_users()
    {
        using var alice = await TestUsers.NewSignedInClient(factory);
        using var bob = await TestUsers.NewSignedInClient(factory);
        var movieId = await MovieId(Godfather);

        Assert.Equal(HttpStatusCode.Created, (await Add(alice, movieId, 7)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Add(bob, movieId, 3)).StatusCode);

        Assert.Equal(7, Assert.Single(await List(alice)).Rating);
        Assert.Equal(3, Assert.Single(await List(bob)).Rating);
    }

    [Fact]
    public async Task Endpoints_without_session_return_401()
    {
        using var client = TestUsers.NewClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/watched")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Add(client, 1, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Update(client, 1, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/watched/1")).StatusCode);
    }
}
