using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

/// <summary>
/// Upserts the embedded Wikidata city catalog (Data/Seed/cities.json, regenerated from Data/Seed/cities.sparql)
/// keyed on <see cref="City.WikidataId"/>: inserts missing cities, updates changed ones, never deletes
/// (users reference cities).
/// </summary>
public static class CityCatalogSeeder
{
    public const string ResourceName = "MovieDate.Data.Seed.cities.json";

    private sealed record SeedCity(
        string WikidataId, string Name, string Voivodeship, double Latitude, double Longitude, int? Population);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(int Inserted, int Updated)> SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var seed = await LoadAsync(ct);

        var existing = await db.Cities.ToDictionaryAsync(c => c.WikidataId, StringComparer.Ordinal, ct);

        int inserted = 0, updated = 0;
        foreach (var item in seed)
        {
            if (existing.TryGetValue(item.WikidataId, out var city))
            {
                if (city.Name == item.Name && city.Voivodeship == item.Voivodeship
                    && city.Latitude == item.Latitude && city.Longitude == item.Longitude
                    && city.Population == item.Population)
                {
                    continue;
                }

                city.Name = item.Name;
                city.Voivodeship = item.Voivodeship;
                city.Latitude = item.Latitude;
                city.Longitude = item.Longitude;
                city.Population = item.Population;
                updated++;
            }
            else
            {
                var added = new City
                {
                    WikidataId = item.WikidataId,
                    Name = item.Name,
                    Voivodeship = item.Voivodeship,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
                    Population = item.Population,
                };
                db.Cities.Add(added);
                existing[item.WikidataId] = added;
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (inserted, updated);
    }

    private static async Task<List<SeedCity>> LoadAsync(CancellationToken ct)
    {
        await using var stream = typeof(CityCatalogSeeder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");

        var cities = await JsonSerializer.DeserializeAsync<List<SeedCity>>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is empty.");

        foreach (var city in cities)
        {
            if (string.IsNullOrWhiteSpace(city.WikidataId) || string.IsNullOrWhiteSpace(city.Name)
                || string.IsNullOrWhiteSpace(city.Voivodeship))
            {
                throw new InvalidOperationException(
                    $"Seed entry '{city.WikidataId}' is missing its Wikidata id, name or voivodeship.");
            }
        }

        return cities;
    }
}
