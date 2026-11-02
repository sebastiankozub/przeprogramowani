using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum WatchedMovieOutcome
{
    Success,

    /// <summary>Rating outside <see cref="WatchedMovie.MinRating"/>–<see cref="WatchedMovie.MaxRating"/>.</summary>
    InvalidRating,

    /// <summary>Unknown movie (add) or movie not on this user's list (update/remove).</summary>
    NotFound,

    /// <summary>The movie is already on this user's list.</summary>
    Duplicate,
}

public sealed record WatchedMovieResult(WatchedMovieOutcome Outcome, WatchedMovieResponse? Value = null)
{
    public static WatchedMovieResult Ok(WatchedMovieResponse? value = null) => new(WatchedMovieOutcome.Success, value);

    public static readonly WatchedMovieResult InvalidRating = new(WatchedMovieOutcome.InvalidRating);
    public static readonly WatchedMovieResult NotFound = new(WatchedMovieOutcome.NotFound);
    public static readonly WatchedMovieResult Duplicate = new(WatchedMovieOutcome.Duplicate);
}

/// <summary>
/// A user's watched movies and their ratings. Every operation is scoped to the given user id
/// (AspNetUsers.Id), so another user's entries are indistinguishable from missing ones.
/// </summary>
public sealed class WatchedMoviesService(AppDbContext db)
{
    public static bool IsValidRating(int rating) =>
        rating is >= WatchedMovie.MinRating and <= WatchedMovie.MaxRating;

    /// <summary>The user's watched movies, most recently added or re-rated first.</summary>
    public async Task<IReadOnlyList<WatchedMovieResponse>> ListAsync(string userId, CancellationToken ct = default) =>
        await db.WatchedMovies
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.UpdatedAt)
            .ThenBy(w => w.MovieId)
            .Select(w => new WatchedMovieResponse(
                new MovieResponse(w.Movie.Id, w.Movie.Title, w.Movie.OriginalTitle, w.Movie.Year),
                w.Rating,
                w.UpdatedAt))
            .ToListAsync(ct);

    public async Task<WatchedMovieResult> AddAsync(string userId, int movieId, int rating, CancellationToken ct = default)
    {
        if (!IsValidRating(rating))
        {
            return WatchedMovieResult.InvalidRating;
        }

        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return WatchedMovieResult.NotFound;
        }

        if (await db.WatchedMovies.AnyAsync(w => w.UserId == userId && w.MovieId == movieId, ct))
        {
            return WatchedMovieResult.Duplicate;
        }

        var now = Clock.Now();
        var watched = new WatchedMovie
        {
            UserId = userId,
            MovieId = movieId,
            Rating = (short)rating,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.WatchedMovies.Add(watched);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent add of the same movie by the same user (e.g. a double submit) lost the race.
            return WatchedMovieResult.Duplicate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The user (or the movie) was deleted between the checks above and the insert — e.g. a deleted
            // account whose cookie is still valid until the next security-stamp check.
            return WatchedMovieResult.NotFound;
        }

        return WatchedMovieResult.Ok(ToResponse(watched, movie));
    }

    public async Task<WatchedMovieResult> UpdateRatingAsync(string userId, int movieId, int rating, CancellationToken ct = default)
    {
        if (!IsValidRating(rating))
        {
            return WatchedMovieResult.InvalidRating;
        }

        var watched = await db.WatchedMovies
            .Include(w => w.Movie)
            .FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == movieId, ct);
        if (watched is null)
        {
            return WatchedMovieResult.NotFound;
        }

        watched.Rating = (short)rating;
        watched.UpdatedAt = Clock.Now();
        await db.SaveChangesAsync(ct);

        return WatchedMovieResult.Ok(ToResponse(watched, watched.Movie));
    }

    public async Task<WatchedMovieResult> RemoveAsync(string userId, int movieId, CancellationToken ct = default)
    {
        var deleted = await db.WatchedMovies
            .Where(w => w.UserId == userId && w.MovieId == movieId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? WatchedMovieResult.NotFound : WatchedMovieResult.Ok();
    }

    private static WatchedMovieResponse ToResponse(WatchedMovie watched, Movie movie) =>
        new(new MovieResponse(movie.Id, movie.Title, movie.OriginalTitle, movie.Year), watched.Rating, watched.UpdatedAt);
}
