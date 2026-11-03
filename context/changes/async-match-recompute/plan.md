# Async Match Recompute Implementation Plan

## Overview

Roadmap slice S-06: every write that can change a user's matches — watched movies, watchlist, home city, display name — responds immediately and schedules a background recompute of that user's match list. `/matches` loses its "Szukaj dopasowań" button; it shows "Trwa aktualizacja dopasowań…" while a recompute is pending and reloads the list on its own when it finishes, within roughly 10 s of the write (PRD FR-002, FR-003, FR-005, NFR).

## Current State Analysis

- The recompute rule is complete and tested: `MatchService.RecomputeAsync` (`Services/MatchService.cs:118`) re-scores, adds strong candidates, tops up to 5, mirrors rows and never deletes, inside the execution strategy + one transaction. Its class comment already says "S-06 calls RecomputeAsync from its background job".
- Recompute is only reachable through `POST /api/matches/recompute` (`Endpoints/MatchesEndpoints.cs:38`), driven by the "Szukaj dopasowań" button (`ClientApp/src/app/matches/matches.component.html:5`).
- Write endpoints that must trigger it:
  - `POST /api/watched`, `PUT /api/watched/{id}`, `DELETE /api/watched/{id}` (`Endpoints/WatchedMoviesEndpoints.cs:35,52,68`)
  - `POST /api/watchlist`, `DELETE /api/watchlist/{id}`, `POST /api/watchlist/{id}/watched` (`Endpoints/WatchlistEndpoints.cs:39,62,77`)
  - `PUT /api/profile/city`, `PUT /api/profile/display-name` (`Endpoints/ProfileEndpoints.cs:35,45`)
- `tech-stack.md:30` fixes the mechanism: an in-process `BackgroundService`, no separate Fly process group.
- Production runs 2 web machines (`fly.toml`, deploy-plan.md), so in-memory state is per machine; a write and the following `/matches` view can hit different machines.
- No column records whether a user's list is up to date; existing production users (including the 20 `mdtest-*` accounts) have complete profiles but may never have been recomputed.
- Tests: `tests/MovieDate.Tests/PostgresAppFixture.cs` is a `WebApplicationFactory` that injects settings via `UseSetting` (`:20-22`), so hosted services run in tests and timings can be shortened through configuration.

## Desired End State

- Any of the 8 trigger writes returns its usual status without waiting for a recompute and stamps `AspNetUsers.MatchesRequestedAt`.
- A per-machine debounced queue runs one `RecomputeAsync` per user after ~2 s of quiet (at most ~5 s after the first write) and stamps `MatchesComputedAt` with the recompute's start time.
- `GET /api/matches/status` → `200 { pending, profileComplete }`; when it reports pending and this machine has nothing queued for the user, it enqueues a recompute (self-heal after restart/deploy, and first list for pre-S-06 accounts).
- `/matches` has no search button; it loads the stored list, shows "Trwa aktualizacja dopasowań…" while pending, polls status, and reloads the list when pending clears. An incomplete profile shows the existing message with a link to `/profile`.
- On production, rating a movie on one test account updates the other side's list within ~10 s without any click.

### Key Discoveries:

- `Services/MatchService.cs:162` — `RecomputeInTransactionAsync` is where the `MatchesComputedAt` stamp belongs, in the same transaction as the upserts.
- `Services/ProfileService.cs:66,92` — the `ExecuteUpdateAsync` pattern for single-column user updates (also rotates `ConcurrencyStamp`; the new stamps do not need to).
- `Services/Clock.cs` — the existing time source; use it for both stamps.
- `tests/MovieDate.Tests/PostgresAppFixture.cs:20` — settings must go through `UseSetting`, not `ConfigureAppConfiguration`.

## What We're NOT Doing

- No durable/DB job queue, Hangfire or separate Fly worker — in-memory queue + DB "pending" stamps only.
- No push to the *other* side: when A's recompute adds or re-scores the mirror row on B's list, B's `pending` is not set; B sees it on the next load or poll of `/matches`.
- No status endpoint for anything other than the caller's own list; no progress percentage.
- `POST /api/matches/recompute` is NOT removed (kept for scripts/tests, e.g. `scripts/seed-test-users.mjs --recompute`); only the UI button goes.
- No changes to scoring, eligibility, or append-only rules from S-05.
- No scheduled/daily recompute (PRD non-goal).

## Implementation Approach

