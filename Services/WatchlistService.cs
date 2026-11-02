using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum WatchlistOutcome
{
    Success,

    /// <summary>Unknown movie (add) or movie not on this user's watchlist (remove).</summary>
    NotFound,

    /// <summary>The movie is already on this user's watchlist.</summary>
    Duplicate,
}

public sealed record WatchlistResult(WatchlistOutcome Outcome, WatchlistItemResponse? Value = null)
{
    public static WatchlistResult Ok(WatchlistItemResponse? value = null) => new(WatchlistOutcome.Success, value);

    public static readonly WatchlistResult NotFound = new(WatchlistOutcome.NotFound);
    public static readonly WatchlistResult Duplicate = new(WatchlistOutcome.Duplicate);
}

/// <summary>
/// A user's want-to-watch list. Every operation is scoped to the given user id (AspNetUsers.Id),
/// so another user's entries are indistinguishable from missing ones. Independent of the watched list:
/// a movie may be on both.
/// </summary>
public sealed class WatchlistService(AppDbContext db, WatchedMoviesService watched)
{
    /// <summary>The user's watchlist, most recently added first.</summary>
    public async Task<IReadOnlyList<WatchlistItemResponse>> ListAsync(string userId, CancellationToken ct = default) =>
        await db.WatchlistItems
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.AddedAt)
            .ThenBy(w => w.MovieId)
            .Select(w => new WatchlistItemResponse(
                new MovieResponse(w.Movie.Id, w.Movie.Title, w.Movie.OriginalTitle, w.Movie.Year),
                w.AddedAt))
            .ToListAsync(ct);

    public async Task<WatchlistResult> AddAsync(string userId, int movieId, CancellationToken ct = default)
    {
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return WatchlistResult.NotFound;
        }

        if (await db.WatchlistItems.AnyAsync(w => w.UserId == userId && w.MovieId == movieId, ct))
        {
            return WatchlistResult.Duplicate;
        }

        var item = new WatchlistItem { UserId = userId, MovieId = movieId, AddedAt = Now() };
        db.WatchlistItems.Add(item);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent add of the same movie by the same user (e.g. a double submit) lost the race.
            return WatchlistResult.Duplicate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The user (or the movie) was deleted between the checks above and the insert.
            return WatchlistResult.NotFound;
        }

        return WatchlistResult.Ok(
            new WatchlistItemResponse(new MovieResponse(movie.Id, movie.Title, movie.OriginalTitle, movie.Year), item.AddedAt));
    }

    public async Task<WatchlistResult> RemoveAsync(string userId, int movieId, CancellationToken ct = default)
    {
        var deleted = await db.WatchlistItems
            .Where(w => w.UserId == userId && w.MovieId == movieId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? WatchlistResult.NotFound : WatchlistResult.Ok();
    }

    /// <summary>
    /// Moves a watchlist entry to the watched list with the given rating, atomically: either the movie is
    /// rated and leaves the watchlist, or nothing changes. Outcome order: not on the watchlist → NotFound,
    /// then rating → InvalidRating, then already rated → Duplicate.
    /// </summary>
    public async Task<WatchedMovieResult> MarkWatchedAsync(string userId, int movieId, int rating, CancellationToken ct = default)
    {
        // Preconditions are checked before any write, so a rejected request never touches the change tracker.
        if (!await db.WatchlistItems.AnyAsync(w => w.UserId == userId && w.MovieId == movieId, ct))
        {
            return WatchedMovieResult.NotFound;
        }

        if (!WatchedMoviesService.IsValidRating(rating))
        {
            return WatchedMovieResult.InvalidRating;
        }

        // A user-initiated transaction must run inside the retrying execution strategy (EnableRetryOnFailure);
        // each attempt is replayed from scratch.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await watched.AddAsync(userId, movieId, rating, ct);
                if (result.Outcome != WatchedMovieOutcome.Success)
                {
                    await RollBackAsync(transaction);
                    return result;
                }

                var removed = await db.WatchlistItems
                    .Where(w => w.UserId == userId && w.MovieId == movieId)
                    .ExecuteDeleteAsync(ct);
                if (removed == 0)
                {
                    // Removed concurrently after the precondition check — the rating must not stick either.
                    await RollBackAsync(transaction);
                    return WatchedMovieResult.NotFound;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                // Drop the WatchedMovie added by AddAsync so neither a retry nor a later SaveChanges persists it.
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task RollBackAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);

        // A failed insert leaves its WatchedMovie tracked as Added; a rolled-back one stays tracked as Unchanged.
        // Either way it no longer exists in the database, so it must not stay in the tracker.
        db.ChangeTracker.Clear();
    }

    // Postgres timestamptz stores microseconds; truncate so the returned value equals what a later read returns.
    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}
