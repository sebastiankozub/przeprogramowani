# Want-to-Watch List — Plan Brief

> Full plan: `context/changes/want-to-watch-list/plan.md`

## What & Why

Roadmap slice S-04: a logged-in user keeps a "want to watch" list of catalog movies — add, remove, view — and can mark an entry as watched with a rating, moving it to their rated list (PRD FR-003). Shared watchlist movies become a matching signal in S-05.

## Starting Point

S-02 delivered the 498-film catalog, catalog search (`GET /api/movies?q=`), the rated list (`/api/watched`, `WatchedMoviesService`) and the `/movies` screen with a built-in autocomplete. Nothing stores watchlist entries yet.

## Desired End State

On production a user opens "Do obejrzenia", finds *Incepcja*, adds it, later clicks "Obejrzałem" with rating 8 — it leaves the watchlist and appears on "Obejrzane filmy" with 8. Each user sees only their own list.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Lists overlap | Independent — a movie may be on both; neither blocks the other | User decision; keeps S-02 untouched |
| "Obejrzałem" | One endpoint `POST /api/watchlist/{id}/watched {rating}` — adds the rating and removes the entry in one transaction | Atomic move, no half-state; reuses S-02 rating rules |
| Mark-watched conflicts | 404 not on list → 400 bad rating → 409 already rated; anything but success rolls back | Entry never lost on a failed move |
| Duplicate add | 409, as in S-02 | Consistent API contract |
| UI placement | New guarded lazy route `/watchlist` + toolbar/home links | Same pattern as `/movies` |
| Search UI | Extract a shared `MovieSearchComponent`, reused by `/movies` and `/watchlist` | One picker instead of a copy |

## Scope

**In scope:** `WatchlistItems` table (additive migration), `WatchlistService` + `/api/watchlist` (list/add/remove/mark watched), integration tests, shared search component, `/watchlist` screen + service spec, navigation, production deploy.

**Out of scope:** list exclusion rules, async recompute (S-06), scoring (S-05), catalog API changes, notes/priorities, pagination, Playwright e2e.

## Architecture / Approach

`WatchlistEndpoints` maps the authenticated `/api/watchlist` group onto `WatchlistService`, which owns the watchlist rules and composes `WatchedMoviesService.AddAsync` with the entry removal inside an execution-strategy transaction (the DB context uses retry-on-failure). The SPA's `WatchlistComponent` combines the shared `MovieSearchComponent` with the list and a per-entry rating + "Obejrzałem".

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — watchlist API | Table, service with atomic move, endpoints, tests | Transaction under `EnableRetryOnFailure` |
| 2. Frontend — /watchlist screen | Shared search, watchlist screen, navigation | Regression on `/movies` from the search refactor |
| 3. Production | Verified flow on Fly | Low — additive migration only |

**Prerequisites:** S-02 done (it is); Docker running; human runs the Fly deploy.
**Estimated effort:** ~1 session across 3 phases.

## Open Risks & Assumptions

- Independent lists mean a movie can be both rated and wanted; S-05 must decide whether that counts twice in scoring.
- The search refactor touches the working `/movies` screen — covered by existing specs plus a manual regression pass.

## Success Criteria (Summary)

- A user can add, remove and view watchlist movies and move one to the rated list with a rating, on production.
- Users never see or change each other's lists; unauthenticated calls get 401.
- `dotnet test` and `ng test` pass, including the atomic move and its rollback cases.