Write path: endpoint succeeds → `MatchRecomputeTrigger.RequestAsync(userId)` stamps `MatchesRequestedAt = now` (one `ExecuteUpdate`) and calls `MatchRecomputeQueue.Enqueue(userId)` → response. The queue is a singleton keyed by user id with first/last request times; a `BackgroundService` takes due users (quiet ≥ QuietPeriod or waited ≥ MaxDelay), creates a DI scope per job, and calls `MatchService.RecomputeAsync`. `RecomputeAsync` captures its start time and, inside its transaction, sets `MatchesComputedAt = start` only if that moves it forward. A write arriving mid-recompute has `RequestedAt > start`, so the user stays pending and the queue (which already holds the new request) runs again.

Pending rule (single definition, in `MatchService`): `profileComplete && (MatchesComputedAt is null || (MatchesRequestedAt is not null && MatchesRequestedAt > MatchesComputedAt))`, where `profileComplete = CityId != null && DisplayName != null`. The `ComputedAt is null` branch gives pre-S-06 accounts their first list through the status self-heal.

## Critical Implementation Details

- **Timing & lifecycle** — the `MatchesComputedAt` stamp must use the time captured *before* the recompute reads data, and must be a forward-only `UPDATE … WHERE "MatchesComputedAt" IS NULL OR "MatchesComputedAt" < @start`; stamping "now" at the end would hide a write that landed during the recompute. A recompute that returns `ProfileIncomplete`/`NotFound` stamps nothing.
- **Worker robustness** — an exception in one job must be logged and swallowed (the user stays pending and self-heals on the next status check); it must never stop the `BackgroundService`. Stopping the host cancels in-flight work cleanly.

## Phase 1: Backend — timestamps + background recompute

### Overview

Additive migration, debounced in-memory queue + hosted worker, triggers on the 8 writes, status endpoint with self-heal, and integration tests.

### Changes Required:

#### 1. Pending stamps on the user

**File**: `Data/AppUser.cs`, new migration `Data/Migrations/<ts>_AddMatchRecomputeStamps.cs` (+ Designer, snapshot)

**Intent**: Record when a recompute was requested and when the last one started, so "pending" is the same answer on both machines and survives restarts.

**Contract**: `DateTimeOffset? MatchesRequestedAt`, `DateTimeOffset? MatchesComputedAt` on `AppUser`; migration only adds two nullable `timestamp with time zone` columns to `AspNetUsers` (additive — lessons.md).

#### 2. Queue + options

**File**: `Services/MatchRecomputeQueue.cs` (new), `appsettings.json`

**Intent**: Coalesce bursts of writes per user and hand out users whose debounce window has elapsed.

**Contract**: singleton `MatchRecomputeQueue` with `Enqueue(string userId)` (sets first-seen once, updates last-seen), `bool Contains(string userId)`, and a way for the worker to take all due user ids (removing them). Options class `MatchRecomputeOptions` bound from section `MatchRecompute`: `QuietPeriod` (default 2 s), `MaxDelay` (default 5 s), `PollInterval` (default 250 ms). Thread-safe (`ConcurrentDictionary` or a lock).

#### 3. Background worker

**File**: `Services/MatchRecomputeWorker.cs` (new)

**Intent**: Run due recomputes off the request path, one at a time per machine.

**Contract**: `BackgroundService`; loop: wait `PollInterval`, take due users, for each create a scope, resolve `MatchService`, `await RecomputeAsync(userId, stoppingToken)`; catch-and-log per job (see Critical Implementation Details).

#### 4. Trigger service

**File**: `Services/MatchRecomputeTrigger.cs` (new)

**Intent**: One call the write endpoints make after a successful write.

**Contract**: scoped `MatchRecomputeTrigger.RequestAsync(string userId, CancellationToken ct)` — `ExecuteUpdate` `MatchesRequestedAt = Clock.Now()` for that user, then `queue.Enqueue(userId)`.

#### 5. Stamp + status in MatchService

**File**: `Services/MatchService.cs`

**Intent**: Mark a finished recompute and answer "is my list up to date?".

**Contract**:
- `RecomputeInTransactionAsync` captures `start` before step 1 and, on `Success`, runs the forward-only `MatchesComputedAt` update inside the transaction (also applies to `POST /recompute`).
- New `Task<MatchStatus?> GetStatusAsync(string userId, CancellationToken ct)` → `null` when the user is missing; otherwise `MatchStatus(bool Pending, bool ProfileComplete)` using the pending rule above.
- Update the class comment (S-06 now calls it from the worker).

