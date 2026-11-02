using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class MoviesTests(PostgresAppFixture factory)
{
    private record MovieBody(int Id, string Title, string? OriginalTitle, int? Year);

    private static async Task<List<MovieBody>> Search(HttpClient client, string q)
    {
        var response = await client.GetAsync($"/api/movies?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<MovieBody>>() ?? [];
    }

    [Fact]
    public async Task Catalog_is_seeded_and_reseeding_inserts_nothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.Movies.CountAsync() > 400);

        var (inserted, updated) = await MovieCatalogSeeder.SeedAsync(db);

        Assert.Equal(0, inserted);
        Assert.Equal(0, updated);
    }

    [Fact]
    public async Task Search_finds_film_by_polish_title_case_insensitive()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "OJCIEC chrzestny");

        var godfather = Assert.Single(results, m => m.OriginalTitle == "The Godfather");
        Assert.Equal("Ojciec chrzestny", godfather.Title);
        Assert.Equal(1972, godfather.Year);
    }

    [Fact]
    public async Task Search_finds_film_by_original_title()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "godfather");

        Assert.Contains(results, m => m.Title == "Ojciec chrzestny");
    }

    [Fact]
    public async Task Search_puts_prefix_matches_first()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "matrix");

        Assert.NotEmpty(results);
        Assert.StartsWith("matrix", results[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_returns_at_most_20_results()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "an");

        Assert.Equal(20, results.Count);
    }

    [Theory]
    [InlineData("%_")]
    [InlineData("%%")]
    [InlineData("\\%")]
    public async Task Search_treats_like_wildcards_literally(string q)
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, q);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("o")]
    [InlineData("  o  ")]
    [InlineData("")]
    public async Task Search_shorter_than_2_characters_returns_empty(string q)
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, q);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_without_session_returns_401()
    {
        using var client = TestUsers.NewClient(factory);

        var response = await client.GetAsync("/api/movies?q=god");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
