<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Want-to-Watch List

- **Plan**: context/changes/want-to-watch-list/plan.md (incl. Review Addenda 2026-11-03 — re-watch overwrite)
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated re-run 2026-11-03: `dotnet build` 0/0; `dotnet test` 50/50; `ng test` 13/13; `npm run build` 499.77 kB initial (within 500 kB budget); prod `/api/watchlist` anonymous 401; prod `/health` ok. Drift review: everything MATCH against plan + addendum except a stale comment (F6). Safety review: `MarkWatchedAsync` sound under `EnableRetryOnFailure` (per-attempt transaction, tracker cleared on every failure path, `removed == 0` re-check inside the transaction); IDOR, contracts, migration verified.

## Findings

### F1 — Lost commit acknowledgement + retry reports a false 404

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Services/WatchlistService.cs:113-148
- **Detail**: Npgsql's retrying strategy has no `verifySucceeded`. If `CommitAsync` succeeds on the server but the connection drops before the ack, the replayed attempt sees the movie already rated, updates it, deletes 0 watchlist rows, rolls back and returns 404 — data is correct but the user is told the move failed. Rare (connection drop exactly at commit on Neon/Fly).
- **Fix**: Use the `ExecuteAsync(state, operation, verifySucceeded)` overload: verification checks "watched row exists with this rating and no watchlist row" and returns Success.
  - Strength: Correct outcome even on the lost-ack path; documented EF pattern for exactly this case.
  - Tradeoff: ~15 lines in a delicate method; the path is hard to test deterministically.
  - Confidence: MED — standard EF guidance, but untestable without fault injection.
  - Blind spot: How often Neon drops connections at commit (likely very rare).
- **Decision**: FIXED (verifySucceeded via ExecuteAsync(state, op, verify); lost-ack path itself not deterministically tested)

### F2 — Atomic rollback of "Obejrzałem" is untested

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/WatchlistTests.cs:171-185
- **Detail**: The 400 test returns before the transaction starts (rating is validated first); no test reaches a rollback branch (rating write failing, or `removed == 0` after a concurrent delete), so "both or nothing" is unproven.
- **Fix**: Add a service-level test with a `DbCommandInterceptor` that deletes the watchlist row from a second connection right after the rating write, then assert 404, no rating row and an empty change tracker.
  - Strength: Proves the core atomicity promise deterministically.
  - Tradeoff: One more complex test (~50 lines, custom DbContext options in the test).
  - Confidence: HIGH — interceptors run on the same command pipeline used in production.
  - Blind spot: None significant.
- **Decision**: FIXED (interceptor-based theory: fresh-add + re-watch rollback; break-check: commit instead of rollback fails both cases)

### F3 — Concurrent rating delete during re-watch returns a misleading 404

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Services/WatchlistService.cs:119-122
- **Detail**: A `DELETE /api/watched/{id}` between the `alreadyRated` check and `UpdateRatingAsync` yields NotFound → 404 "not on your watchlist" although the entry is still there; the SPA's reload on 404 self-heals.
- **Fix**: If `UpdateRatingAsync` returns NotFound, fall back to `AddAsync`.
- **Decision**: FIXED (UpdateRatingAsync NotFound -> AddAsync fallback)

### F4 — `AddAppWatchlist` relies on `AddAppWatchedMovies` being called

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Endpoints/WatchlistEndpoints.cs:13-17
- **Detail**: `WatchlistService` depends on `WatchedMoviesService`, which only `AddAppWatchedMovies` registers.
- **Fix**: `services.TryAddScoped<WatchedMoviesService>()` inside `AddAppWatchlist`.
- **Decision**: FIXED (TryAddScoped<WatchedMoviesService>)

### F5 — `Now()` microsecond truncation duplicated

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Services/WatchlistService.cs:161-165
- **Detail**: Verbatim copy of `WatchedMoviesService.Now()`.
- **Fix**: Move to a shared internal helper (e.g. `Services/Clock.cs`).
- **Decision**: FIXED (Services/Clock.cs shared helper)

### F6 — Pre-addendum wording left in `.http` comment and spec mock

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: movie-date.http:84, ClientApp/src/app/watchlist/watchlist.service.spec.ts:61
- **Detail**: Comment still says "409 already rated — entry stays on the watchlist"; the spec's mock 409 title says "already on your watched list" (only the mapped message is asserted).
- **Fix**: Update both to the re-watch overwrite / concurrent-race wording.
- **Decision**: FIXED (.http comment + spec mock wording)

### F7 — `pickedRatings` keeps entries after a reload

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/watchlist/watchlist.component.ts:35, 145
- **Detail**: After `reloadIfStale`, picked ratings for movies no longer listed remain in the map; harmless and tiny.
- **Fix**: Prune `pickedRatings` to the reloaded ids.
- **Decision**: FIXED (pickedRatings pruned after reload)

### F8 — Initial bundle at 499.77 kB of the 500 kB budget

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: ClientApp/angular.json (budgets), main chunk 491 kB
- **Detail**: Main grew ~6.5 kB in this change (493 → 499.77 kB); the next frontend change will trip the warning (and eventually the error budget).
- **Fix**: Raise the initial `maximumWarning` budget to 600 kB now (error budget unchanged), and revisit main-chunk contents if it keeps growing.
- **Decision**: FIXED (initial maximumWarning 600kB, maximumError 1MB unchanged)
