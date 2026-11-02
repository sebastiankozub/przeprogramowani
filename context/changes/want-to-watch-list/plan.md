# Want-to-Watch List Implementation Plan

## Overview

Roadmap slice **S-04** (`want-to-watch-list`): a logged-in user adds catalog movies to a "want to watch" list, removes them, sees the list, and can mark an entry as watched with a 1–10 rating, which atomically moves it to the rated list from S-02. PRD refs: FR-003, US-01. Shared watchlist movies feed matching in S-05; the asynchronous recompute is S-06.

## Current State Analysis

- S-02 delivered `Movies` (498-film Wikidata catalog seeded by `--migrate`), `WatchedMovies`, `Services/WatchedMoviesService` (enum results `WatchedMovieOutcome`/`WatchedMovieResult`, rating 1–10, 409 on duplicate, FK-violation → NotFound), `Endpoints/MoviesEndpoints.cs` (`GET /api/movies?q=`) and `Endpoints/WatchedMoviesEndpoints.cs` (`AddAppWatchedMovies` + `MapWatchedMoviesEndpoints`, group `RequireAuthorization()`, user id via `UserManager.GetUserId`), tests via `TestUsers.NewSignedInClient` on the Testcontainers fixture.
- `Data/DataServiceExtensions.cs:24` enables `EnableRetryOnFailure` — user-initiated transactions must run inside `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`, otherwise EF throws.
- SPA: `/movies` (`ClientApp/src/app/movies/movies.component.*`) has the autocomplete search built in; `MoviesService` wraps `/api/movies` + `/api/watched` with `MoviesError(message, status)`; `/movies` is lazy-loaded (`loadComponent`) to keep the initial bundle under the 500 kB budget; toolbar links in `app.html` show only when signed in.
- Lessons: migrations additive only; per-user data keyed on the user id (`NameIdentifier`), never on email.

## Desired End State

- `WatchlistItems` stores `(UserId, MovieId, AddedAt)`; one additive migration.
- `GET /api/watchlist` lists the user's entries newest first; `POST /api/watchlist {movieId}` adds; `DELETE /api/watchlist/{movieId}` removes; `POST /api/watchlist/{movieId}/watched {rating}` moves the entry to the rated list in one transaction.
- The two lists are otherwise independent: a movie may be on both if the user adds it to each; neither list blocks the other.
- SPA `/watchlist` ("Do obejrzenia", guarded) with the shared movie search, the list with remove and a rating + "Obejrzałem" action; reachable from toolbar and home. `/movies` uses the same search component, behaviour unchanged.
- Verified on https://moviedate-sk.fly.dev.

### Key Discoveries:

- The catalog search API needs no change — the watchlist screen reuses `GET /api/movies?q=` (S-02 review F5 concerned server-side query reuse, which this slice does not need).
- `WatchedMoviesService.AddAsync` already validates rating and duplicate and saves — "mark watched" composes it with the watchlist removal inside one execution-strategy transaction instead of duplicating the rules.
- A shared scoped `AppDbContext` serves both services within a request, so one transaction covers both writes.

## What We're NOT Doing

- No mutual-exclusion rule between the lists (user decision): adding to the watchlist a movie that is already rated is allowed, and rating a movie on `/movies` does not touch the watchlist. Only the explicit "Obejrzałem" action moves an entry.
- No async match recompute (S-06), no scoring (S-05).
- No catalog API changes, no notes/priority/ordering on watchlist entries, no pagination.
- No Playwright e2e; browser flow verified manually.

## Implementation Approach

Backend first: entity + migration, `WatchlistService` (list/add/remove/mark-watched) reusing `WatchedMoviesService` for the rating rules, `WatchlistEndpoints`, integration tests. Then the SPA: extract the autocomplete into a shared `MovieSearchComponent`, refactor `/movies` onto it, add the `/watchlist` screen and its service. Finally the production deploy.

## Critical Implementation Details

