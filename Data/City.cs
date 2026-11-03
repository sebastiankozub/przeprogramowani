namespace MovieDate.Data;

/// <summary>
/// Polish town (or large village), seeded from Wikidata (Data/Seed/cities.json) and keyed by its QID.
/// Coordinates and population are server-side only — never part of an API response.
/// </summary>
public class City
{
    public const int WikidataIdMaxLength = 16;
    public const int NameMaxLength = 200;
    public const int VoivodeshipMaxLength = 100;

    public int Id { get; set; }

    public required string WikidataId { get; set; }

    /// <summary>Polish name.</summary>
    public required string Name { get; set; }

    /// <summary>Lower-case adjective form, e.g. "mazowieckie".</summary>
    public required string Voivodeship { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int? Population { get; set; }
}
