# Ranked Match List Implementation Plan

## Overview

Roadmap slice **S-05** (`ranked-match-list`): a logged-in user who has set a city and a display name presses "Szukaj dopasowań" and sees a ranked list of other users scored on shared highly-rated movies, shared watchlist movies and distance-decayed city proximity. The list is stored and append-only — a candidate who once appeared is never removed by a later recompute, only re-scored — and it is mirrored both ways. When strong matches are scarce, the closest users top the list up to 5. PRD refs: FR-005, US-01, NFR-01, NFR-04, §Business Logic. The asynchronous recompute on every movie write is S-06; here the recompute runs when the user asks for it.

## Current State Analysis

- All scoring inputs exist: `WatchedMovie` (1–10 rating, PK `(UserId, MovieId)`) in `Data/WatchedMovie.cs`, `WatchlistItem` in `Data/WatchlistItem.cs`, home city `AppUser.CityId → City` with server-only `Latitude`/`Longitude` in `Data/AppUser.cs` / `Data/City.cs`.
- Nothing matching-related exists: no table, service, endpoint, contract or screen.
- Users have no public identity — only an email (`ProfileResponse(string Email, CityResponse? City)` in `Contracts/ProfileResponse.cs`). Showing emails to strangers is not acceptable, so a display name is needed.
- Production holds only throwaway `e2e-*@moviedate.test` accounts, mostly without a city (`context/deployment/deploy-plan.md`), so "more than one candidate" cannot be shown without creating accounts.
- Established patterns: per-feature `Endpoints/XxxEndpoints.cs` with `AddAppXxx` + `MapXxxEndpoints`, services in `Services/` returning outcome records (`Services/WatchlistService.cs:9-26`), contracts in `Contracts/`, 409s as `Results.Problem` (`Endpoints/WatchlistEndpoints.cs:54`), integration tests on Testcontainers via `PostgresAppFixture` + `TestUsers.NewSignedInClient`, Angular feature folders with a service + spec and `mapErrors` (`ClientApp/src/app/shared/api-errors.ts`).

## Desired End State

- `/profile` lets the user set a display name (1–40 chars); home nudges when the city or the name is missing and links to `/matches`.
- `/matches` (guarded) shows the stored ranked list instantly and has a "Szukaj dopasowań" button that recomputes and refreshes it. Each entry shows display name, city name + voivodeship, score 0–100, and up to 5 shared favorite and 5 shared watchlist titles — never an email, never coordinates or a distance.
- A user without a city or display name gets a clear message linking to `/profile` instead of a recompute.
- In production, after running the demo-account script, a fresh real user with a city, name and a few ratings gets more than one ranked candidate.

Verify via `dotnet test tests/MovieDate.Tests`, the Angular tests, and the production flow in Phase 4.

### Key Discoveries:

- `IdentityUser` subclass already carries profile data (`Data/AppUser.cs`), and `ProfileService.SetCityAsync` shows the safe direct-update pattern that rotates `ConcurrencyStamp` (`Services/ProfileService.cs:41-66`) — reuse it for the display name.
- `WatchlistService` documents the execution-strategy rule: user-initiated transactions must run inside `CreateExecutionStrategy()` because `EnableRetryOnFailure` is on (`Services/WatchlistService.cs:107-115`).
- The `set-home-city` review accepted moving catalog/city lookups into services when S-05 needs them (`context/archive/2026-11-03-set-home-city/reviews/impl-review.md:92-94`).
- Lessons: migrations must be additive (expand/contract); per-user data keyed on `AspNetUsers.Id`, never on email (`context/foundation/lessons.md`).

## What We're NOT Doing

- No recompute on movie/watchlist writes and no background job — S-06.
- No friends or messaging — S-07/S-08. The response exposes the candidate's user id so S-07 can reference it, nothing more.
- No relative "above own average" favorites; the threshold is a fixed rating ≥ 7.
- No removal of list entries for any reason other than account deletion (FK cascade); a candidate who later becomes ineligible keeps their last score.
- No paging of the match list; no exposure of distance in km.
- No opt-in demo seeder inside the app; production demo users come from an external script that uses the public API.
- No clearing of the display name (set/change only, like the city).
- No uniqueness constraint on display names.

## Implementation Approach

Expand the schema first (display name column + `Matches` table in one additive migration) and expose the display name in the profile API. Then put the business rule in two services: `MatchScoringService` — pure, DB-free functions (proximity decay, taste curve, composite score) that unit tests pin down — and `MatchService`, which loads the inputs, picks which candidates to add, and upserts rows in both directions. S-06 will call the same `MatchService.RecomputeAsync` from its background job. The SPA then gets the profile field and the `/matches` screen. Finally a Node script fills production with demo accounts through the public API, and the deploy is verified.

