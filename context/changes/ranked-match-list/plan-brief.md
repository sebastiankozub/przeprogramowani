# Ranked Match List — Plan Brief

> Full plan: `context/changes/ranked-match-list/plan.md`

## What & Why

Roadmap slice S-05: a user with a city and a display name presses "Szukaj dopasowań" and gets a ranked list of other users scored on shared favorite movies, shared watchlist movies and city proximity. This is the core business rule of MovieDate (FR-005) and the gate to friends (S-07) and the north-star first message (S-08).

## Starting Point

Ratings (1–10), watchlists and home cities with server-only coordinates exist from S-02–S-04. There is no match table, service, API or screen, users have no public identity besides their email, and production has only throwaway e2e accounts.

## Desired End State

`/profile` has a display name field; `/matches` shows a stored, append-only, two-way list with name, city, score 0–100 and shared titles — never an email or coordinates. Users without a city or name are pointed to `/profile`. In production, demo accounts created through the public API let a fresh user see more than one ranked candidate.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Highly-rated movie | Both users rated it ≥ 7 | More overlap on a small user base than ≥ 8. |
| Score | `50·taste + 50·proximity`, 0–100 | Equal weight to taste and being able to meet. |
| Taste curve | `r/(r+4)`, `r = 2·sharedFavorites + sharedWatchlist` | Favorites count double; counts saturate, two shared favorites = 0.5. |
| Proximity curve | `1/(1+(d/100)^0.683)` (haversine km) | Hits PRD anchors exactly: 100 km = 0.5, 500 km = 0.25, never 0. |
| Who is added | Up to 20 new strong (≥ 1 shared movie); top up with closest users to 5 | Guarantees > 1 candidate whenever others exist (secondary success criterion). |
| Eligibility | City and display name required on both sides | Proximity always computable; every entry has a human name. |
| Trigger | Stored list on `GET`; recompute on `POST` ("Szukaj dopasowań") | Viewing stays < 2 s; S-06 reuses the same recompute. |
| Symmetry | Mirror both ways with the same score | Score is symmetric; incoming S-08 messages come from someone on your list. |
| Identity | New display name on `/profile`; never show emails | No email leak to strangers; S-07/S-08 need a readable identity. |
| Prod demo | Node script registering demo accounts via the public API | No demo code in the app; exercises real endpoints. |
| Append-only | `ON CONFLICT` upserts; rows only vanish on account deletion | Concurrent recomputes can't duplicate or drop entries. |

## Scope

**In scope:** display name (profile API + UI), `AddMatches` migration, scoring + recompute + list services, `/api/matches` endpoints, `/matches` screen, home links/nudges, demo-account script, prod deploy.

**Out of scope:** recompute on movie writes / background job (S-06), friends and messaging (S-07/S-08), relative "above own average" favorites, paging, exposing distance, clearing the display name, an in-app demo seeder.

## Architecture / Approach

`MatchScoringService` holds pure formulas (unit-tested). `MatchService.RecomputeAsync` loads the requester's favorites/watchlist, computes shared counts per eligible user with grouped SQL, picks rows to add (strong first, closest top-up), and upserts `A→X` and `X→A` in one transaction inside the execution strategy. `MatchService.ListAsync` is a pure read joining candidate name/city plus up to 5 live shared titles per kind. Endpoints follow the per-feature pattern (`MatchesEndpoints`, `Contracts/MatchResponse`).

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — display name + Matches schema | Additive migration, `PUT /api/profile/display-name` | Migration not additive (lesson) |
| 2. Backend — scoring + match API | Formulas, mirrored append-only recompute, `GET`/`POST /api/matches` | Concurrency/upsert correctness; test isolation on a shared DB |
| 3. Frontend — display name + /matches | Profile field, matches screen, home nudges | 409 / empty-state UX |
| 4. Production — deploy + demo accounts | Demo script, deploy, prod verification | Fly trial cold start vs. NFR-01 |

**Prerequisites:** S-02, S-03, S-04 done (they are); Docker for tests; Fly access for Phase 4.
**Estimated effort:** ~2–3 sessions across 4 phases.

## Open Risks & Assumptions

- Stored counts/score can lag behind live shared titles until the next recompute (S-06 closes the gap).
- Top-up neighbours stay on the list forever (append-only), even after strong matches arrive.
- With mirroring, a user's list can grow from others' recomputes without their own action.
- Fly trial machines cold-start; the < 2 s check is done on a warm machine.

## Success Criteria (Summary)

- A real production user with name, city and a few ratings sees more than one ranked candidate, and never an email or coordinates.
- Re-running the search never removes anyone; scores update; both sides see each other.
- The scoring formulas match the PRD anchor points in unit tests.
