namespace MovieDate.Data;

/// <summary>Catalog film, seeded from Wikidata (Data/Seed/movies.json) and keyed by its QID.</summary>
public class Movie
{
    public const int WikidataIdMaxLength = 16;
    public const int TitleMaxLength = 300;

    public int Id { get; set; }

    public required string WikidataId { get; set; }

    /// <summary>Polish title, falling back to the English label.</summary>
    public required string Title { get; set; }

    public string? OriginalTitle { get; set; }

    public int? Year { get; set; }
}
