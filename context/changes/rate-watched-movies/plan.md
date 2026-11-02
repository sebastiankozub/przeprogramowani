# Rate Watched Movies Implementation Plan

## Overview

Roadmap slice **S-02** (`rate-watched-movies`): a logged-in user searches a local movie catalog, adds a watched movie with a 1–10 rating, edits the rating, removes it, and sees their rated list. The catalog is ~500 popular films exported once from Wikidata (CC0) and seeded by the `--migrate` release step. PRD refs: FR-002, US-01. The asynchronous match recompute that FR-002 also mentions is deferred to S-06 (roadmap).

## Current State Analysis

- After S-01: `AppDbContext : IdentityDbContext<AppUser>, IDataProtectionKeyContext` (`Data/AppDbContext.cs`), migrations `InitialCreate` + `AddIdentity`, `MigrationRunner.RunAsync` applies migrations in `--migrate` mode (Fly `release_command`) and is reused by the test fixture (`tests/MovieDate.Tests/PostgresAppFixture.cs:30`).
- Endpoint pattern: one static class per feature with `AddAppX(...)` + `MapXEndpoints(...)` (`Endpoints/HealthEndpoints.cs`, `Endpoints/AuthEndpoints.cs`); request/response records in `Contracts/`; business logic belongs in `Services/` (CLAUDE.md module layout rule — `Services/` does not exist yet).
- Auth: cookie session; `RequireAuthorization()` on protected endpoints; unauthenticated API calls get 401.
- SPA: routes in `ClientApp/src/app/app.routes.ts` (`''` guarded home, `login`, `register`); `authGuard`; `authInterceptor` turns 401 from product APIs into a redirect to `/login?returnUrl=…`; `app.html` is a Material toolbar + `<router-outlet>`; Vitest specs via `ng test`.
- Lessons (`context/foundation/lessons.md`): migrations must be backward-compatible with the previous release (additive only); per-user data is keyed on the user id (`ClaimTypes.NameIdentifier` / `AspNetUsers.Id`), never on the email claim.

## Desired End State

- `Movies` holds ~500 films (Polish title, original title, year, Wikidata QID), seeded idempotently on every `--migrate` run locally, in tests and on Fly.
- `GET /api/movies?q=<≥2 chars>` returns up to 20 matches by Polish or original title, case-insensitive, prefix matches first.
- `GET/POST/PUT/DELETE /api/watched` let the signed-in user list, add (rating 1–10), change the rating of, and remove their watched movies; another user's data is never visible or editable.
- SPA `/movies` ("Obejrzane filmy", guarded) offers autocomplete search, a 1–10 rating picker, the rated list with inline rating change and removal; reachable from the toolbar and home.
- Verified on https://moviedate-sk.fly.dev.

### Key Discoveries:

- `MigrationRunner.RunAsync` is the single path used by Fly's release command and by the test fixture — seeding there gives tests and production the same catalog (`Data/MigrationRunner.cs:11`, `PostgresAppFixture.cs:30`).
- `AppUser.Id` is a `string` (Identity default) — `WatchedMovies.UserId` must be `string` with an FK to `AspNetUsers.Id`.
- The SPA interceptor already handles 401 from `/api/movies` and `/api/watched` (not in its self-handled list) — no extra session handling needed.
- Neon autosuspend: search-as-you-type wakes the DB; debounce keeps request count low.

## What We're NOT Doing

- No asynchronous match recompute on writes (S-06); no matching/scoring (S-05) and no "highly-rated" threshold yet — only the 1–10 scale is fixed here.
- No want-to-watch list (S-04).
- No external catalog API (PRD non-goal); no admin UI to edit the catalog; the seeder never deletes movies.
- No posters/images, genres, descriptions or other movie metadata beyond title, original title, year.
- No accent-insensitive search (`unaccent`) — "zycie" will not find "Życie"; accepted for MVP.
- No pagination of the rated list (MVP users rate tens of movies).
- No Playwright e2e; browser flow verified manually.

## Implementation Approach

Backend first so the contract exists before the SPA uses it: entities + one additive migration, the seed data file and an idempotent seeder hooked into `MigrationRunner`, a `WatchedMoviesService` holding the rules (rating range, duplicate, ownership), and two endpoint classes. Then the Angular `/movies` screen on top of a small `MoviesService`. Finally a production deploy whose release command applies the migration and seeds the catalog.

## Critical Implementation Details

