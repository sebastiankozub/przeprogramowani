# Set Home City Implementation Plan

## Overview

Roadmap slice **S-03** (`set-home-city`): a logged-in user picks their home city from a seeded list of Polish localities with coordinates and sees it on a new profile screen. Coordinates are stored for the distance scoring of S-05 but never leave the server (NFR-04 guardrail). PRD refs: FR-004, US-01, NFR-04.

## Current State Analysis

- `AppUser : IdentityUser` is empty (`Data/AppUser.cs`) — the place for profile fields; the roadmap anticipated "S-03 adds profile fields".
- S-02 established the seed pipeline: `Data/Seed/movies.sparql` → `Data/Seed/normalize.mjs` (with a `CORRECTIONS` map) → embedded `Data/Seed/movies.json` → `MovieCatalogSeeder.SeedAsync` (idempotent upsert by `WikidataId`, never deletes) called from `MigrationRunner.RunAsync` after `MigrateAsync`; `appsettings.json` keeps EF SQL logging at Warning so the release log stays readable.
- Search pattern: `Endpoints/MoviesEndpoints.cs` (`q` trimmed, ≥ 2 chars, capped at 100, LIKE-escaped ILIKE, prefix first, max 20). Services hold rules with enum results; endpoints map them; DTOs in `Contracts/`; user id via `UserManager.GetUserId` (`NameIdentifier`).
- SPA: Angular Material autocomplete pattern in `ClientApp/src/app/movies/movie-search.component.*`; lazy guarded routes (`/movies`, `/watchlist`); toolbar links shown only when signed in; error mapping via `MoviesError(message, status)` / exported `mapErrors`; initial bundle warning budget 600 kB.
- Lessons: migrations additive only (a nullable column is additive); per-user data keyed on the user id, never on email.

## Desired End State

- `Cities` holds Polish localities with Polish name, voivodeship, latitude, longitude and population: every town with city rights plus every other settlement with population ≥ 10,000 (Wikidata), seeded idempotently on every `--migrate`.
- `AspNetUsers.CityId` (nullable FK) stores the user's choice.
- `GET /api/cities?q=` searches the list; `GET /api/profile` returns the user's email and city; `PUT /api/profile/city` sets or changes it. No response anywhere contains coordinates or population.
- SPA `/profile` ("Profil", guarded) shows the email and lets the user pick/change the city; home shows the city or a "Ustaw swoje miasto" link to `/profile`.
- Verified on https://moviedate-sk.fly.dev.

### Key Discoveries:

