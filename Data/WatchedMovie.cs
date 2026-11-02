namespace MovieDate.Data;

/// <summary>A film the user has watched, with their 1–10 rating. One row per (user, movie).</summary>
public class WatchedMovie
{
    public const short MinRating = 1;
    public const short MaxRating = 10;

    public required string UserId { get; set; }

    public int MovieId { get; set; }

    public Movie Movie { get; set; } = null!;

    public short Rating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
