namespace MovieDate.Data;

/// <summary>A film the user wants to watch. One row per (user, movie); independent of <see cref="WatchedMovie"/>.</summary>
public class WatchlistItem
{
    public required string UserId { get; set; }

    public int MovieId { get; set; }

    public Movie Movie { get; set; } = null!;

    public DateTimeOffset AddedAt { get; set; }
}
