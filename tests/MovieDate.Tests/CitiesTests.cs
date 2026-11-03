using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class CitiesTests(PostgresAppFixture factory)
{
    private record CityBody(int Id, string Name, string Voivodeship);

    private static readonly string[] ForbiddenFields = ["latitude", "longitude", "population"];

    /// <summary>Guardrail: no API response may carry a city's coordinates or population, at any depth.</summary>
    internal static void AssertNoCoordinatesOrPopulation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = new List<string>();
        Collect(document.RootElement, names);

        foreach (var forbidden in ForbiddenFields)
        {
            Assert.DoesNotContain(names, n => string.Equals(n, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        static void Collect(JsonElement element, List<string> names)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        names.Add(property.Name);
                        Collect(property.Value, names);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item, names);
                    }
                    break;
            }
        }
    }

    private static async Task<List<CityBody>> Search(HttpClient client, string q)
    {
        var response = await client.GetAsync($"/api/cities?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<CityBody>>() ?? [];
    }

    [Fact]
    public async Task Catalog_is_seeded_and_reseeding_inserts_nothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.Cities.CountAsync() > 900);

        var (inserted, updated) = await CityCatalogSeeder.SeedAsync(db);

        Assert.Equal(0, inserted);
        Assert.Equal(0, updated);
    }

    [Fact]
    public async Task Search_finds_city_case_insensitive_with_voivodeship()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "WARSZAWA");

        var warsaw = Assert.Single(results, c => c.Name == "Warszawa");
        Assert.Equal("mazowieckie", warsaw.Voivodeship);
    }

    [Fact]
    public async Task Search_puts_prefix_matches_first_then_larger_places()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "war");

        Assert.NotEmpty(results);
        Assert.Equal("Warszawa", results[0].Name);
        // Every prefix match precedes every non-prefix ("contains") match.
        var firstContains = results.FindIndex(c => !c.Name.StartsWith("war", StringComparison.OrdinalIgnoreCase));
        if (firstContains >= 0)
        {
            Assert.All(results.Skip(firstContains),
                c => Assert.False(c.Name.StartsWith("war", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task Search_returns_at_most_20_results()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "ow");

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

    [Fact]
    public async Task Search_matches_literal_percent_and_underscore_in_names()
    {
        // A name with a literal "_x%", and a decoy that an unescaped "_x%" pattern would also match.
        var token = Guid.NewGuid().ToString("N")[..8];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Cities.AddRange(
                new City { WikidataId = $"QT-{token}a", Name = $"Zq{token}_x%Zq literal", Voivodeship = "testowe" },
                new City { WikidataId = $"QT-{token}b", Name = $"Zq{token}AxBZq decoy", Voivodeship = "testowe" });
            await db.SaveChangesAsync();
        }

        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, $"zq{token}_x%zq");

        var found = Assert.Single(results);
        Assert.Equal($"Zq{token}_x%Zq literal", found.Name);
    }

    [Fact]
    public async Task Search_caps_overlong_query()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, "warszawa" + new string('x', 500));

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("w")]
    [InlineData("  w  ")]
    [InlineData("")]
    public async Task Search_shorter_than_2_characters_returns_empty(string q)
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var results = await Search(client, q);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_response_has_no_coordinates_or_population()
    {
        using var client = await TestUsers.NewSignedInClient(factory);

        var response = await client.GetAsync("/api/cities?q=kra");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kraków", json);
        AssertNoCoordinatesOrPopulation(json);
    }

    [Fact]
    public async Task Search_without_session_returns_401()
    {
        using var client = TestUsers.NewClient(factory);

        var response = await client.GetAsync("/api/cities?q=war");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