- **Transaction under the retry strategy:** "mark watched" wraps `WatchedMoviesService.AddAsync` + the watchlist delete in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` with an explicit transaction; any non-success outcome (400/404/409) rolls back so the entry stays on the watchlist.
- **Outcome order for mark-watched:** entry not on the user's watchlist → 404 (checked first, so a foreign or missing entry never reveals rating validation); then rating → 400; then already rated → 409.

## Phase 1: Backend — watchlist API

### Overview

Storage, service with the move action, endpoints, integration tests.

### Changes Required:

#### 1. Entity and DbContext

**File**: `Data/WatchlistItem.cs` (new), `Data/AppDbContext.cs`

**Intent**: Watchlist entry per user and movie, mirroring `WatchedMovie` keys and delete behaviour.

**Contract**: `WatchlistItem { string UserId; int MovieId; DateTimeOffset AddedAt }` — PK `(UserId, MovieId)`, FK `UserId → AspNetUsers.Id` (cascade), FK `MovieId → Movies.Id` (restrict); `DbSet<WatchlistItem> WatchlistItems`.

#### 2. Migration

**File**: `Data/Migrations/*_AddWatchlist.cs` (generated)

**Intent**: `dotnet ef migrations add AddWatchlist --output-dir Data/Migrations`; additive only (lesson).

**Contract**: `Up` contains only `CreateTable` / `CreateIndex`.

#### 3. Contracts

**File**: `Contracts/WatchlistItemResponse.cs`, `Contracts/AddToWatchlistRequest.cs` (new)

**Intent**: Response/request records; mark-watched reuses `UpdateRatingRequest` and returns `WatchedMovieResponse`.

**Contract**: `WatchlistItemResponse(MovieResponse Movie, DateTimeOffset AddedAt)`; `AddToWatchlistRequest(int MovieId)`.

#### 4. Service

**File**: `Services/WatchlistService.cs` (new)

**Intent**: List (newest first), add (movie must exist; duplicate → Duplicate), remove (missing → NotFound), mark watched (see Critical Implementation Details) — all scoped to the user id; same enum-result style as `WatchedMoviesService` (new `WatchlistOutcome`/`WatchlistResult`, or reuse where it fits — implementer's choice). FK violation on insert → NotFound, unique violation race → Duplicate, as in S-02.

**Contract**: scoped service depending on `AppDbContext` and `WatchedMoviesService`; methods take `string userId`.

#### 5. Endpoints

**File**: `Endpoints/WatchlistEndpoints.cs` (new), `Program.cs`

**Intent**: `AddAppWatchlist` registers the service; `MapWatchlistEndpoints` maps the authenticated `/api/watchlist` group. `Program.cs` gains only the Add/Map calls above the SPA fallback.

**Contract**: `GET /api/watchlist` → 200 `WatchlistItemResponse[]`. `POST /api/watchlist` → 201 `WatchlistItemResponse` (no Location header, as in S-02) / 404 unknown movie / 409 already on the list. `DELETE /api/watchlist/{movieId}` → 204 / 404. `POST /api/watchlist/{movieId}/watched` `{rating}` → 200 `WatchedMovieResponse` / 404 not on the watchlist / 400 rating outside 1–10 / 409 already rated. All → 401 without a session.

#### 6. Integration tests

**File**: `tests/MovieDate.Tests/WatchlistTests.cs` (new), `movie-date.http`

**Intent**: add → list contains it; duplicate → 409; unknown movie → 404; remove → gone, second remove → 404; 401 on all four routes; user B cannot see or remove A's entry; a movie can be both rated and on the watchlist (independent lists); mark watched → 200, entry gone from the watchlist and present in `/api/watched` with the rating; mark watched with rating 0/11 → 400 and entry still on the watchlist; mark watched for an already-rated movie → 409 and entry still on the watchlist; mark watched for a movie not on the watchlist → 404. Example requests in `movie-date.http`.

**Contract**: test class in `PostgresAppCollection`, using `TestUsers`.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddWatchlist` exists and is additive (only `CreateTable`/`CreateIndex` in `Up`)
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Local migrate applies `AddWatchlist`: `dotnet run --project movie-date.csproj -- --migrate`
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5069/api/watchlist` → `401` with the app running

#### Manual Verification:

- Via `movie-date.http` (logged in): add, list, mark watched (moves to `/api/watched`), remove behave per contract

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — /watchlist screen

### Overview

Shared movie search component, watchlist service and screen, navigation.

### Changes Required:

#### 1. Shared movie search

**File**: `ClientApp/src/app/movies/movie-search.component.ts` + `.html` (new), `ClientApp/src/app/movies/movies.component.*`

**Intent**: Extract the autocomplete (debounce 300 ms, ≥ 2 chars, `switchMap` search, "Tytuł (Oryginalny, rok)" label, search error handling) from `MoviesComponent` into a standalone component that emits the selected movie and can be reset by its parent; `/movies` uses it with unchanged behaviour.

**Contract**: input for the field label; output `movieSelected: Movie | null`; a `reset()` method (or equivalent) the parent calls after a successful add.

#### 2. Watchlist service

**File**: `ClientApp/src/app/watchlist/watchlist.service.ts` (new)

**Intent**: Wrap `/api/watchlist` (list, add, remove, markWatched) with the same `MoviesError(message, status)` mapping style as `MoviesService`; Polish messages for 409 ("Ten film jest już na liście do obejrzenia." on add; "Ten film jest już na liście obejrzanych — zmień ocenę na ekranie Obejrzane filmy." on mark watched), 404 and 400.

**Contract**: `list()`, `add(movieId)`, `remove(movieId)`, `markWatched(movieId, rating)` returning Observables.

#### 3. Screen

**File**: `ClientApp/src/app/watchlist/watchlist.component.ts` + `.html` (new), `ClientApp/src/app/app.routes.ts`

**Intent**: Route `watchlist` (lazy, guarded, title "Do obejrzenia"). Top: shared search + "Dodaj" (disabled until a movie is chosen and while pending). List newest first: title/original/year, remove button, a 1–10 rating select and "Obejrzałem" (enabled once a rating is chosen); on success the entry disappears from the list. Inline errors; on 404/409 reload the list (same stale-state handling as `/movies`). Empty state: "Twoja lista do obejrzenia jest pusta."

**Contract**: `/watchlist` declared before the `**` redirect, `canActivate: [authGuard]`, `loadComponent`.

#### 4. Navigation

**File**: `ClientApp/src/app/app.html`, `ClientApp/src/app/home/home.component.html`

**Intent**: Toolbar link "Do obejrzenia" next to "Obejrzane filmy" (signed-in only); home link to `/watchlist`.

**Contract**: existing `app.spec.ts` assertions keep passing.

#### 5. Tests

**File**: `ClientApp/src/app/watchlist/watchlist.service.spec.ts` (new)

**Intent**: Vitest + `HttpTestingController`: `add` posts `{movieId}`; `markWatched` posts `{rating}` to `/api/watchlist/{id}/watched`; 409 on add vs on markWatched yields the two distinct messages.

**Contract**: runs under `ng test`; existing specs keep passing.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `cd ClientApp && npx ng test --watch=false`
- Frontend production build passes within budget: `cd ClientApp && npm run build`
- Docker image builds: `docker build -t moviedate-local .`

#### Manual Verification:

- `/movies` still works after the search refactor (search, add, change rating, remove)
- `/watchlist`: add a movie, duplicate shows the message, remove works, empty state shown
- "Obejrzałem" with rating 8 removes it from `/watchlist` and it appears on `/movies` with 8
- Logged out, `/watchlist` redirects to login and returns after login

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production

### Overview

Deploy, verify on production, record it.

### Changes Required:

#### 1. Deploy (human)

**Intent**: `fly deploy --remote-only --app moviedate-sk`; the release command applies `AddWatchlist` (seed: 0 inserted, 0 updated).

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record v8 (S-02 review fixes) and the new known-good release with the watchlist verification.

**Contract**: Section "What's still open".

### Success Criteria:

#### Automated Verification:

- Release completes: `fly releases -a moviedate-sk` shows a new `complete` release
- `curl -s -o /dev/null -w "%{http_code}" https://moviedate-sk.fly.dev/api/watchlist` → `401`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`

#### Manual Verification:

- Prod flow: add to watchlist, mark watched (moves to rated list), remove; survives reload
- `deploy-plan.md` updated with the new known-good release

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Angular `WatchlistService`: URLs, payloads, distinct 409 messages; existing `MoviesService` spec still green after the refactor.

### Integration Tests:

- API on Testcontainers Postgres: CRUD, 401/404/409, isolation, independent lists, mark-watched move and its rollback on 400/409/404.

### Manual Testing Steps:

1. Local browser flow on `/watchlist` and a regression pass on `/movies` (Phase 2).
2. Production flow (Phase 3).

## Performance Considerations

Single-user lists of tens of rows; one indexed query each (PK `(UserId, MovieId)`). Mark-watched is one short transaction.

## Migration Notes

`AddWatchlist` only creates `WatchlistItems` — backward-compatible with the running release, which never touches it. Rolling back leaves the table unused.

## References

- Roadmap item: `context/foundation/roadmap.md` → S-04
- PRD: `context/foundation/prd.md` → FR-003, US-01
- Lessons: `context/foundation/lessons.md`
- Prior change: `context/archive/2026-11-02-rate-watched-movies/` (patterns, review F2/F5/F8)
- Patterns: `Services/WatchedMoviesService.cs`, `Endpoints/WatchedMoviesEndpoints.cs`, `tests/MovieDate.Tests/WatchedMoviesTests.cs`, `ClientApp/src/app/movies/*`

## Review Addenda (implementation, 2026-11-03)

- **Re-watch (user decision during Phase 2):** "Obejrzałem" on a movie that is already rated no longer returns 409 — it overwrites the rating with the new one (latest opinion wins, as on /movies) and removes the watchlist entry in the same transaction. 409 remains only for a concurrent-rating race. Outcome order is now: not on the watchlist → 404, then rating → 400. Supersedes the "already rated → 409" parts of Critical Implementation Details, Phase 1 §5–6 and the mark-watched 409 message in Phase 2 §2.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — watchlist API

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 1949428
- [x] 1.2 Migration `AddWatchlist` exists and is additive — 1949428
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 1949428
- [x] 1.4 Local migrate applies `AddWatchlist` — 1949428
- [x] 1.5 `/api/watchlist` → `401` with the app running — 1949428

#### Manual

- [x] 1.6 Add, list, mark watched, remove per contract via `movie-date.http` — 1949428

### Phase 2: Frontend — /watchlist screen

#### Automated

- [x] 2.1 Frontend tests pass: `npx ng test --watch=false` — 21eb865
- [x] 2.2 Frontend production build passes within budget — 21eb865
- [x] 2.3 Docker image builds — 21eb865

#### Manual

- [x] 2.4 `/movies` still works after the search refactor — 21eb865
- [x] 2.5 `/watchlist` add, duplicate message, remove, empty state — 21eb865
- [x] 2.6 "Obejrzałem" with rating 8 moves the movie to `/movies` — 21eb865
- [x] 2.7 Logged-out `/watchlist` redirects to login and returns — 21eb865

### Phase 3: Production

#### Automated

- [x] 3.1 New release completes
- [x] 3.2 Prod `/api/watchlist` → `401`
- [x] 3.3 Prod `/health` → `{"status":"ok"}`

#### Manual

- [x] 3.4 Prod flow: add, mark watched, remove; survives reload
- [x] 3.5 `deploy-plan.md` updated with the new known-good release
