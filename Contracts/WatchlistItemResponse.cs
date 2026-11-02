namespace MovieDate.Contracts;

public record WatchlistItemResponse(MovieResponse Movie, DateTimeOffset AddedAt);