- Adding a nullable `CityId` column + FK to `AspNetUsers` is additive and safe for the running release (Identity's `UserManager` ignores unknown columns only if the old code never maps them — the old image's model simply has no `CityId`, and EF does not select columns it doesn't know).
- `MovieCatalogSeeder` is the template for `CityCatalogSeeder`; both run in the same `MigrationRunner` try/catch so a seed failure fails the release.
- The city label must disambiguate duplicate names (several Polish towns share a name), hence "Nazwa (województwo)".

## What We're NOT Doing

- No clearing of the city once set (user decision) — only changing it; no DELETE endpoint.
- No villages below 10,000 inhabitants without city rights, no counties or voivodeships as selectable options; no locations outside Poland (PRD non-goal).
- No distance computation or matching (S-05); no display of other users' cities yet.
- No other profile fields (name, photo, bio); no blocking of features when the city is missing — only a nudge on home.
- No accent-insensitive search (consistent with movies).
- No Playwright e2e.

## Implementation Approach

Backend first: entity + user column in one additive migration, Wikidata export + normalizer + seeder (mirroring movies), `CitiesEndpoints` for search, `ProfileService` + `ProfileEndpoints` for reading and setting the city, integration tests including the "no coordinates" guardrail. Then the SPA profile screen and home nudge. Finally the production deploy.

## Critical Implementation Details

- **Guardrail (NFR-04):** `CityResponse` carries only `Id`, `Name`, `Voivodeship`; coordinates and population exist only in the entity and in server-side queries. A test asserts the raw JSON of `/api/cities` and `/api/profile` contains no `latitude`, `longitude` or `population` keys.
- **Search ordering:** prefix matches first, then larger population first, then name, then id — so "Warszawa" beats "Warszawka"-like small places for "war".

## Phase 1: Backend — cities + profile API

### Overview

City storage and seed, the user's city column, search and profile endpoints, integration tests.

### Changes Required:

#### 1. Entities and DbContext

**File**: `Data/City.cs` (new), `Data/AppUser.cs`, `Data/AppDbContext.cs`

**Intent**: City lookup entity; nullable city reference on the user.

**Contract**: `City { int Id; string WikidataId (unique, max 16); string Name (max 200); string Voivodeship (max 100); double Latitude; double Longitude; int? Population }`. `AppUser.CityId int?` + navigation `City?`, FK → `Cities.Id` restrict, index on `CityId`. `DbSet<City> Cities`.

#### 2. Migration

**File**: `Data/Migrations/*_AddCities.cs` (generated)

**Intent**: `dotnet ef migrations add AddCities --output-dir Data/Migrations`; additive only (lesson).

**Contract**: `Up` contains only `CreateTable` (Cities), `AddColumn` (nullable `CityId` on `AspNetUsers`), `CreateIndex`, `AddForeignKey`.

#### 3. Seed data

**File**: `Data/Seed/cities.sparql`, `Data/Seed/normalize-cities.mjs`, `Data/Seed/cities.json` (new), `movie-date.csproj`

**Intent**: Query Wikidata for settlements located in Poland that either have town (city) rights or population ≥ 10,000, with coordinates (P625), the voivodeship they belong to (via P131 chain to a voivodeship of Poland), Polish label and latest population (P1082). Normalize to one row per QID (choose the latest-dated population; drop rows without a Polish label, coordinates or voivodeship; strip Wikipedia-style disambiguators from labels), with a `CORRECTIONS` map like the movie normalizer. Usage documented in the headers, as for movies. `cities.json` is an `EmbeddedResource`.

**Contract**: JSON array of `{ "wikidataId": "Q270", "name": "Warszawa", "voivodeship": "mazowieckie", "latitude": 52.23, "longitude": 21.01, "population": 1860000 }`; voivodeship as the lower-case adjective form ("mazowieckie"); expected size ~1,000–1,150 rows.

#### 4. Seeder

**File**: `Data/CityCatalogSeeder.cs` (new), `Data/MigrationRunner.cs`

**Intent**: Idempotent upsert by `WikidataId`, never deletes (users reference cities); called after the movie seeder; logs inserted/updated counts.

**Contract**: `static Task<(int Inserted, int Updated)> SeedAsync(AppDbContext db, CancellationToken ct = default)`.

#### 5. Contracts

**File**: `Contracts/CityResponse.cs`, `Contracts/ProfileResponse.cs`, `Contracts/SetCityRequest.cs` (new)

**Intent**: DTOs without coordinates.

**Contract**: `CityResponse(int Id, string Name, string Voivodeship)`; `ProfileResponse(string Email, CityResponse? City)`; `SetCityRequest(int CityId)`.

#### 6. Service

**File**: `Services/ProfileService.cs` (new)

**Intent**: Read the user's profile (email + city) and set/change the city (city must exist → otherwise NotFound); keyed on the user id.

**Contract**: scoped service; `GetAsync(string userId)`, `SetCityAsync(string userId, int cityId)` with enum-style results like `WatchedMoviesService`.

#### 7. Endpoints

**File**: `Endpoints/CitiesEndpoints.cs`, `Endpoints/ProfileEndpoints.cs` (new), `Program.cs`

**Intent**: `GET /api/cities?q=` (requires auth; same input handling as movie search: trim, < 2 chars → empty, cap 100, LIKE-escape; ILIKE contains on `Name`; ordering per Critical Implementation Details; max 20). `AddAppProfile` + `MapProfileEndpoints`: `GET /api/profile`, `PUT /api/profile/city`. `Program.cs` gains only Add/Map calls above the SPA fallback.

**Contract**: `GET /api/cities?q=` → 200 `CityResponse[]`. `GET /api/profile` → 200 `ProfileResponse` (`city: null` until set). `PUT /api/profile/city` `{cityId}` → 200 `ProfileResponse` / 404 unknown city. All → 401 without a session.

#### 8. Integration tests

**File**: `tests/MovieDate.Tests/CitiesTests.cs`, `tests/MovieDate.Tests/ProfileTests.cs` (new), `movie-date.http`

**Intent**: catalog seeded (> 900 rows) and reseed inserts 0; "Warszawa" found case-insensitively with voivodeship "mazowieckie"; prefix + population ordering ("war" → Warszawa first); < 2 chars → empty; wildcard escaping; 401 on all routes; profile starts with `city: null`; set → profile shows it; change → new city; unknown city → 404 and city unchanged; user B does not see A's city; raw JSON of `/api/cities` and `/api/profile` has no `latitude`/`longitude`/`population`. Example requests in `movie-date.http`.

**Contract**: test classes in `PostgresAppCollection`, using `TestUsers`.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddCities` exists and is additive (only `CreateTable`/`AddColumn`/`CreateIndex`/`AddForeignKey` in `Up`)
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Local migrate applies `AddCities` and seeds ~1,000–1,150 cities; a second run inserts 0
- `curl -s -o /dev/null -w "%{http_code}" "http://localhost:5069/api/cities?q=war"` → `401` with the app running

#### Manual Verification:

- Via `movie-date.http` (logged in): search "kra" and "war", set and change the city, profile shows it, no coordinates in responses
- `cities.json` spot-check: ~15 known places (all voivodeship capitals, a few small towns, a large village such as Kozy) present with correct voivodeship and plausible coordinates; no duplicates by QID

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — /profile screen

### Overview

Profile service and screen with a city picker, home nudge, navigation.

### Changes Required:

#### 1. Profile service

**File**: `ClientApp/src/app/profile/profile.service.ts` (new)

**Intent**: Wrap `/api/profile` (get, setCity) and `/api/cities` (search) using the existing `mapErrors` style; Polish messages (404 on set: "Nie znaleziono tego miasta.").

**Contract**: `get()`, `setCity(cityId)`, `searchCities(q)` returning Observables; models `City { id, name, voivodeship }`, `Profile { email, city: City | null }`.

#### 2. Screen

**File**: `ClientApp/src/app/profile/profile.component.ts` + `.html` (new), `ClientApp/src/app/app.routes.ts`

**Intent**: Route `profile` (lazy, guarded, title "Profil"). Shows the email (read-only) and the current city ("Twoje miasto: Kraków (małopolskie)" or "Nie ustawiono miasta"); a city autocomplete (debounce ~300 ms, ≥ 2 chars, option label "Nazwa (województwo)") with a "Zapisz" button (disabled until a city is chosen and while pending); success updates the displayed city; inline errors. No clear/remove action.

**Contract**: `/profile` declared before the `**` redirect, `canActivate: [authGuard]`, `loadComponent`.

#### 3. Home nudge and navigation

**File**: `ClientApp/src/app/home/home.component.*`, `ClientApp/src/app/app.html`

**Intent**: Home loads the profile and shows "Twoje miasto: …" or a "Ustaw swoje miasto" link to `/profile`; toolbar gets a "Profil" link (signed-in only).

**Contract**: existing `app.spec.ts` assertions keep passing.

#### 4. Tests

**File**: `ClientApp/src/app/profile/profile.service.spec.ts` (new)

**Intent**: Vitest + `HttpTestingController`: `searchCities` calls `/api/cities?q=…`; `setCity` PUTs `{cityId}` to `/api/profile/city`; 404 maps to the Polish message.

**Contract**: runs under `ng test`; existing specs keep passing.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `cd ClientApp && npx ng test --watch=false`
- Frontend production build passes: `cd ClientApp && npm run build`
- Docker image builds: `docker build -t moviedate-local .`

#### Manual Verification:

- New account: home shows "Ustaw swoje miasto"; the link opens `/profile`
- Searching "kra" offers Kraków (małopolskie); saving shows it on `/profile` and on home after reload
- Changing to another city persists across reload; there is no way to clear it
- Logged out, `/profile` redirects to login and returns after login

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production

### Overview

Deploy, verify on production, record it.

### Changes Required:

#### 1. Deploy (human)

**Intent**: `fly deploy --remote-only --app moviedate-sk`; the release command applies `AddCities` and seeds the city catalog. This deploy also ships the S-04 review fixes (`b7e11e4`).

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the new known-good release, the city seed step, and the S-04 review fixes.

**Contract**: Section "What's still open".

### Success Criteria:

#### Automated Verification:

- Release completes: `fly releases -a moviedate-sk` shows a new `complete` release
- `curl -s -o /dev/null -w "%{http_code}" "https://moviedate-sk.fly.dev/api/cities?q=war"` → `401`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`

#### Manual Verification:

- Prod flow: set and change the city on `/profile`, home shows it; responses contain no coordinates
- `deploy-plan.md` updated with the new known-good release

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Angular `ProfileService`: URLs, payloads, 404 message.

### Integration Tests:

- API on Testcontainers Postgres: city seed idempotency, search ordering/escaping/min length, profile read/set/change, 404, 401, isolation, no-coordinates guardrail.

### Manual Testing Steps:

1. Local browser flow on `/profile` and home (Phase 2).
2. Production flow (Phase 3).

## Performance Considerations

~1,100 cities: ILIKE scan is trivial; capped at 20 results; SPA debounces. Profile read is one join by primary key.

## Migration Notes

`AddCities` creates `Cities` and adds a nullable `CityId` column with FK to `AspNetUsers` — backward-compatible: the running release never reads or writes it. Rolling back leaves the column null/unused. The city seeder never deletes, so user references stay valid (FK restrict).

## References

- Roadmap item: `context/foundation/roadmap.md` → S-03
- PRD: `context/foundation/prd.md` → FR-004, US-01, NFR-04, Business Logic (distance decay), Non-Goals (Poland only)
- Lessons: `context/foundation/lessons.md`
- Prior changes: `context/archive/2026-11-02-rate-watched-movies/` (seed pipeline), `context/archive/2026-11-03-want-to-watch-list/`
- Patterns: `Data/MovieCatalogSeeder.cs`, `Data/Seed/normalize.mjs`, `Endpoints/MoviesEndpoints.cs`, `Services/WatchedMoviesService.cs`, `ClientApp/src/app/movies/movie-search.component.*`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — cities + profile API

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 2b0f208
- [x] 1.2 Migration `AddCities` exists and is additive — 2b0f208
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 2b0f208
- [x] 1.4 Local migrate applies `AddCities` and seeds the cities; second run inserts 0 — 2b0f208
- [x] 1.5 `/api/cities?q=war` → `401` with the app running — 2b0f208

#### Manual

- [x] 1.6 Search, set and change the city via `movie-date.http`; no coordinates in responses — 2b0f208
- [x] 1.7 `cities.json` spot-check of ~15 known places — 2b0f208

### Phase 2: Frontend — /profile screen

#### Automated

- [ ] 2.1 Frontend tests pass: `npx ng test --watch=false`
- [ ] 2.2 Frontend production build passes
- [ ] 2.3 Docker image builds

#### Manual

- [ ] 2.4 New account: home nudge links to `/profile`
- [ ] 2.5 "kra" offers Kraków (małopolskie); saved city shows on `/profile` and home
- [ ] 2.6 Changing the city persists; no way to clear it
- [ ] 2.7 Logged-out `/profile` redirects to login and returns

### Phase 3: Production

#### Automated

- [ ] 3.1 New release completes
- [ ] 3.2 Prod `/api/cities?q=war` → `401`
- [ ] 3.3 Prod `/health` → `{"status":"ok"}`

#### Manual

- [ ] 3.4 Prod flow: set and change the city, home shows it, no coordinates in responses
- [ ] 3.5 `deploy-plan.md` updated with the new known-good release