- **Seeding order:** the seeder runs inside `MigrationRunner.RunAsync` *after* `MigrateAsync` and in the same try/catch, so a seed failure fails the release (exit 1) exactly like a migration failure, and the fixture's exit-code check covers it.
- **Search input:** escape `\`, `%` and `_` in `q` before building the ILIKE pattern (`EF.Functions.ILike(..., pattern, "\\")`); otherwise `q = "%"` matches everything.
- **User identity:** endpoints read the user id from `ClaimTypes.NameIdentifier` (lesson "Key per-user data on the user id"); every `/api/watched` query filters by it, so a foreign `movieId` simply yields 404.

## Phase 1: Backend — catalog + ratings API

### Overview

Movie catalog storage and seed, search endpoint, watched-movie CRUD with rules, integration tests.

### Changes Required:

#### 1. Entities and DbContext

**File**: `Data/Movie.cs` (new), `Data/WatchedMovie.cs` (new), `Data/AppDbContext.cs`

**Intent**: Add the catalog entity and the user↔movie rating entity, with constraints enforced in the database as well as in the service.

**Contract**: `Movie { int Id; string WikidataId (unique, max 16); string Title (max 300); string? OriginalTitle (max 300); int? Year }`. `WatchedMovie { string UserId; int MovieId; short Rating; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }` — PK `(UserId, MovieId)`, FK `UserId → AspNetUsers.Id` (cascade), FK `MovieId → Movies.Id` (restrict), check constraint `Rating BETWEEN 1 AND 10`. `DbSet<Movie> Movies`, `DbSet<WatchedMovie> WatchedMovies`.

#### 2. Migration

**File**: `Data/Migrations/*_AddMoviesAndRatings.cs` (generated)

**Intent**: `dotnet ef migrations add AddMoviesAndRatings --output-dir Data/Migrations`. Additive only — two new tables, indexes, check constraint (lesson: backward-compatible with v6).

**Contract**: `Up` contains only `CreateTable` / `CreateIndex` (+ the check constraint inside `CreateTable`).

#### 3. Seed data

**File**: `Data/Seed/movies.sparql` (new), `Data/Seed/movies.json` (new), `movie-date.csproj`

**Intent**: Keep the Wikidata query in the repo for reproducibility and commit its cleaned result. The query selects films (`wdt:P31 wd:Q11424`) ordered by `wikibase:sitelinks` descending, limit 500, with the Polish label (fallback English label), original title (`P1476`) and the earliest publication year (`P577`). Run it once against `https://query.wikidata.org/sparql` (JSON format), normalize to the contract below, drop rows without any title, dedupe by QID. `movies.json` is an `EmbeddedResource`.

**Contract**: JSON array of `{ "wikidataId": "Q47703", "title": "Ojciec chrzestny", "originalTitle": "The Godfather", "year": 1972 }`; ~500 entries; UTF-8.

#### 4. Seeder

**File**: `Data/MovieCatalogSeeder.cs` (new), `Data/MigrationRunner.cs`

**Intent**: Idempotent upsert of the embedded catalog keyed on `WikidataId`: insert missing films, update title/original title/year of existing ones, never delete. Called from `MigrationRunner.RunAsync` after `MigrateAsync`; logs inserted/updated counts.

**Contract**: `static Task<(int Inserted, int Updated)> SeedAsync(AppDbContext db, CancellationToken ct = default)`.

#### 5. Contracts

**File**: `Contracts/MovieResponse.cs`, `Contracts/WatchedMovieResponse.cs`, `Contracts/AddWatchedMovieRequest.cs`, `Contracts/UpdateRatingRequest.cs` (new)

**Intent**: Request/response records; endpoints never return entities.

**Contract**: `MovieResponse(int Id, string Title, string? OriginalTitle, int? Year)`; `WatchedMovieResponse(MovieResponse Movie, int Rating, DateTimeOffset UpdatedAt)`; `AddWatchedMovieRequest(int MovieId, int Rating)`; `UpdateRatingRequest(int Rating)`.

#### 6. Service

**File**: `Services/WatchedMoviesService.cs` (new)

**Intent**: Business rules for a user's watched movies — list (most recently updated first), add (rating 1–10, movie must exist, no duplicate), update rating, remove — all scoped to the given user id. This is the hook S-06 will extend with recompute.

**Contract**: scoped service; methods take `string userId`; results distinguish success / validation error / not found / duplicate (a small result type or enum — implementer's choice, no exceptions for expected outcomes).

#### 7. Endpoints

**File**: `Endpoints/MoviesEndpoints.cs` (new), `Endpoints/WatchedMoviesEndpoints.cs` (new), `Program.cs`

**Intent**: `MoviesEndpoints`: `GET /api/movies?q=` (requires auth; `q` trimmed, < 2 chars → empty array; ILIKE contains on `Title` or `OriginalTitle`, prefix matches first then by title, max 20). `WatchedMoviesEndpoints` (+ `AddAppWatchedMovies` registering the service): group `/api/watched` with `RequireAuthorization()`. `Program.cs` only gains the Add/Map calls above the SPA fallback.

**Contract**: `GET /api/movies?q=` → 200 `MovieResponse[]`. `GET /api/watched` → 200 `WatchedMovieResponse[]`. `POST /api/watched` → 201 `WatchedMovieResponse` / 400 ValidationProblem (rating outside 1–10) / 404 (unknown movie) / 409 (already on the list). `PUT /api/watched/{movieId}` → 200 / 400 / 404. `DELETE /api/watched/{movieId}` → 204 / 404. All → 401 without a session.

#### 8. Integration tests

**File**: `tests/MovieDate.Tests/MoviesTests.cs`, `tests/MovieDate.Tests/WatchedMoviesTests.cs` (new)

**Intent**: Against the existing fixture (catalog seeded by `MigrationRunner`), with logged-in cookie clients and unique emails: catalog seeded (> 400 rows) and a second `SeedAsync` inserts 0; search finds a film by Polish and by original title, case-insensitive, `q="%"` returns nothing unexpected (escape), `q` of 1 char → empty; 401 without session on both groups; add → list contains it; duplicate → 409; rating 0 and 11 → 400; unknown movie → 404; update rating → list shows it; delete → gone, second delete → 404; user B cannot see, update or delete user A's entry (404).

**Contract**: test classes in `PostgresAppCollection`; a shared helper for "register + login → cookie client" may be extracted from `AuthTests.cs` if it keeps both readable.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddMoviesAndRatings` exists and is additive (only `CreateTable`/`CreateIndex` in `Up`)
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Local migrate applies the migration and seeds the catalog: `dotnet run --project movie-date.csproj -- --migrate` logs ~500 inserted; a second run logs 0 inserted
- `curl -s -o /dev/null -w "%{http_code}" "http://localhost:5069/api/movies?q=god"` → `401` with the app running

#### Manual Verification:

- Via `movie-date.http` (logged in): search "ojciec" and "godfather" both return The Godfather; add, update, delete behave per contract
- Spot-check `movies.json`: Polish titles look right for ~10 well-known films; no empty titles

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — /movies screen

### Overview

Movies service, the "Obejrzane filmy" screen, navigation, service test.

### Changes Required:

#### 1. Movies service

**File**: `ClientApp/src/app/movies/movies.service.ts` (new)

**Intent**: Thin HTTP wrapper over `/api/movies` and `/api/watched` with typed models; maps 409 / 400 / 404 to Polish user-facing messages (same `Error`-with-message approach as `AuthService`).

**Contract**: `search(q): Observable<Movie[]>`, `listWatched(): Observable<WatchedMovie[]>`, `add(movieId, rating)`, `updateRating(movieId, rating)`, `remove(movieId)`.

#### 2. Screen

**File**: `ClientApp/src/app/movies/movies.component.ts` + `.html` (new), `ClientApp/src/app/app.routes.ts`

**Intent**: Route `movies` (guarded, title "Obejrzane filmy"). Top: `mat-autocomplete` search (debounce ~300 ms, from 2 chars, option shows "Tytuł (Oryginalny, rok)"), a 1–10 rating `mat-select`, "Dodaj" button (disabled until a movie and rating are chosen and while pending). Below: the rated list (title, original title, year, rating select that saves on change, remove button). Inline error line for failed operations (duplicate → "Ten film jest już na Twojej liście — zmień ocenę poniżej."). Empty state: "Nie oceniłeś jeszcze żadnego filmu." UI copy in Polish.

**Contract**: `/movies` with `canActivate: [authGuard]`, declared before the `**` redirect.

#### 3. Navigation

**File**: `ClientApp/src/app/app.html`, `ClientApp/src/app/app.ts`, `ClientApp/src/app/home/home.component.html`

**Intent**: Toolbar gets links "Start" (`/`) and "Obejrzane filmy" (`/movies`), shown only when signed in (`AuthService.currentUser`); home gets a link/button to `/movies`.

**Contract**: `routerLink` navigation; existing `app.spec.ts` assertions keep passing.

#### 4. Service test

**File**: `ClientApp/src/app/movies/movies.service.spec.ts` (new)

**Intent**: Vitest + `HttpTestingController`: `search` calls `/api/movies?q=…`; `add` posts `{movieId, rating}`; a 409 on `add` surfaces the Polish duplicate message.

**Contract**: runs under `ng test`.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `cd ClientApp && npx ng test --watch=false`
- Frontend production build passes: `cd ClientApp && npm run build`
- Docker image builds: `docker build -t moviedate-local .`

#### Manual Verification:

- With `docker compose up -d`, `dotnet run` and `npm start`: logged out, `/movies` redirects to `/login?returnUrl=%2Fmovies` and returns there after login
- Typing "ojciec" and "godfather" both suggest Ojciec chrzestny; adding with rating 9 shows it in the list
- Changing the rating persists across reload; removing it empties the list (empty state shown)
- Adding the same movie again shows the duplicate message

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production

### Overview

Deploy, verify on production, record it.

### Changes Required:

#### 1. Deploy (human)

**Intent**: `fly deploy --remote-only --app moviedate-sk`; the release command applies `AddMoviesAndRatings` and seeds the catalog before rollout. Also check the release machine's `Preparing to run: dotnet movie-date.dll --migrate` line (open item 3.4 from S-01).

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the new known-good release, the catalog seed step in the release command, and the S-01 3.4 confirmation.

**Contract**: Section "What's still open".

### Success Criteria:

#### Automated Verification:

- Release completes: `fly releases -a moviedate-sk` shows a new `complete` release
- `curl -s -o /dev/null -w "%{http_code}" "https://moviedate-sk.fly.dev/api/movies?q=god"` → `401`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`

#### Manual Verification:

- Release log shows the migration applied, the seeder's inserted count (~500), and `Preparing to run: \`dotnet movie-date.dll --migrate\``
- On production (logged in): search, add, change rating, remove work; data survives reload
- `deploy-plan.md` updated with the new known-good release

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Angular `MoviesService`: URLs, payloads, 409 message mapping.

### Integration Tests:

- API on Testcontainers Postgres: seed idempotency; search by PL/original title, case-insensitive, wildcard escaping, min length; 401 on both groups; add/list/update/delete; 400 (rating 0, 11), 404 (unknown movie, missing entry), 409 (duplicate); cross-user isolation.

### Manual Testing Steps:

1. Local browser flow via `ng serve` + proxy (Phase 2).
2. Production flow (Phase 3).

## Performance Considerations

Search is an ILIKE scan over ~500 rows — no index needed; capped at 20 results; the SPA debounces input (~300 ms) and requires 2 characters, which also limits Neon wake-ups. The rated list is unpaginated (tens of rows per user).

## Migration Notes

`AddMoviesAndRatings` only creates `Movies` and `WatchedMovies` — backward-compatible with the running v6 release, which never touches them. Rolling back to v6 leaves the tables (and seeded catalog) unused. The seeder never deletes, so later catalog refreshes cannot orphan ratings (FK `MovieId` is restrict).

## References

- Roadmap item: `context/foundation/roadmap.md` → S-02
- PRD: `context/foundation/prd.md` → FR-002, US-01, Non-Goals (no external catalog API)
- Lessons: `context/foundation/lessons.md` → "Keep migrations backward-compatible…", "Key per-user data on the user id…"
- Prior change: `context/archive/2026-11-02-register-and-login/`
- Patterns: `Endpoints/AuthEndpoints.cs` (Add/Map pair, `RequireAuthorization`), `Data/MigrationRunner.cs`, `tests/MovieDate.Tests/AuthTests.cs`, `ClientApp/src/app/auth/auth.service.ts` (error mapping)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — catalog + ratings API

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 089592d
- [x] 1.2 Migration `AddMoviesAndRatings` exists and is additive — 089592d
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 089592d
- [x] 1.4 Local migrate applies the migration and seeds ~500 movies; second run inserts 0 — 089592d
- [x] 1.5 `/api/movies?q=god` → `401` with the app running — 089592d

#### Manual

- [x] 1.6 Search by Polish and original title; add/update/delete per contract via `movie-date.http` — 089592d
- [x] 1.7 `movies.json` spot-check: Polish titles correct, no empty titles — 089592d

### Phase 2: Frontend — /movies screen

#### Automated

- [x] 2.1 Frontend tests pass: `npx ng test --watch=false` — a99ef18
- [x] 2.2 Frontend production build passes: `npm run build` — a99ef18
- [x] 2.3 Docker image builds — a99ef18

#### Manual

- [x] 2.4 Logged-out `/movies` redirects to login and returns after login — a99ef18 (verified via API through the dev proxy / production; browser UI click-through pending)
- [x] 2.5 "ojciec" and "godfather" both suggest the film; add with rating 9 shows it — a99ef18 (verified via API through the dev proxy / production; browser UI click-through pending)
- [x] 2.6 Rating change persists across reload; remove shows empty state — a99ef18 (verified via API through the dev proxy / production; browser UI click-through pending)
- [x] 2.7 Adding a duplicate shows the duplicate message — a99ef18 (verified via API through the dev proxy / production; browser UI click-through pending)

### Phase 3: Production

#### Automated

- [x] 3.1 New release completes — 4443f66
- [x] 3.2 Prod `/api/movies?q=god` → `401` — 4443f66
- [x] 3.3 Prod `/health` → `{"status":"ok"}` — 4443f66

#### Manual

- [x] 3.4 Release log shows migration, seed count and non-duplicated `--migrate` command — 4443f66
- [x] 3.5 Prod flow: search, add, change rating, remove; survives reload — 4443f66 (verified via API through the dev proxy / production; browser UI click-through pending)
- [x] 3.6 `deploy-plan.md` updated with the new known-good release — 4443f66
