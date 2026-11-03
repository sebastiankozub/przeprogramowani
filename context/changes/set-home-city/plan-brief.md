# Set Home City — Plan Brief

> Full plan: `context/changes/set-home-city/plan.md`

## What & Why

Roadmap slice S-03: a logged-in user picks their home city from a predefined list of Polish localities and sees it on a profile screen (PRD FR-004). The city's coordinates feed the distance-decayed proximity score in S-05, but must never be exposed to anyone (NFR-04). This is the last input S-05 needs.

## Starting Point

Users exist (`AppUser`, empty), movies and watchlist are done (S-02, S-04), and S-02 left a proven Wikidata → normalizer → embedded JSON → idempotent seeder pipeline plus a server-side autocomplete pattern. There is no location data and no profile screen.

## Desired End State

On production a new user sees "Ustaw swoje miasto" on home, opens "Profil", types "kra", picks "Kraków (małopolskie)" and saves; home then shows the city, and it can be changed (not cleared). No API response contains coordinates.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Data source | Wikidata (CC0) export, normalizer + seeder like movies | Same proven pipeline, no attribution, Polish names |
| Locality scope | All towns with city rights **plus** any other settlement ≥ 10,000 inhabitants | Full coverage incl. large villages (user decision) |
| Selectable levels | Localities only (no counties/voivodeships) | Comparable distances for S-05 |
| UI | New guarded lazy `/profile` screen + toolbar link; home shows city or a nudge | Natural home for future profile fields |
| Picker | Server-side autocomplete `GET /api/cities?q=`, label "Nazwa (województwo)" | Mirrors movie search; coordinates never leave the server |
| Change vs clear | City can be changed, not cleared; nudge (not a block) while unset | S-05 needs a city; gentle guidance |
| Storage | Nullable `AspNetUsers.CityId` FK (restrict) + `Cities` table | Additive migration; seeder never deletes referenced rows |

## Scope

**In scope:** `Cities` table + `CityId` column (additive migration), Wikidata query + normalizer + `cities.json` + seeder, `/api/cities`, `/api/profile` (+ `PUT /city`), integration tests incl. no-coordinates guardrail, `/profile` screen, home nudge, toolbar link, service spec, production deploy.

**Out of scope:** clearing the city, villages < 10k without city rights, counties/voivodeships, non-Polish places, distance/matching (S-05), other profile fields, accent-insensitive search, Playwright e2e.

## Architecture / Approach

`MigrationRunner` migrates, seeds movies, then seeds cities. `CitiesEndpoints` searches `Cities` (prefix first, then population); `ProfileEndpoints` maps `GET /api/profile` and `PUT /api/profile/city` onto `ProfileService`, keyed on the user id. DTOs (`CityResponse`, `ProfileResponse`) carry only id, name and voivodeship. The SPA's `/profile` screen uses a city autocomplete; home reads the profile for the nudge.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — cities + profile API | Data, seed, search, profile endpoints, tests | Wikidata query completeness (missing coords/voivodeship, duplicates) |
| 2. Frontend — /profile screen | Profile screen, city picker, home nudge | Low — mirrors the movie picker |
| 3. Production | Seeded cities and flow verified on Fly | Longer release step (seed ~1,100 rows) |

**Prerequisites:** S-01 done (it is); Docker running; network access to query.wikidata.org once; human runs the Fly deploy.
**Estimated effort:** ~1 session across 3 phases.

## Open Risks & Assumptions

- Wikidata population figures are partly outdated, so the 10k threshold is approximate; spot-checked manually.
- Duplicate names are disambiguated by voivodeship only; two same-named places in one voivodeship would look identical (expected to be rare; checked during the spot-check).
- A user without a city is allowed; S-05 must handle them (fallback or prompt).

## Success Criteria (Summary)

- A user can set and change their city on production and sees it on home and profile.
- No API response ever contains coordinates or population; unauthenticated calls get 401.
- `dotnet test` and `ng test` pass, including the no-coordinates guardrail and seed idempotency.
