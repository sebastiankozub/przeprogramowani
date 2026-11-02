namespace MovieDate.Contracts;

public record WatchedMovieResponse(MovieResponse Movie, int Rating, DateTimeOffset UpdatedAt);
