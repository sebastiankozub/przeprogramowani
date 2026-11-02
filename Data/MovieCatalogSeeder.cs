using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

/// <summary>
/// Upserts the embedded Wikidata catalog (Data/Seed/movies.json, regenerated from Data/Seed/movies.sparql)
/// keyed on <see cref="Movie.WikidataId"/>: inserts missing films, updates changed ones, never deletes.
/// </summary>
public static class MovieCatalogSeeder
{
    public const string ResourceName = "MovieDate.Data.Seed.movies.json";

    private sealed record SeedMovie(string WikidataId, string Title, string? OriginalTitle, int? Year);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(int Inserted, int Updated)> SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var seed = await LoadAsync(ct);

        var existing = await db.Movies.ToDictionaryAsync(m => m.WikidataId, StringComparer.Ordinal, ct);

        int inserted = 0, updated = 0;
        foreach (var item in seed)
        {
            if (existing.TryGetValue(item.WikidataId, out var movie))
            {
                if (movie.Title == item.Title && movie.OriginalTitle == item.OriginalTitle && movie.Year == item.Year)
                {
                    continue;
                }

                movie.Title = item.Title;
                movie.OriginalTitle = item.OriginalTitle;
                movie.Year = item.Year;
                updated++;
            }
            else
            {
                var added = new Movie
                {
                    WikidataId = item.WikidataId,
                    Title = item.Title,
                    OriginalTitle = item.OriginalTitle,
                    Year = item.Year,
                };
                db.Movies.Add(added);
                existing[item.WikidataId] = added;
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (inserted, updated);
    }

    private static async Task<List<SeedMovie>> LoadAsync(CancellationToken ct)
    {
        await using var stream = typeof(MovieCatalogSeeder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");

        var movies = await JsonSerializer.DeserializeAsync<List<SeedMovie>>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is empty.");

        foreach (var movie in movies)
        {
            if (string.IsNullOrWhiteSpace(movie.WikidataId) || string.IsNullOrWhiteSpace(movie.Title))
            {
                throw new InvalidOperationException(
                    $"Seed entry '{movie.WikidataId}' is missing its Wikidata id or title.");
            }
        }

        return movies;
    }
}