#### 6. Endpoints

**File**: `Endpoints/WatchedMoviesEndpoints.cs`, `Endpoints/WatchlistEndpoints.cs`, `Endpoints/ProfileEndpoints.cs`, `Endpoints/MatchesEndpoints.cs`, `Contracts/MatchStatusResponse.cs` (new)

**Intent**: Fire the trigger on every successful write and expose status.

**Contract**:
- After a **success** outcome only, the 8 handlers call `MatchRecomputeTrigger.RequestAsync` before returning; error outcomes (400/404/409) trigger nothing. Response codes and bodies are unchanged.
- `GET /api/matches/status` (authorized) → `200 MatchStatusResponse(bool Pending, bool ProfileComplete)`, `404` if the user row is gone. If `Pending && !queue.Contains(userId)` it calls `queue.Enqueue(userId)` (no new RequestedAt stamp needed).
- `AddAppMatches` registers the queue (singleton), options, trigger (scoped) and `AddHostedService<MatchRecomputeWorker>()`. The `--migrate` path in `Program.cs` returns before `app.Run`, so the worker never starts on the release machine.

#### 7. Tests

**File**: `tests/MovieDate.Tests/MatchRecomputeTests.cs` (new), `PostgresAppFixture.cs`, `movie-date.http`

**Intent**: Prove asynchrony, debounce, stamps, status and self-heal end to end.

