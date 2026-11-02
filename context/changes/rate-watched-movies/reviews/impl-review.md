<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Rate Watched Movies

- **Plan**: context/changes/rate-watched-movies/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-02
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 7 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated re-run 2026-11-02: `dotnet build` 0 errors/0 warnings; `dotnet test` 35/35; `ng test` 9/9; `npm run build` 493 kB (within budget); prod `/api/movies` anonymous 401; prod `/health` ok. Drift review: every planned change MATCH, no MISSING/DRIFT.

## Findings

### F1 — Insert by a deleted-but-still-cookied user returns 500

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Services/WatchedMoviesService.cs:82-90
- **Detail**: Only `UniqueViolation` is caught on insert. If the account is deleted while its cookie is still valid (security stamp re-validated every 30 min), the insert hits the `UserId` FK (`23503`) and the request fails with an unhandled 500.
- **Fix**: Also catch `ForeignKeyViolation` and return NotFound (or accept as risk — no account deletion exists yet).
- **Decision**: FIXED (ForeignKeyViolation -> 404)

### F2 — SPA list drifts from server state after update / 404 / 409

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/movies/movies.component.ts:97-145
- **Detail**: (a) a re-rated movie stays in place but jumps to the top after reload (server orders by `UpdatedAt` desc); (b) on 404 from update/remove the stale row stays; (c) on 409 the message says "zmień ocenę poniżej" but the row may not be in the local list (added in another tab).
- **Fix**: Move an updated entry to the top (as `add()` does); on 404/409 reload the list via `listWatched()`.
- **Decision**: FIXED (updated row moves to top; list reloaded on 404/409)

### F3 — Test gaps: escaping has no positive case, two-user test asserts only 201s

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/MoviesTests.cs:78-89, tests/MovieDate.Tests/WatchedMoviesTests.cs:180-189
- **Detail**: `%_` and `%%` would catch missing escaping, but `\%` passes with or without it, and no test proves a title containing a literal `%`/`_` is found. `Same_movie_can_be_rated_by_two_users` never checks that each user still has their own rating.
- **Fix**: Seed a test movie whose title contains `_` and `%` and assert it is found by searching the literal; assert both users' ratings after the two adds.
- **Decision**: FIXED (literal _x% + decoy test; both users ratings asserted; break-check: removing escaping fails 4 tests)

### F4 — `201 Created` Location points to a route without GET

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Endpoints/WatchedMoviesEndpoints.cs:46
- **Detail**: Location is `/api/watched/{id}`, which only has PUT/DELETE (GET → 405).
- **Fix**: Return 201 without a Location header (`Results.Json(..., statusCode: 201)`).
- **Decision**: FIXED (201 via Results.Json, no Location)

### F5 — Catalog search query lives in the endpoint, not in Services/

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: Endpoints/MoviesEndpoints.cs:20-46
- **Detail**: CLAUDE.md puts business logic in Services/; the search is a small query with no business rules and the plan placed it in the endpoint. S-04 (watchlist) and S-05 (matching) will reuse catalog queries.
- **Fix**: Accept for now; move to a `MovieCatalogService` when S-04/S-05 reuse catalog queries.
- **Decision**: SKIPPED (accepted; move to a catalog service when S-04/S-05 reuse catalog queries)

### F6 — JSON normalizer not committed — seed not reproducible

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Data/Seed/movies.sparql:8-9
- **Detail**: The normalization (original-language title choice, "(film …)" suffix removal, year floor) lives in a scratchpad `normalize.js` outside the repo; regenerating `movies.json` from the query alone gives different output.
- **Fix**: Commit the normalizer as `Data/Seed/normalize.mjs` (excluded from the build) with a one-line usage note in `movies.sparql`.
- **Decision**: FIXED (Data/Seed/normalize.mjs incl. CORRECTIONS map; regenerates movies.json byte-identical)

### F7 — EF Core SQL logging floods Fly's log buffer

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: appsettings.json (Logging section)
- **Detail**: ~500 seed INSERTs logged at Information by `Microsoft.EntityFrameworkCore.Database.Command` pushed the release machine's start lines out of `fly logs` (100-line buffer) during the v7 check.
- **Fix**: Set `Microsoft.EntityFrameworkCore.Database.Command` to `Warning` in `appsettings.json` (Development keeps its own level).
- **Decision**: FIXED (EF Database.Command -> Warning in appsettings.json)

### F8 — SPA error-message nits

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/movies/movies.service.ts:97-98, movies.component.ts:62-64
- **Detail**: Every 400 is shown as "Ocena musi być liczbą od 1 do 10." (also for search/malformed body); a failed search's error is not cleared by a later successful search.
- **Fix**: Map 400 per operation; clear the error on a successful search.
- **Decision**: FIXED (400 mapped per operation; search error cleared on next success; spec added)

### F9 — Search `q` length uncapped; leading-wildcard ILIKE scans

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Endpoints/MoviesEndpoints.cs:22
- **Detail**: Trivial at 498 rows; matters only if the catalog grows by orders of magnitude.
- **Fix**: Cap `q` at 100 characters now; `pg_trgm` GIN index only when the catalog grows.
- **Decision**: FIXED (q capped at 100 chars + smoke test)

### F10 — Phase 2 manual UI checks verified via API only

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/rate-watched-movies/plan.md (Progress 2.4–2.7, 3.5)
- **Detail**: Rows 2.4–2.7 and 3.5 were checked through the dev proxy / production API, not in a browser; autocomplete behaviour, empty-state rendering and toolbar links were not seen.
- **Fix**: Click through https://moviedate-sk.fly.dev/movies once and annotate the rows accordingly.
- **Decision**: FIXED (Progress rows annotated; browser click-through pending by the user)
