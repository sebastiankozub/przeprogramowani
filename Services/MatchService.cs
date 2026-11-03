using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum MatchOutcome
{
    Success,

    /// <summary>The requester has no home city or no display name, so they cannot be matched.</summary>
    ProfileIncomplete,

    /// <summary>The user row no longer exists (e.g. a deleted account's cookie).</summary>
    NotFound,
}

public sealed record MatchResult(MatchOutcome Outcome, IReadOnlyList<MatchResponse>? Value = null)
{
    public static MatchResult Ok(IReadOnlyList<MatchResponse> value) => new(MatchOutcome.Success, value);

    public static readonly MatchResult ProfileIncomplete = new(MatchOutcome.ProfileIncomplete);
    public static readonly MatchResult NotFound = new(MatchOutcome.NotFound);
}

/// <summary>Whether the user's stored list is waiting for a recompute, and whether they can be matched at all.</summary>
public sealed record MatchStatus(bool Pending, bool ProfileComplete);

/// <summary>
/// The stored, append-only, mirrored match list (FR-005). Every operation is keyed on AspNetUsers.Id, never on
/// the email. Rows are only ever inserted or re-scored here — never deleted; they disappear solely through the
/// FK cascade when either account is deleted. <see cref="RecomputeAsync"/> runs from
/// <see cref="MatchRecomputeWorker"/> after writes (S-06) and from <c>POST /api/matches/recompute</c>.
/// </summary>
public sealed class MatchService(AppDbContext db)
{
    /// <summary>At most this many strong, not-yet-listed candidates are added per recompute.</summary>
    public const int MaxNewStrongCandidates = 20;

    /// <summary>When the list is shorter than this, the closest non-strong users top it up.</summary>
    public const int MinListSize = 5;

    /// <summary>At most this many shared titles of each kind are returned per entry.</summary>
    public const int MaxSharedTitles = 5;

    // A candidate (or the requester) deleted between the reads and the upsert surfaces as an FK violation;
    // the recompute is then simply re-run on fresh data.
    private const int MaxAttempts = 3;

    /// <summary>The user's stored list: score desc, then first-matched asc, then candidate id. A pure read.</summary>
    public async Task<IReadOnlyList<MatchResponse>> ListAsync(string userId, CancellationToken ct = default)
    {
        // Candidates only ever enter the list with a city and a display name, and neither can be cleared;
        // the filter just keeps the non-nullable contract honest.
        var rows = await db.Matches
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(
                db.Users.Where(u => u.CityId != null && u.DisplayName != null),
                m => m.CandidateId,
                u => u.Id,
                (m, u) => new
                {
                    m.CandidateId,
                    m.Score,
                    m.SharedFavorites,
                    m.SharedWatchlist,
                    m.FirstMatchedAt,
                    DisplayName = u.DisplayName!,
                    City = new CityResponse(u.City!.Id, u.City.Name, u.City.Voivodeship),
                })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.FirstMatchedAt)
            .ThenBy(r => r.CandidateId)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var candidateIds = rows.Select(r => r.CandidateId).ToList();
        var favorites = await SharedFavoriteTitlesAsync(userId, candidateIds, ct);
        var watchlist = await SharedWatchlistTitlesAsync(userId, candidateIds, ct);
        var friends = (await db.Friendships
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .Select(f => f.FriendId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        return rows
            .Select(r => new MatchResponse(
                r.CandidateId,
                r.DisplayName,
                r.City,
                (int)Math.Round(r.Score, MidpointRounding.AwayFromZero),
                r.SharedFavorites,
                r.SharedWatchlist,
                favorites.TryGetValue(r.CandidateId, out var f) ? f : [],
                watchlist.TryGetValue(r.CandidateId, out var w) ? w : [],
                r.FirstMatchedAt,
                friends.Contains(r.CandidateId)))
            .ToList();
    }

    /// <summary>
    /// The pending rule (S-06): a user with a complete profile is pending until their first recompute, and again
    /// whenever a write requested one after the last recompute started. Null when the user row is gone.
    /// </summary>
    public async Task<MatchStatus?> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                ProfileComplete = u.CityId != null && u.DisplayName != null,
                u.MatchesRequestedAt,
                u.MatchesComputedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return null;
        }

