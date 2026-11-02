# Rate Watched Movies — Plan Brief

> Full plan: `context/changes/rate-watched-movies/plan.md`

## What & Why

Roadmap slice S-02: a logged-in user picks movies from a local catalog, adds them as watched with a 1–10 rating, edits or removes them, and sees their rated list (PRD FR-002). Rated movies are the main input to matching (S-05), so this is the next step on the shortest path to the north star (first message to a match).

## Starting Point

S-01 delivered Identity cookie auth, `AppDbContext` on Neon Postgres with migrations applied by Fly's `--migrate` release step, a Testcontainers test fixture, and an Angular Material SPA with `authGuard` + a 401 interceptor. There is no movie data yet.

## Desired End State

On https://moviedate-sk.fly.dev a user opens "Obejrzane filmy", types "ojciec" or "godfather", picks *Ojciec chrzestny*, rates it 9, later changes the rating or removes it — and only ever sees their own list. The catalog of ~500 films is seeded automatically on every deploy.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Rating scale | Integers 1–10 | Familiar to Polish film fans (Filmweb/IMDb) and gives S-05 room for a "highly-rated" threshold |
| Catalog source | One-off Wikidata export, ~500 films by sitelink count, JSON in repo | CC0 licence, Polish titles, enough overlap between users for matching |
| Seeding | Idempotent upsert by Wikidata QID inside `--migrate`, never deletes | Same path for local, tests and Fly; catalog updates need no migration |
| Search | Server-side `GET /api/movies?q=`, ILIKE on Polish + original title, ≥ 2 chars, max 20 | Survives a later swap to an external catalog; finds both language variants |
| Duplicate add | 409 + message; rating changes only via PUT | Clear add vs edit contract; each write is a distinct event for S-06 |
| UI placement | New guarded route `/movies` + toolbar/home links | Home stays a hub for the S-03/S-04/S-05 screens |
| User identity | `ClaimTypes.NameIdentifier` → `AspNetUsers.Id` | Recorded lesson from S-01 review |

## Scope

**In scope:** `Movies` + `WatchedMovies` tables (additive migration), Wikidata query + seed JSON + seeder, `/api/movies` search, `/api/watched` CRUD with a `WatchedMoviesService`, integration tests, Angular `/movies` screen + service test, navigation, production deploy.

**Out of scope:** async match recompute (S-06), scoring (S-05), want-to-watch list (S-04), external catalog API, posters/genres, accent-insensitive search, pagination, Playwright e2e.

## Architecture / Approach

`MigrationRunner` migrates, then `MovieCatalogSeeder` upserts the embedded `movies.json`. `MoviesEndpoints` serves search; `WatchedMoviesEndpoints` maps the authenticated `/api/watched` group onto `WatchedMoviesService`, which enforces rating range, existence, uniqueness and per-user scoping (also the future hook for S-06). The SPA's `MoviesService` calls both; `MoviesComponent` combines autocomplete, rating picker and the editable list.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — catalog + ratings API | Tables, seed, search, CRUD, integration tests | Wikidata export quality (missing Polish labels, odd titles) |
| 2. Frontend — /movies screen | Autocomplete, rating, editable list, navigation | Autocomplete UX (debounce, display of selected option) |
| 3. Production | Seeded catalog and flow verified on Fly | Seed step lengthening/failing the release command |

**Prerequisites:** S-01 done (it is); Docker running; network access to query.wikidata.org once; human runs the Fly deploy.
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- Wikidata labels may be missing in Polish for some films — fallback to the English label; spot-checked manually.
- No accent-insensitive search: "zycie" does not find "Życie" — accepted for MVP.
- Search-as-you-type wakes Neon from autosuspend; first query may take 1–4 s.
- "Highly-rated" threshold is not defined here — S-05 decides it on the 1–10 scale.

## Success Criteria (Summary)

- A user can search the catalog in Polish or English titles, add a movie with a 1–10 rating, change and remove it, on production.
- Users never see or change each other's ratings; unauthenticated calls get 401.
- `dotnet test` and `ng test` pass, including duplicate (409), range (400) and isolation cases.
