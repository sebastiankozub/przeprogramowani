namespace MovieDate.Contracts;

/// <summary>
/// One entry on the requester's match list. City-level data only — never the candidate's email, coordinates
/// or the distance (NFR-04). <see cref="Score"/> is the stored score rounded to an integer; the counts are as
/// of the last recompute, while the title lists are read live (at most 5 each, ordered by title).
/// </summary>
public record MatchResponse(
    string UserId,
    string DisplayName,
    CityResponse City,
    int Score,
    int SharedFavoritesCount,
    int SharedWatchlistCount,
    IReadOnlyList<MovieResponse> SharedFavorites,
    IReadOnlyList<MovieResponse> SharedWatchlist,
    DateTimeOffset FirstMatchedAt);