        var pending = user.ProfileComplete
                      && (user.MatchesComputedAt is null
                          || (user.MatchesRequestedAt is not null && user.MatchesRequestedAt > user.MatchesComputedAt));
        return new MatchStatus(pending, user.ProfileComplete);
    }

    /// <summary>
    /// Recompute rule for the requester (plan steps 1–7): re-score the eligible listed candidates, add up to
    /// <see cref="MaxNewStrongCandidates"/> new strong ones, top up with the closest users to
    /// <see cref="MinListSize"/>, and mirror every written row. Returns the refreshed list.
    /// </summary>
    public async Task<MatchResult> RecomputeAsync(string userId, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            MatchOutcome outcome;
            try
            {
                // A user-initiated transaction must run inside the retrying execution strategy (EnableRetryOnFailure);
                // each attempt is replayed from scratch. No verifySucceeded: the upserts are idempotent, so replaying
                // a commit whose acknowledgement was lost just writes the same rows again.
                var strategy = db.Database.CreateExecutionStrategy();
                outcome = await strategy.ExecuteAsync(
                    userId,
                    (_, id, token) => RecomputeInTransactionAsync(id, token),
                    verifySucceeded: null,
                    ct);
            }
            catch (Exception ex) when (IsForeignKeyViolation(ex) && attempt < MaxAttempts)
            {
                // Someone in the batch was deleted concurrently; the next attempt reads fresh data
                // (and reports NotFound if it was the requester).
                continue;
            }

            return outcome switch
            {
                MatchOutcome.Success => MatchResult.Ok(await ListAsync(userId, ct)),
                MatchOutcome.ProfileIncomplete => MatchResult.ProfileIncomplete,
                MatchOutcome.NotFound => MatchResult.NotFound,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
            };
        }
    }

    private async Task<MatchOutcome> RecomputeInTransactionAsync(string userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Captured before anything is read: a write that lands while this recompute runs stamps a later
        // MatchesRequestedAt, so the user stays pending and is recomputed again.
        var start = Clock.Now();

        // 1. The requester must have a city and a display name; nothing is written otherwise.
        var requester = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.DisplayName,
                HasCity = u.CityId != null,
                Latitude = u.City != null ? u.City.Latitude : 0,
                Longitude = u.City != null ? u.City.Longitude : 0,
            })
            .FirstOrDefaultAsync(ct);
        if (requester is null)
        {
            return MatchOutcome.NotFound;
        }

        if (!requester.HasCity || requester.DisplayName is null)
        {
            return MatchOutcome.ProfileIncomplete;
        }

        var listed = (await db.Matches
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.CandidateId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // 2. Eligible candidates: every other user with a city and a display name.
        var eligible = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != userId && u.CityId != null && u.DisplayName != null)
            .Select(u => new { u.Id, u.City!.Latitude, u.City.Longitude })
            .ToListAsync(ct);

        // H and W per candidate via grouped joins against the requester's movie ids — other users' full
        // lists are never loaded.
        var myFavorites = db.WatchedMovies
            .Where(w => w.UserId == userId && w.Rating >= MatchScoringService.FavoriteThreshold)
            .Select(w => w.MovieId);
        var sharedFavorites = await db.WatchedMovies
            .Where(w => w.UserId != userId
                        && w.Rating >= MatchScoringService.FavoriteThreshold
                        && myFavorites.Contains(w.MovieId))
            .GroupBy(w => w.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, StringComparer.Ordinal, ct);

        var myWatchlist = db.WatchlistItems
            .Where(w => w.UserId == userId)
            .Select(w => w.MovieId);
        var sharedWatchlist = await db.WatchlistItems
            .Where(w => w.UserId != userId && myWatchlist.Contains(w.MovieId))
            .GroupBy(w => w.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, StringComparer.Ordinal, ct);

        var scored = eligible
            .Select(c =>
            {
                var h = sharedFavorites.GetValueOrDefault(c.Id);
                var w = sharedWatchlist.GetValueOrDefault(c.Id);
                var km = MatchScoringService.DistanceKm(requester.Latitude, requester.Longitude, c.Latitude, c.Longitude);
                return new ScoredCandidate(c.Id, h, w, km, MatchScoringService.Score(h, w, km),
                    MatchScoringService.IsStrong(h, w));
            })
            .ToList();

        // 3. Re-score every listed candidate who is still eligible (ineligible ones keep their last score).
        var rescored = scored.Where(c => listed.Contains(c.Id));

        // 4. Up to 20 strong, not-yet-listed candidates, highest score first.
        var newStrong = scored
            .Where(c => c.IsStrong && !listed.Contains(c.Id))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Take(MaxNewStrongCandidates)
            .ToList();

        // 5. Top up with the closest not-yet-listed, non-strong users while the list is shorter than 5.
        var missing = MinListSize - listed.Count - newStrong.Count;
        var topUp = missing <= 0
            ? []
            : scored
                .Where(c => !c.IsStrong && !listed.Contains(c.Id))
                .OrderBy(c => c.DistanceKm)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .Take(missing)
                .ToList();

        var written = rescored.Concat(newStrong).Concat(topUp).ToList();
        if (written.Count > 0)
        {
            await UpsertMirroredAsync(userId, written, ct);
        }

        // Mark the list as computed as of `start` (S-06) — forward-only, so a slower, older recompute committing
        // later never moves the stamp back past a newer one.
        await db.Users
            .Where(u => u.Id == userId && (u.MatchesComputedAt == null || u.MatchesComputedAt < start))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MatchesComputedAt, start), ct);

        await transaction.CommitAsync(ct);
        return MatchOutcome.Success;
    }

    /// <summary>
    /// 6. Upserts A → X and the mirror X → A for every candidate in one statement. On conflict only the score,
    /// counts and UpdatedAt change — FirstMatchedAt is kept. Rows are written in (UserId, CandidateId) order so
    /// concurrent recomputes touching the same pairs (A's and B's, or a double click) lock them in the same
    /// order instead of deadlocking.
    /// </summary>
    private async Task UpsertMirroredAsync(string userId, IReadOnlyList<ScoredCandidate> candidates,
        CancellationToken ct)
    {
        var rows = candidates
            .SelectMany(c => new[]
            {
                (UserId: userId, CandidateId: c.Id, c.Score, c.SharedFavorites, c.SharedWatchlist),
                (UserId: c.Id, CandidateId: userId, c.Score, c.SharedFavorites, c.SharedWatchlist),
            })
            .ToList();

        var userIds = rows.Select(r => r.UserId).ToArray();
        var candidateIds = rows.Select(r => r.CandidateId).ToArray();
        var scores = rows.Select(r => r.Score).ToArray();
        var favorites = rows.Select(r => r.SharedFavorites).ToArray();
        var watchlist = rows.Select(r => r.SharedWatchlist).ToArray();
        var now = Clock.Now();

        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "Matches" ("UserId", "CandidateId", "Score", "SharedFavorites", "SharedWatchlist", "FirstMatchedAt", "UpdatedAt")
             SELECT r.user_id, r.candidate_id, r.score, r.shared_favorites, r.shared_watchlist, {now}, {now}
             FROM unnest({userIds}::text[], {candidateIds}::text[], {scores}::double precision[], {favorites}::integer[], {watchlist}::integer[])
                  AS r(user_id, candidate_id, score, shared_favorites, shared_watchlist)
             ORDER BY r.user_id COLLATE "C", r.candidate_id COLLATE "C"
             ON CONFLICT ("UserId", "CandidateId") DO UPDATE SET
                 "Score" = EXCLUDED."Score",
                 "SharedFavorites" = EXCLUDED."SharedFavorites",
                 "SharedWatchlist" = EXCLUDED."SharedWatchlist",
                 "UpdatedAt" = EXCLUDED."UpdatedAt"
             """,
            ct);
    }

    private async Task<Dictionary<string, IReadOnlyList<MovieResponse>>> SharedFavoriteTitlesAsync(
        string userId, List<string> candidateIds, CancellationToken ct)
    {
        var shared = await (
                from mine in db.WatchedMovies
                where mine.UserId == userId && mine.Rating >= MatchScoringService.FavoriteThreshold
                join theirs in db.WatchedMovies on mine.MovieId equals theirs.MovieId
                where candidateIds.Contains(theirs.UserId) && theirs.Rating >= MatchScoringService.FavoriteThreshold
                orderby mine.Movie.Title, mine.MovieId
                select new SharedTitle(
                    theirs.UserId,
                    new MovieResponse(mine.Movie.Id, mine.Movie.Title, mine.Movie.OriginalTitle, mine.Movie.Year)))
            .AsNoTracking()
            .ToListAsync(ct);

        return CapPerCandidate(shared);
    }

    private async Task<Dictionary<string, IReadOnlyList<MovieResponse>>> SharedWatchlistTitlesAsync(
        string userId, List<string> candidateIds, CancellationToken ct)
    {
        var shared = await (
                from mine in db.WatchlistItems
                where mine.UserId == userId
                join theirs in db.WatchlistItems on mine.MovieId equals theirs.MovieId
                where candidateIds.Contains(theirs.UserId)
                orderby mine.Movie.Title, mine.MovieId
                select new SharedTitle(
                    theirs.UserId,
                    new MovieResponse(mine.Movie.Id, mine.Movie.Title, mine.Movie.OriginalTitle, mine.Movie.Year)))
            .AsNoTracking()
            .ToListAsync(ct);

        return CapPerCandidate(shared);
    }

    // The queries already order by title (database collation, as in the movie search); GroupBy keeps that order.
    private static Dictionary<string, IReadOnlyList<MovieResponse>> CapPerCandidate(List<SharedTitle> shared) =>
        shared
            .GroupBy(s => s.CandidateId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MovieResponse>)g
                    .Select(s => s.Movie)
                    .Take(MaxSharedTitles)
                    .ToList(),
                StringComparer.Ordinal);

    // ExecuteSql bypasses SaveChanges, so the provider exception may arrive unwrapped.
    private static bool IsForeignKeyViolation(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private sealed record ScoredCandidate(
        string Id, int SharedFavorites, int SharedWatchlist, double DistanceKm, double Score, bool IsStrong);

    private sealed record SharedTitle(string CandidateId, MovieResponse Movie);
}