**Scoring rule (fixed by the planning interview):**

- Favorite = a movie the user rated **≥ 7**. Shared favorites `H` = movies both users rated ≥ 7. Shared watchlist `W` = movies on both users' watchlists.
- Taste `t = r / (r + 4)` with `r = 2H + W` (t = 0 when nothing is shared; two shared favorites → 0.5).
- Proximity `p(d) = 1 / (1 + (d / 100)^k)` with `k = ln 3 / ln 5 ≈ 0.6826` and `d` = great-circle (haversine) distance between the two cities in km → p(0) = 1, p(100) = 0.5, p(500) = 0.25, never 0.
- Score = `50·t + 50·p`, range (0, 100]. Every input is symmetric, so score(A, B) = score(B, A).
- **Strong** candidate = `H + W ≥ 1`.

**Recompute rule for requester A:**

1. A must have a city and a display name, otherwise `ProfileIncomplete` (409) and nothing is written.
2. Eligible candidates = every other user with a city and a display name.
3. Re-score every existing row `A → X` whose X is still eligible (ineligible X keep their last score).
4. Add up to **20** strong, not-yet-listed candidates, highest score first.
5. If A's list would still have fewer than **5** entries, add the closest not-yet-listed, non-strong eligible users (ascending distance, then user id) until 5 or none left.
6. For every row `A → X` written or re-scored, upsert the mirror `X → A` with the same score and counts (keeping X's original `FirstMatchedAt` if the row exists).
7. Order of the list: score desc, then `FirstMatchedAt` asc, then candidate id.

## Critical Implementation Details

- **Concurrency & append-only:** two recomputes (A's and B's, or a double click) can touch the same row pair. Write rows with `INSERT … ON CONFLICT ("UserId", "CandidateId") DO UPDATE SET` score/counts/`UpdatedAt` (never `FirstMatchedAt`), via `ExecuteSqlInterpolatedAsync` inside the execution strategy. Never issue a `DELETE` on `Matches` from application code.
- **Performance constraint:** NFR-01 (< 2 s) applies to `GET /api/matches`, which must be a pure read (one query for rows + candidate city/name, one for shared titles). Recompute may be slower, but should compute `H`/`W` per candidate with grouped SQL joins against A's favorite/watchlist movie ids, not by loading every user's movies.

## Phase 1: Backend — display name + Matches schema

### Overview

Add the data model this slice needs and expose the display name in the profile API.

### Changes Required:

#### 1. Entities and DbContext

**File**: `Data/AppUser.cs`, `Data/Match.cs` (new), `Data/AppDbContext.cs`

**Intent**: Give users a public display name and store each user's match list.

**Contract**: `AppUser.DisplayName: string?` (max 40, `DisplayNameMaxLength` const). `Match { string UserId; string CandidateId; double Score; int SharedFavorites; int SharedWatchlist; DateTimeOffset FirstMatchedAt; DateTimeOffset UpdatedAt }` — PK `(UserId, CandidateId)`, FK `UserId → AspNetUsers.Id` cascade, FK `CandidateId → AspNetUsers.Id` cascade, index on `CandidateId`, check constraint `"UserId" <> "CandidateId"`. `DbSet<Match> Matches`.

#### 2. Migration

**File**: `Data/Migrations/<timestamp>_AddMatches.cs`

**Intent**: One additive migration: nullable `AspNetUsers.DisplayName` column + `Matches` table.

**Contract**: `Up` contains only `AddColumn`/`CreateTable`/`CreateIndex` (FKs/check inside `CreateTable`); generated with `dotnet ef migrations add AddMatches -o Data/Migrations`.

#### 3. Contracts

**File**: `Contracts/ProfileResponse.cs`, `Contracts/SetDisplayNameRequest.cs` (new)

**Intent**: Return the display name with the profile and accept it on update.

**Contract**: `ProfileResponse(string Email, string? DisplayName, CityResponse? City)`; `SetDisplayNameRequest(string DisplayName)`.

#### 4. Service

**File**: `Services/ProfileService.cs`

**Intent**: Set the display name with the same direct-update + `ConcurrencyStamp` rotation as the city; validate after trimming.

**Contract**: `SetDisplayNameAsync(string userId, string displayName, CancellationToken)` → `ProfileResult`; new outcome `InvalidDisplayName` when the trimmed value is empty or longer than 40 chars; `NotFound` when the user row is gone. `GetAsync` projects `DisplayName`.

#### 5. Endpoint

**File**: `Endpoints/ProfileEndpoints.cs`

**Intent**: Expose the display-name update.

**Contract**: `PUT /api/profile/display-name` (authorized) → 200 `ProfileResponse` | 400 `ValidationProblem` (`displayName`) | 404.

#### 6. Integration tests

**File**: `tests/MovieDate.Tests/ProfileTests.cs`

**Intent**: Cover the new field.

**Contract**: new user → `displayName: null`; set "  Ania " → stored "Ania"; empty/whitespace and 41 chars → 400; change persists; anonymous → 401; another user's profile unaffected.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddMatches` exists and is additive (only `AddColumn`/`CreateTable`/`CreateIndex` in `Up`)
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Local migrate applies `AddMatches` cleanly: `dotnet run -- --migrate` exits 0 against the compose DB

#### Manual Verification:

- Via `movie-date.http` (logged in): set and change the display name; profile shows it; whitespace-only returns 400

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Backend — scoring + match API

### Overview

Implement the scoring rule, the append-only mirrored recompute and the list read, behind two endpoints.

### Changes Required:

#### 1. Scoring

**File**: `Services/MatchScoringService.cs` (new)

**Intent**: Pure, DB-free formulas so the business rule is unit-testable in isolation.

**Contract**: static (or stateless) members: `FavoriteThreshold = 7`, `DistanceKm(lat1, lon1, lat2, lon2)` (haversine, R = 6371), `Proximity(double km)`, `Taste(int sharedFavorites, int sharedWatchlist)`, `Score(int sharedFavorites, int sharedWatchlist, double km)` → 0–100, `IsStrong(int sharedFavorites, int sharedWatchlist)`. Constants per the scoring rule in Implementation Approach.

#### 2. Contracts

**File**: `Contracts/MatchResponse.cs` (new)

**Intent**: One list entry, exposing only city-level data and no email.

**Contract**: `MatchResponse(string UserId, string DisplayName, CityResponse City, int Score, int SharedFavoritesCount, int SharedWatchlistCount, IReadOnlyList<MovieResponse> SharedFavorites, IReadOnlyList<MovieResponse> SharedWatchlist, DateTimeOffset FirstMatchedAt)`. `Score` is the stored score rounded to an integer. Counts come from the stored row (as of the last recompute); the title lists are read live, at most 5 each, ordered by title.

#### 3. Match service

**File**: `Services/MatchService.cs` (new)

**Intent**: Own the recompute and list rules; S-06 will call `RecomputeAsync` from its background job.

**Contract**: `ListAsync(string userId, CancellationToken)` → `IReadOnlyList<MatchResponse>` in list order (score desc, `FirstMatchedAt` asc, candidate id). `RecomputeAsync(string userId, CancellationToken)` → `MatchResult` with outcomes `Success` (value = the list) | `ProfileIncomplete` | `NotFound` (user row gone); implements recompute steps 1–7 from Implementation Approach, writing all rows of one recompute in a single transaction inside the execution strategy, via `ON CONFLICT` upserts.

#### 4. Endpoints

**File**: `Endpoints/MatchesEndpoints.cs` (new), `Program.cs`

**Intent**: Expose the list and the recompute, following the per-feature endpoint pattern.

**Contract**: `AddAppMatches()` registers `MatchService`; `MapMatchesEndpoints()` maps a group `/api/matches` with `RequireAuthorization()`: `GET /` → 200 list (possibly empty); `POST /recompute` → 200 list | 409 `Problem` with title `profile_incomplete` | 404. Both registered in `Program.cs` above the SPA fallback.

#### 5. Unit tests

**File**: `tests/MovieDate.Tests/MatchScoringTests.cs` (new)

**Intent**: Pin the formulas.

**Contract**: `Proximity(0) == 1`, `Proximity(100) ≈ 0.5`, `Proximity(500) ≈ 0.25`, `Proximity(5000) > 0` and decreasing; `Taste(0,0) == 0`, `Taste(2,0) == 0.5`, `Taste(1,2) == 0.5`; `Score` symmetric bounds (0, 100]; `IsStrong` boundaries; known distance Warszawa–Kraków ≈ 252 km (±5).

#### 6. Integration tests

**File**: `tests/MovieDate.Tests/MatchesTests.cs` (new)

**Intent**: Prove the business rules end to end through the API. Users are built via the public API (register, city, name, ratings, watchlist); assertions only consider rows whose candidates the test created, since the Postgres container is shared across the collection.

**Contract**:
- Anonymous `GET`/`POST` → 401.
- No city or no display name → recompute 409; no rows written.
- Strong match ranks above a non-strong closer user when its score is higher; rating 7/7 counts as a shared favorite, 6/9 does not.
- Top-up: with no strong candidates the list gets the closest users (up to 5).
- Mirror: after A's recompute, B's `GET` contains A with the same score.
- Append-only: after A removes the shared movie and recomputes, B is still listed with a lower score; a candidate who no longer qualifies is never deleted.
- Concurrent recomputes by A and B complete without errors or duplicate rows.
- Responses contain no `email`, `latitude`, `longitude` or distance field (NFR-04).
- Deleting a candidate account removes them from lists (cascade).

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Tests pass, including the new scoring and matches tests: `dotnet test tests/MovieDate.Tests`
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5069/api/matches` → `401` with the app running

#### Manual Verification:

- Via `movie-date.http` with two local accounts sharing a movie: recompute on one, both lists show each other with the same score; no emails or coordinates in responses

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Frontend — display name + /matches screen

### Overview

Let users set their display name and see/refresh their match list.

### Changes Required:

#### 1. Profile

**File**: `ClientApp/src/app/profile/profile.service.ts`, `profile.component.ts/.html`, `profile.service.spec.ts`

**Intent**: Add a display-name field with save (1–40 chars, trimmed) next to the city picker.

**Contract**: `ProfileService.setDisplayName(name)` → `PUT /api/profile/display-name`; profile model gains `displayName: string | null`; 400 maps to a Polish validation message.

#### 2. Matches feature

**File**: `ClientApp/src/app/matches/matches.service.ts` (+ `.spec.ts`), `matches.component.ts/.html`, `ClientApp/src/app/app.routes.ts`

**Intent**: Guarded `/matches` ("Dopasowania") screen: loads the stored list on open, "Szukaj dopasowań" button runs the recompute with a progress indicator, renders entries (name, city + voivodeship, score, shared titles), an empty state inviting the first search, and on 409 a message linking to `/profile`.

**Contract**: `MatchesService.list()` → `GET /api/matches`; `MatchesService.recompute()` → `POST /api/matches/recompute`; both via `mapErrors` with a `conflict` message ("Uzupełnij miasto i nazwę w profilu, aby szukać dopasowań."). Route `matches`, lazy-loaded, `authGuard`, title "Dopasowania".

#### 3. Home

**File**: `ClientApp/src/app/home/home.component.ts/.html`

**Intent**: Add a "Dopasowania" link and a nudge when the display name is missing (alongside the existing city nudge).

**Contract**: Home reads `displayName` from the profile; shows "Ustaw swoją nazwę" → `/profile` when null.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `cd ClientApp && npx ng test --watch=false`
- Frontend production build passes: `cd ClientApp && npm run build`
- Docker image builds: `docker build -t moviedate-local .`

#### Manual Verification:

- New account: home shows both nudges; after setting name and city on `/profile` they disappear
- `/matches` without a city/name shows the profile message after pressing the button
- With two local accounts sharing movies: the button shows a progress state, then a ranked list with names, cities, scores and shared titles; reload keeps the list
- Logged out, `/matches` redirects to login and returns after login

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Production — deploy + demo accounts

### Overview

Ship the slice, create demo accounts in production through the public API, and verify more than one ranked candidate.

### Changes Required:

#### 1. Demo-account script

**File**: `scripts/demo-accounts.mjs` (new), `scripts/README.md` (new, short)

**Intent**: A dependency-free Node script (built-in `fetch`) that registers ~10 demo accounts `demo-01..10@moviedate.test` and fills their display name, city, ratings and watchlist through the public API, so the production demo is reproducible. Safe to re-run: existing accounts are logged into, duplicates (409) are tolerated, ratings are updated.

**Contract**: env `BASE_URL` (default `http://localhost:5069`) and `DEMO_PASSWORD` (required; never committed or logged). Profiles are hard-coded in the script: varied cities (e.g. Kraków ×2, Warszawa, Wrocław, Gdańsk, Rzeszów, Zakopane, Białystok…) and overlapping popular titles found via `/api/movies?q=`, with ratings on both sides of 7. Prints a summary per account; exits non-zero on any unexpected status.

#### 2. Deploy (human)

**Intent**: `fly deploy --remote-only --app moviedate-sk`; the release command applies `AddMatches`.

#### 3. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the new known-good release, the `AddMatches` migration, the demo accounts (no password) and the rollback caveat.

**Contract**: Section "What's still open".

### Success Criteria:

#### Automated Verification:

- Script works locally: `DEMO_PASSWORD=... node scripts/demo-accounts.mjs` exits 0 twice in a row against the local app
- Release completes: `fly releases -a moviedate-sk` shows a new `complete` release
- `curl -s -o /dev/null -w "%{http_code}" https://moviedate-sk.fly.dev/api/matches` → `401`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`

#### Manual Verification:

- Script run against production (`BASE_URL=https://moviedate-sk.fly.dev`) exits 0
- Prod flow: a fresh account sets name + city, rates a few popular movies, presses "Szukaj dopasowań" → more than one ranked candidate, with no emails or coordinates; a demo account's list now contains the fresh account (mirror)
- `GET /api/matches` responds in < 2 s on a warm machine
- `deploy-plan.md` updated with the new known-good release

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `MatchScoringService`: decay anchor points (0/100/500 km), never-zero tail, taste curve, score bounds and symmetry, strong-match boundary, haversine against a known city pair.
- Angular `ProfileService` and `MatchesService`: URLs, payloads, 400/409 messages.

### Integration Tests:

- API on Testcontainers Postgres: display name validation; eligibility 409; ranking; ≥ 7 threshold boundary; top-up to 5; mirror; append-only after data change; concurrent recomputes; cascade on account deletion; isolation; NFR-04 guardrail (no email/coordinates/distance).

### Manual Testing Steps:

1. Local profile + matches flow with two accounts (Phases 1–3).
2. Production flow with demo accounts and a fresh account (Phase 4).

## Performance Considerations

`GET /api/matches` is a read of the user's rows joined to candidate name/city plus a capped shared-titles query — well within NFR-01 at small scale. Recompute scans eligible users once and computes `H`/`W` with grouped joins against the requester's movie ids; at the target scale (small) this is sub-second. The Fly trial machine cold start (deploy-plan "Fly trial limit") can still exceed 2 s on the first request.

## Migration Notes

`AddMatches` adds a nullable `DisplayName` column and a new `Matches` table — backward-compatible: the running release never reads or writes either. Rolling back leaves both unused. No data backfill: existing users have no name until they set one, so they are not eligible for matching until then.

## References

- Roadmap item: `context/foundation/roadmap.md` → S-05
- PRD: `context/foundation/prd.md` → FR-005, US-01, §Business Logic, NFR-01, NFR-04
- Lessons: `context/foundation/lessons.md`
- Similar implementation: `Services/ProfileService.cs:41-66` (direct update), `Services/WatchlistService.cs:96-160` (execution strategy + transaction), `Endpoints/WatchlistEndpoints.cs:54` (409 problem)
- Prior slice plan: `context/archive/2026-11-03-set-home-city/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — display name + Matches schema

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 001ccf5
- [x] 1.2 Migration `AddMatches` exists and is additive — 001ccf5
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 001ccf5
- [x] 1.4 Local migrate applies `AddMatches` cleanly — 001ccf5

#### Manual

- [x] 1.5 Set and change the display name via `movie-date.http`; whitespace-only returns 400 — 001ccf5

### Phase 2: Backend — scoring + match API

#### Automated

- [x] 2.1 App builds: `dotnet build movie-date.csproj` — e9d9d2b
- [x] 2.2 Tests pass, including the new scoring and matches tests — e9d9d2b
- [x] 2.3 `/api/matches` → `401` with the app running — e9d9d2b

#### Manual

- [x] 2.4 Two local accounts sharing a movie see each other with the same score; no emails or coordinates — e9d9d2b

### Phase 3: Frontend — display name + /matches screen

#### Automated

- [x] 3.1 Frontend tests pass: `npx ng test --watch=false` — 5c617b2
- [x] 3.2 Frontend production build passes — 5c617b2
- [x] 3.3 Docker image builds — 5c617b2

#### Manual

- [ ] 3.4 New account: home nudges for name and city disappear after setting them
- [x] 3.5 `/matches` without city/name shows the profile message
- [x] 3.6 Two local accounts: progress state, ranked list with names, cities, scores, shared titles; reload keeps it
- [ ] 3.7 Logged-out `/matches` redirects to login and returns

### Phase 4: Production — deploy + demo accounts

#### Automated

- [x] 4.1 Demo script exits 0 twice in a row locally
- [x] 4.2 Release completes on Fly
- [x] 4.3 Production `/api/matches` → `401`
- [x] 4.4 Production `/health` → ok

#### Manual

- [x] 4.5 Demo script run against production exits 0
- [x] 4.6 Prod flow: fresh account gets more than one ranked candidate; mirror visible on a demo account
- [x] 4.7 `GET /api/matches` < 2 s on a warm machine
- [x] 4.8 `deploy-plan.md` updated with the new known-good release
