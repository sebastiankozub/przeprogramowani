# Async Match Recompute — Plan Brief

> Full plan: `context/changes/async-match-recompute/plan.md`

## What & Why

Roadmap slice S-06. The PRD (FR-002, FR-003, FR-005) requires every watched/watchlist write to trigger an asynchronous match recompute: the write responds immediately and the updated list is visible within ~10 s. Today matches only change when the user presses "Szukaj dopasowań" — a certification requirement is unmet.

## Starting Point

S-05 shipped the full recompute rule (`MatchService.RecomputeAsync`: re-score, add strong, top up to 5, mirror, never delete) behind `POST /api/matches/recompute` and a button on `/matches`. Nothing calls it on writes; production runs 2 Fly machines, so in-memory state alone can't answer "is my list up to date?".

## Desired End State

Rating, adding or removing a movie, or changing city or display name returns immediately and schedules a recompute. `/matches` has no button: it shows "Trwa aktualizacja dopasowań…" while an update is pending and reloads the list by itself when it lands. Existing accounts get their first list automatically on their next visit.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Triggers | 6 movie writes + city + display name | City changes distance; setting the last missing profile field should make the user matchable without a button. |
| Bursts | Debounce: ~2 s quiet, max ~5 s | Several quick ratings cost one recompute and still fit the 10 s budget. |
| Mechanism | In-memory per-machine queue + `BackgroundService` | Settled in `tech-stack.md:30`; no worker process or job library. |
| Pending state | DB stamps `MatchesRequestedAt` / `MatchesComputedAt` + `GET /api/matches/status` | Correct across both machines and restarts; additive migration. |
| Frontend refresh | Load on entry; while pending show indicator, poll status ~2 s (≤ 30 s), reload on completion | Tells the user an update is on the way without permanent polling. |
| Recovery | Status check re-enqueues a pending user missing from the local queue | Heals lost work after deploy/restart and gives pre-S-06 accounts their first list. |
| Button | Removed from UI; `POST /recompute` kept for scripts/tests | Matching is fully automatic, as the PRD describes. |

## Scope

**In scope:** migration (2 nullable columns), queue + worker + trigger service, triggers on 8 endpoints, `GET /api/matches/status` with self-heal, `ComputedAt` stamp in `RecomputeAsync`, integration/unit tests, `/matches` UI rework, prod deploy + verification.

**Out of scope:** durable job queue/Hangfire/worker process, notifying the *other* side's open page of mirrored changes, removing `POST /recompute`, scoring/eligibility changes, scheduled recompute.

## Architecture / Approach

Write succeeds → `MatchRecomputeTrigger` stamps `MatchesRequestedAt` and enqueues the user id → `MatchRecomputeWorker` picks users after the debounce window → scoped `MatchService.RecomputeAsync` → in its transaction, forward-only `MatchesComputedAt = start`. Pending = profile complete and (`ComputedAt` null or `RequestedAt > ComputedAt`). `/matches` reads status, polls while pending, reloads the stored list.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — stamps + background recompute | Migration, queue/worker, 8 triggers, status endpoint, tests | Race between a mid-recompute write and the ComputedAt stamp; flaky timing in tests |
| 2. Frontend — automatic /matches | Pending indicator, status polling, auto reload, no button | Polling that never stops |
| 3. Production — deploy + verify | Live < 10 s refresh, deploy-plan entry | Fly trial cold start delays the first recompute |

**Prerequisites:** S-05 committed and deployed (done); Docker for tests; human runs `fly deploy`.
**Estimated effort:** ~1 session across 3 phases (deadline 2026-11-04).

## Open Risks & Assumptions

- Queued work is lost on restart; healed only when the user (or their page) checks status or writes again.
- The other side of a mirrored match isn't told; their open `/matches` page updates only on reload or their own pending cycle.
- On the Fly trial, machines stop after 5 min; a cold start can push the first update past 10 s.

## Success Criteria (Summary)

- A movie, city or name write never waits for a recompute, and the match list reflects it within ~10 s on production.
- `/matches` shows when an update is on the way and refreshes itself; no button is needed.
- Existing accounts get a list automatically; nothing is ever removed from a list.