**Contract**: fixture sets `MatchRecompute:QuietPeriod`/`MaxDelay`/`PollInterval` to small values via `UseSetting`. Tests (poll with a timeout, never fixed sleeps longer than needed):
- Rating a shared movie (two users with city + name) → both lists contain each other within the timeout, without calling `POST /recompute`.
- Each trigger kind (watched add/update/delete, watchlist add/delete/mark-watched, city, display name) makes `status.pending` true right after the write and false after the worker runs.
- Error outcomes (e.g. rating 11 → 400, duplicate → 409) do not stamp `MatchesRequestedAt`.
- Burst: several writes within the quiet period produce one recompute (assert via `MatchesComputedAt` changing once, or a test hook counting runs).
- Profile incomplete → `{ pending: false, profileComplete: false }`; worker skips without error.
- Pre-S-06 user (complete profile, both stamps null) → status pending → list appears without any write (self-heal).
- Unit tests for the queue's due-selection (quiet period, max delay, re-enqueue during wait).
- Anonymous `GET /api/matches/status` → 401.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddMatchRecomputeStamps` exists and only adds nullable columns
- Tests pass: `dotnet test tests/MovieDate.Tests`
- `/api/matches/status` → `401` with the app running

#### Manual Verification:

- Two local accounts: rating a shared movie on one makes it appear on the other's `GET /api/matches` within ~10 s, with no `POST /recompute`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — automatic /matches

### Overview

Remove the search button; drive the page from the status endpoint with a pending indicator and auto-reload.

### Changes Required:

#### 1. Service

**File**: `ClientApp/src/app/matches/matches.service.ts` (+ spec)

**Intent**: Read the status.

**Contract**: `MatchStatus { pending: boolean; profileComplete: boolean }`; `status(): Observable<MatchStatus>` on `GET /api/matches/status` with the existing `mapErrors` style. `recompute()` is removed from the service (endpoint stays server-side).

#### 2. Component + template

**File**: `ClientApp/src/app/matches/matches.component.ts`, `.html` (+ spec)

**Intent**: Show the stored list, tell the user when an update is on the way, and refresh on completion.

**Contract**:
- On init: load status and list. `!profileComplete` → existing `PROFILE_INCOMPLETE` message + `Przejdź do profilu` link (no list call needed beyond what's harmless).
- `pending` → indeterminate progress bar + "Trwa aktualizacja dopasowań…"; poll status every ~2 s; when pending turns false, reload the list and hide the indicator. Stop polling after ~30 s with "Aktualizacja trwa dłużej niż zwykle — odśwież stronę za chwilę." Polling stops on component destroy.
- No button. Empty state text: "Nie masz jeszcze dopasowań. Oceń filmy lub dodaj je do listy do obejrzenia — lista zaktualizuje się automatycznie."
- Specs cover: pending → poll → reload; profile incomplete; empty state; polling stops on destroy (fake timers).

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `npx ng test --watch=false`
- Frontend production build passes
- Docker image builds

#### Manual Verification:

- Rate a movie, open `/matches` immediately: "Trwa aktualizacja dopasowań…" appears, then the list updates on its own
- Account without city/name: `/matches` shows the profile message and link, no spinner forever
- No "Szukaj dopasowań" button anywhere

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production — deploy + verify

### Overview

Ship to Fly, verify the < 10 s refresh on production with the seeded test accounts, record the release.

### Changes Required:

#### 1. Deploy + record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Human runs `fly deploy`; record the new known-good release, the additive migration, and the rollback note (S-05 image is schema-compatible; rolling back only stops auto-recompute).

**Contract**: new bullet in the deploy-plan execution log in the existing style.

### Success Criteria:

#### Automated Verification:

- Release completes on Fly (release command applies `AddMatchRecomputeStamps`)
- Production `/api/matches/status` → `401`
- Production `/health` → ok

#### Manual Verification:

- Logged in as `sebastiankozub@o2.pl`, `/matches` shows the pending indicator once, then a list (self-heal for the pre-S-06 account)
- On an `mdtest-*` account rate Casablanca ≥ 7: the reference account's `/matches` shows the change within ~10 s without any click
- `deploy-plan.md` updated with the new known-good release

---

## Testing Strategy

### Unit Tests:

- Queue due-selection: quiet period elapsed, max delay reached during a continuous burst, re-enqueue while waiting, `Contains` after take.

### Integration Tests:

- Trigger → background recompute → mirrored rows on both sides (no `POST /recompute`).
- Every trigger kind stamps and clears pending; error outcomes don't stamp.
- Debounce collapses a burst into one recompute.
- Self-heal for a never-computed user; profile-incomplete status.

### Manual Testing Steps:

1. Two local accounts with city + name; rate a shared movie on A; watch B's `/matches` update within ~10 s.
2. Change A's city; A's `/matches` shows the indicator and new scores.
3. New account without city: `/matches` shows the profile message; set city and name → list appears automatically.

## Performance Considerations

Each trigger adds one `UPDATE AspNetUsers` to the write; the debounced recompute runs off the request path, one at a time per machine. `/matches` polls a cheap indexed read every ~2 s for at most ~30 s, so Neon is not kept awake indefinitely.

## Migration Notes

Additive: two nullable columns. Existing users get `NULL` stamps, which the pending rule treats as "needs a first recompute" (when the profile is complete) — handled by the status self-heal, no backfill.

## References

- PRD: `context/foundation/prd.md` FR-002, FR-003, FR-005, NFR (~10 s)
- Roadmap: `context/foundation/roadmap.md` S-06
- Stack decision: `context/foundation/tech-stack.md:30`
- Prior slice: `context/changes/ranked-match-list/plan.md`, `Services/MatchService.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — timestamps + background recompute

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 3edc850
- [x] 1.2 Migration `AddMatchRecomputeStamps` exists and only adds nullable columns — 3edc850
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 3edc850
- [x] 1.4 `/api/matches/status` → `401` with the app running — 3edc850

#### Manual

- [x] 1.5 Two local accounts: rating a shared movie on one makes it appear on the other's `GET /api/matches` within ~10 s, with no `POST /recompute` — 3edc850

### Phase 2: Frontend — automatic /matches

#### Automated

- [x] 2.1 Frontend tests pass: `npx ng test --watch=false`
- [x] 2.2 Frontend production build passes
- [x] 2.3 Docker image builds

#### Manual

- [ ] 2.4 Rate a movie, open `/matches` immediately: "Trwa aktualizacja dopasowań…" appears, then the list updates on its own
- [ ] 2.5 Account without city/name: `/matches` shows the profile message and link, no spinner forever
- [ ] 2.6 No "Szukaj dopasowań" button anywhere

### Phase 3: Production — deploy + verify

#### Automated

- [ ] 3.1 Release completes on Fly (release command applies `AddMatchRecomputeStamps`)
- [ ] 3.2 Production `/api/matches/status` → `401`
- [ ] 3.3 Production `/health` → ok

#### Manual

- [ ] 3.4 Logged in as `sebastiankozub@o2.pl`, `/matches` shows the pending indicator once, then a list (self-heal for the pre-S-06 account)
- [ ] 3.5 On an `mdtest-*` account rate Casablanca ≥ 7: the reference account's `/matches` shows the change within ~10 s without any click
- [ ] 3.6 `deploy-plan.md` updated with the new known-good release
