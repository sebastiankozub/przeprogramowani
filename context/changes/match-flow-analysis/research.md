---
date: 2026-11-04T09:33:08+01:00
researcher: Claude Code (Opus 5.5) for the sole maintainer
git_commit: 7734e90456a39382b6ccc2fc7cd33a3e11e93218
branch: architect
repository: sebastiankozub/przeprogramowani-MovieDate
topic: "Match flow: write → recompute trigger → background recompute → ranked match list → friends/messaging access gate"
tags: [research, match-flow, matching, match-recompute, movie-ratings, watchlist, profile-city, friends-messaging, repo-map]
status: complete
last_updated: 2026-11-04
last_updated_by: Claude Code (Opus 5.5)
---

# Research: the match flow

**Date**: 2026-11-04T09:33:08+01:00
**Researcher**: Claude Code (Opus 5.5) for the sole maintainer
**Git Commit**: 7734e90 (all `file:line` anchors refer to this commit)
**Branch**: architect
**Repository**: sebastiankozub/przeprogramowani-MovieDate

Labels: **[E]** evidence (read in source at the anchor), **[I]** inference, **[U]** unknown.

## Research Question

How does a user's write turn into an updated, ranked match list? The writes in scope are:
- adding, editing or removing a watched-movie rating;
- adding, removing or marking-watched a watchlist item;
- changing the home city or display name.

From the write, the path runs through the recompute trigger and the background recompute to the list read. At the end of it, the friends and messaging access check depends on the match list. The analysis pays particular attention to the areas that `context/map/repo-map.md` flags:
- **Risk zones:** movie-ratings and matching.
- **Watch items:** match-recompute, friends-messaging and identity.
- **Unknowns 1–3:** whether recompute survives a restart or multiple machines; whether triggers that live in endpoints cover every write; whether the match-list semantics leak into messaging access.

The analysis covers only the current state of the repository. Three parallel workers produced the input: an e2e trace, a test-gap map and a blast-radius analysis. The decisive anchors were re-checked by the main agent.

## Summary

The match flow is designed as **"commit the write → stamp the user in the DB → enqueue in memory → debounced background recompute → append-only mirrored upsert"**. A status endpoint re-enqueues users this machine has lost, which the code calls "self-heal".

- **Durability:** the source of truth for "pending" is two DB stamps on `AspNetUsers`, not the in-memory queue. This answers map unknown #1 for the common case.
- **Where self-heal fails:**
  - the stamp is written **after** the write commits, in a separate statement;
  - there is **no startup sweep**;
  - self-heal only runs while the user has `/matches` open.
- **Trigger coverage:** the trigger is called from **7 call sites in 3 endpoint files**, which serve 8 write routes. No service calls it. A theory test over all 8 routes covers this today, but nothing at compile time forces a future write path to trigger. This answers map unknown #2.
- **Access gate:** the friends and messaging gates check `Matches` in the **caller → other direction only**. They are correct today only because every recompute writes both mirror rows in one statement and never deletes. This answers map unknown #3: the coupling is real and hand-kept.

Tests are strong on the happy path, the debounce queue, failure branches that must not trigger, scoring and append-only. They are thin on the worker's exception path, a real restart, the FK retry loop, forward-only stamps and the list-growth caps. The static `Clock.Now()` blocks deterministic tests of stamp ordering.

## Feature overview

What the user experiences (PRD FR-002, FR-003, FR-005, FR-006, FR-007):
- **Write.** The user rates movies, edits a watchlist or sets a city. The write returns at once.
- **Pending.** `/matches` shows "Trwa aktualizacja dopasowań" ("updating matches") while a recompute is pending. The SPA polls `/api/matches/status` every 2 s for up to 30 s, then reloads the list.
- **List.** The list ranks candidates by score:
  - shared favourites (rating ≥ 7 on both sides);
  - shared watchlist titles;
  - distance decay.
- **Fallback.** Below 5 entries the list is topped up with the closest users.
- **Append-only.** Once listed, a candidate is never removed; only re-scored.
- **Friends and messages.** From the list the user can add a friend (one-sided). After that, either side can open the conversation and send messages.

```mermaid
sequenceDiagram
  participant SPA
  participant EP as Write endpoint
  participant Svc as Service
  participant DB
  participant T as Trigger
  participant Q as Queue (per machine, in-memory)
  participant W as Worker
  participant MS as MatchService
  SPA->>EP: POST/PUT/DELETE watched | watchlist | profile
  EP->>Svc: Add/Update/Remove/MarkWatched/SetCity/SetDisplayName
  Svc->>DB: SaveChanges / ExecuteDelete / Tx commit
  alt error outcome (400/404/409)
    EP-->>SPA: error, no trigger
  else success
    EP->>T: RequestAsync (after commit)
    T->>DB: UPDATE MatchesRequestedAt = Clock.Now() (separate statement)
    T->>Q: Enqueue (quiet 2 s / max 5 s)
    EP-->>SPA: 200/201/204
  end
  loop every 250 ms
    W->>Q: TakeDue (removes)
    W->>MS: RecomputeAsync (new scope)
    MS->>DB: BEGIN; start = Clock.Now(); read Users, Matches, WatchedMovies, WatchlistItems
    MS->>DB: UPSERT A→X and X→A (no delete); ComputedAt = start (forward-only); COMMIT
    Note over W: exception → LogError, job dropped
  end
  SPA->>EP: GET /api/matches/status (every 2 s, max 30 s)
  EP->>DB: pending = profileComplete && (computed null || requested > computed)
  EP->>Q: re-enqueue if pending and not queued on this machine
  SPA->>EP: GET /api/matches when not pending
  SPA->>EP: POST /api/friends → gate Matches(caller→cand)
  SPA->>EP: GET/POST /api/conversations → gate Matches(caller→other) AND Friendship(either way)
```

## Detailed Findings

### A. Write paths (movie-ratings, watchlist, profile-city)

All 8 write routes call the trigger only on a success outcome, after the service has committed. Each error outcome returns early without triggering.

| Route | Endpoint → service | Commit | Trigger |
|---|---|---|---|
| POST `/api/watched` | `Endpoints/WatchedMoviesEndpoints.cs:36` → `Services/WatchedMoviesService.cs:53` (validates rating, movie, duplicate; unique → 409, FK → 404) | SaveChanges `WatchedMoviesService.cs:84` | `WatchedMoviesEndpoints.cs:51` [E] |
| PUT `/api/watched/{id}` | `WatchedMoviesEndpoints.cs:57` → `WatchedMoviesService.cs:101` | SaveChanges `:116-118` | `WatchedMoviesEndpoints.cs:73`. Also fires when the rating is unchanged [E] |
| DELETE `/api/watched/{id}` | `WatchedMoviesEndpoints.cs:78` → `WatchedMoviesService.cs:125-129` | `ExecuteDeleteAsync` autocommit | `WatchedMoviesEndpoints.cs:93` [E] |
| POST `/api/watchlist` | `Endpoints/WatchlistEndpoints.cs:40` → `Services/WatchlistService.cs:47` | SaveChanges `:65` | `WatchlistEndpoints.cs:52` [E] |
| DELETE `/api/watchlist/{id}` | `WatchlistEndpoints.cs:68` → `WatchlistService.cs:84-88` | `ExecuteDeleteAsync` autocommit | `WatchlistEndpoints.cs:83` [E] |
| POST `/api/watchlist/{id}/watched` | `WatchlistEndpoints.cs:89` → `WatchlistService.cs:97`. Explicit transaction `:123`; calls `WatchedMoviesService` inside it `:127-135`; rollback `:137-141`; commit `:153` | Tx commit | `WatchlistEndpoints.cs:102` [E] |
| PUT `/api/profile/city` | `Endpoints/ProfileEndpoints.cs:37` → `Services/ProfileService.cs:48` | `ExecuteUpdateAsync` autocommit `:64-68` | via `ToTriggeringResult` `ProfileEndpoints.cs:63-69` (call at `:68`). Also fires when the city is unchanged [E] |
| PUT `/api/profile/display-name` | `ProfileEndpoints.cs:48` → `ProfileService.cs:79-96` | autocommit | same helper, `ProfileEndpoints.cs:55` → `:68` [E] |

- [E] `grep RequestAsync` over `Endpoints/` and `Services/` at 7734e90 finds exactly 7 call sites, all in the three endpoint files above. The 8th route reaches the shared profile helper. No call site exists in `Services/`.
- [I] The services that perform the writes know nothing about recompute. Any write path added later, inside a service or in a new endpoint, has to remember the trigger by hand.

### B. Recompute machinery (match-recompute)

- **Trigger** `Services/MatchRecomputeTrigger.cs:13-24`.
  - A separate `ExecuteUpdateAsync` sets `MatchesRequestedAt = Clock.Now()` with `CancellationToken.None`, then calls `queue.Enqueue`.
  - The comment at `:15-17` admits that a missing stamp "is the one case the status self-heal cannot detect" [E].
- **Queue** `Services/MatchRecomputeQueue.cs:51-98`.
  - In-memory `Dictionary` per process behind a lock; one entry per user, so a burst collapses [E].
  - A user is due after a quiet period of 2 s or a max delay of 5 s (defaults `:36,39`, bound from `appsettings.json` `MatchRecompute`) [E].
  - `TakeDue` removes users as it hands them out (`:78-95`) [E].
  - It uses an injectable `TimeProvider` (`:51`) [E].
- **Worker** `Services/MatchRecomputeWorker.cs:109-151`.
  - A `PeriodicTimer` polls every 250 ms. Users are processed one at a time per machine, each in a new DI scope [E].
  - Any exception is logged and the job dropped, with no retry (`:147-150`) [E].
  - On shutdown, users already taken stay pending only in the DB (`:128-131`) [E].
- **Pending rule** `Services/MatchService.cs:129-131`: `ProfileComplete && (ComputedAt is null || RequestedAt > ComputedAt)` [E].
  - `start = Clock.Now()` is captured right after `BEGIN` (`:176-180`).
  - `ComputedAt = start` is written forward-only (`:279-283`).
  - So a write that lands during a recompute keeps the user pending [E].
- **Self-heal** `Endpoints/MatchesEndpoints.cs:54-76`: `GET /status` re-enqueues the user if pending and not queued on this machine (`:69-72`) [E].
- **No startup sweep:** the only `AddHostedService` in non-test code is the worker (`MatchesEndpoints.cs:31`) [E].
- **DI** `MatchesEndpoints.cs:17-33`:
  - registration of match-recompute lives in matching's `AddAppMatches`;
  - the queue is a singleton per process (`:29`);
  - the trigger is scoped (`:30`) [E].
- **Fly:** `fly.toml:14-17` sets `min_machines_running = 1` and a `web` process group. The machine count is not declared in the repo. `context/foundation/lessons.md:5` records "2 web machines roll one by one" [E]. Each machine has its own queue [I].

### C. Recompute work (matching)

`Services/MatchService.cs:140-327`. All anchors in this section are [E] unless marked.

- **Retry wrapper:** an execution strategy wraps the transaction. An FK violation is retried while `attempt < MaxAttempts` (3) (`:49`, `:157`); after that it propagates to the worker.
- **Requester checks:** a missing user returns NotFound. A profile without city or display name returns ProfileIncomplete. Neither writes anything or stamps the user.
- **Reads:**
  - the requester's listed candidates (`Matches`);
  - eligible `Users` (with city and display name);
  - shared favourites from `WatchedMovies`, rating ≥ 7;
  - shared watchlist from `WatchlistItems`.
  - `Friendships` is read only by `ListAsync` (`:86-91`), not by the recompute.
- **Selection:**
  - re-score listed candidates who are still eligible; ineligible ones keep their old score;
  - add up to `MaxNewStrongCandidates` = 20 new strong candidates (`:39`, `:259`);
  - top up with the closest candidates until `MinListSize` = 5 (`:42`).
- **Write:** `UpsertMirroredAsync` (`:289-327`) writes A→X and X→A in one raw-SQL `INSERT … ON CONFLICT DO UPDATE`, ordered to avoid deadlocks. `FirstMatchedAt` is kept.
- **No DELETE:** rows disappear only through the FK cascade on account deletion (`:31-35`).
- **The candidate side is not stamped:** X's mirror row is updated, but X's `MatchesRequestedAt`/`MatchesComputedAt` are not touched. X's own candidate discovery does not run [E]. So an X who has `/matches` open does not see A appear until X's next recompute or reload [I].
- **Scoring** `Services/MatchScoringService.cs:32-59`: pure, with no dependencies; Ce 0 per `context/map/evidence/3-structure.md` [E].

### D. Read path (matching, SPA)

- `GET /api/matches` (`MatchesEndpoints.cs:42-50`) → `ListAsync` (`MatchService.cs:52-106`) [E].
  - Ordered by score, then `FirstMatchedAt`.
  - Shared titles are computed live, capped at `MaxSharedTitles` = 5.
  - `isFriend` reflects the caller's direction.
- `GET /status` → `MatchStatusResponse(Pending, ProfileComplete)` [E].
- `POST /recompute` runs a synchronous recompute (`MatchesEndpoints.cs:78-99`, 409 `profile_incomplete`). The SPA does not call it; only seed scripts and tests do [E].
- SPA `ClientApp/src/app/matches/matches.component.ts`:
  - loads status and list in parallel on init;
  - while pending, polls status every 2 s for at most 15 checks (30 s), then reloads the list;
  - on timeout it sets `slow`;
  - a poll error stops polling;
  - there is no refresh outside this page [E, per the e2e-trace worker; anchors `:14-16`, `:137-181`].

### E. Access gates (friends-messaging)

- **Add friend:** `FriendsService` `MatchedCandidates` (`Services/FriendsService.cs:97-102`) requires `Matches(UserId == caller && CandidateId == candidate)` and a complete candidate profile. Otherwise 404 [E].
- **Conversations:** `ConversationService.CanTalkAsync` (`Services/ConversationService.cs:36-41`) requires all of [E]:
  - `caller != other`;
  - `Matches(caller → other)`;
  - a `Friendship` in either direction.
- **Direction dependency:** both gates query one direction only. They depend on the recompute always writing both mirror rows (`MatchService.cs:289-327`) and never deleting them [E for the queries; I for the dependency].
- **Friends list:** it does not re-check the match (`FriendsService.cs:85-95`, per the trace worker) [E].

### F. Test coverage of the flow

Rows marked "covered" were read in test code; no coverage tool was run [E].

| Area | Covered | Main gap |
|---|---|---|
| All 8 write routes request a recompute | Yes: one theory over the 8 routes, `tests/MovieDate.Tests/MatchRecomputeTests.cs:76-126` | The theory is hand-extended for new routes |
| Failure branches do **not** trigger | Most: `MatchRecomputeTests.cs:128-161` | Not covered: watchlist add 409 duplicate, mark-watched 400, mark-watched 409 rollback |
| Queue debounce | Yes: unit tests with a manual `TimeProvider` (`MatchRecomputeTests.cs:362-430`) and a live burst test (`:163-187`) | The live burst test depends on wall-clock time (300 ms quiet), so it is a possible CI flake [I] |
| Worker survives a failing job | **No**: `:189-212` covers a ProfileIncomplete *outcome*, not an exception | `catch (Exception)` at `MatchRecomputeWorker.cs:147` is unexercised |
| Restart / durability | Partial: `:214-239` nulls the stamps and checks `/status` re-enqueues | No real host restart; no "RequestedAt > ComputedAt with an empty queue"; no two-machine case |
| FK retry loop; forward-only `ComputedAt`; write during a running recompute | **No** | Static `Clock.Now()` (`Services/Clock.cs`) prevents deterministic time control |
| Scoring, ranking, fallback, mirror, append-only (lost favourite), concurrent recomputes, no PII | Yes: `MatchScoringTests.cs:15-103`, `MatchesTests.cs:88-253` | No test of the 20-new-strong cap, `MaxSharedTitles`, or append-only after a city move |
| Gates: stranger, own id, matched-but-not-friend → 404; one-way friendship can reply | Yes: `FriendsTests.cs:56-87`, `ConversationsTests.cs:50-115` | API tests seed match rows directly (`TestPairs.cs:47-65`, no-worker fixture `PostgresAppFixture.cs:65-78`). Only `ClientApp/e2e/messaging.spec.ts` drives the gate from a real recompute |
| SPA matches service and component, polling, timeout | Yes: `matches.service.spec.ts:36-79`, `matches.component.spec.ts:67-260`; e2e `critical-path.spec.ts:13-77` | Probably untested: initial list failing while status succeeds; profile becoming incomplete during polling [I] |

### G. Blast radius: what changes together

Sources:
- the static graph in `context/map/evidence/3-structure.md`;
- co-change commits 001ccf5, e9d9d2b, 3edc850 (22 files: the write endpoints, queue/trigger/worker, the stamps migration and tests) and e6f9fa6 (friends: `MatchResponse.IsFriend`, `MatchService`, `TestPairs`);
- direct inspection of the seams.

| If you change… | …also change or check | How known |
|---|---|---|
| **Where the trigger is called** (endpoints → services) | Constructors of `WatchedMoviesService`, `WatchlistService`, `ProfileService`; the endpoint lambda parameters; success-only and after-commit semantics (stamping inside a rolled-back transaction would create phantom pendings); the mark-watched path calls `WatchedMoviesService` inside its transaction, so a double trigger is debounced but redundant; keep `CancellationToken.None` | 7 call sites [E]; 3edc850 co-change [E]; the theory test survives because it is HTTP-level [E] |
| **Match entity / scoring output** | `Data/AppDbContext.cs` Matches config (composite PK, `IX_Matches_CandidateId`, `CK_Matches_NotSelf`, cascades); new migration + Designer + snapshot (generated); **raw SQL column list in `MatchService.cs:313-326`** (hand-kept; compiles fine, fails only at runtime); `Contracts/MatchResponse.cs` ↔ TS `Match` in `matches.service.ts`; `TestPairs.cs` seeds `new Match{…}`; `movie-date.http`. Migrations must be additive (lessons.md, rule 1) | seam inspection [E]; 001ccf5, e9d9d2b [E] |
| **Recompute durability** (sweep or persisted queue) | `AddAppMatches` registrations; `MatchRecompute:*` config keys in 4 places (`appsettings.json`, `PostgresAppFixture.cs:18-34`, `ClientApp/playwright.config.ts`, the options class); `PostgresAppFixture.cs:65-78` removes the worker by `ImplementationType`, so renaming the worker breaks that silently; must not run in `--migrate` mode; must coexist with or replace the `/status` self-heal; a mix of `Clock` and `TimeProvider` | seam inspection [E] |
| **Match-list semantics** (mirror, append-only) | `FriendsService.cs:97-102`, `ConversationService.cs:36-41` (one-direction queries); `TestPairs.cs:47-65`; `messaging.spec.ts`; `scripts/cleanup-test-users.sql`; `ListAsync` profile-completeness filter, aligned with the friends gate | queries [E]; e6f9fa6 co-change [E] |

**Hand-kept contracts with no build guard** [E]:
1. C# `MatchResponse`/`MatchStatusResponse` ↔ TS `Match`/`MatchStatus`. Checked field by field at 7734e90: **no drift** (10/10 and 2/2 fields).
2. The raw-SQL upsert column list ↔ the `Match` entity.
3. The `MatchRecompute:*` config keys in 4 places.
4. The `profile_incomplete` string literal (endpoint, TS spec, C# test).
5. "Every write path must trigger", plus the hand-extended theory test.
6. The mirror/direction assumption between `UpsertMirroredAsync` and the two gates.

## Technical debt

Ordered by risk to the flow. Each item carries its label.

1. **The write and the stamp are not atomic** [E: `WatchedMoviesEndpoints.cs:45-51`, `WatchlistEndpoints.cs:99-103`, `ProfileEndpoints.cs:63-69`, `MatchRecomputeTrigger.cs:15-21`].
   - The write commits, then a separate statement stamps `MatchesRequestedAt`.
   - If the process dies or the stamp fails in between, the change never reaches the match list, and self-heal cannot detect it (the code says so).
   - [I] If the stamp throws, the client gets a 500 for a write that succeeded. A retried "add" then returns 409, which does not trigger, so the stamp is never written.
2. **Recompute is triggered from endpoints, not from the write services** [E: 7 call sites, 0 in `Services/`].
   - Correctness depends on every current and future endpoint remembering to trigger, plus a hand-extended theory test.
   - It also places match-recompute's DI registration inside matching's `AddAppMatches` [E].
3. **No startup sweep; self-heal runs only through `/status`** [E: one hosted service, `MatchesEndpoints.cs:31`; re-enqueue at `:69-72`].
   - [I] After a deploy or restart, a pending user is recomputed only when they next open `/matches`. Until then their row on other users' lists is stale too.
4. **A failed recompute is logged and dropped, with no retry or backoff** [E: `MatchRecomputeWorker.cs:147-150`; the FK retry gives up after 3 attempts, `MatchService.cs:157`].
   - [I] A user whose recompute fails persistently is retried only while their page polls (up to 30 s), then abandoned with `slow = true`.
   - There is no error tracker in the repo (repo map, Limitations), so these failures exist only in logs.
5. **The pending comparison mixes clocks across machines** [I from `Clock.cs` (static `DateTimeOffset.UtcNow`), `MatchRecomputeTrigger.cs:21`, `MatchService.cs:180`].
   - `RequestedAt` comes from the write machine; `start` comes from the recompute machine.
   - If the recompute machine's clock runs ahead, a write that lands during that recompute can be silently lost.
   - Also, `Clock.Now()` is static, so stamp ordering cannot be tested deterministically [E].
6. **The access gates rely on mirror rows through a one-direction query** [E: `FriendsService.cs:102`, `ConversationService.cs:39`].
   - Safe today because the upsert writes both rows in one statement and never deletes.
   - [I] Any future expiry, partial delete or non-mirrored write breaks messaging asymmetrically.
   - Test-plan Phase 3 (privacy, R2), which would guard this, has not started (repo map).
7. **The candidate side is never marked pending** [E: `MatchService.cs:279-283` stamps only the requester]. [I] The other user's open page does not refresh. This is consistent with the PRD's "observes shortly after", but not stated in it.
8. **Raw-SQL upsert duplicates the entity shape** [E: `MatchService.cs:313-326`]. A column change compiles and fails only at runtime. Integration tests catch it before deploy.
9. **Redundant recomputes** [E: unchanged rating at `WatchedMoviesService.cs:116-118`, unchanged city at `ProfileService.cs:64-68`]. Both still stamp and recompute. Harmless cost.
10. **Dead SPA error mapping** [E: `matches.service.ts:46-48` maps 409 for `GET /api/matches`, which never returns 409 (`MatchesEndpoints.cs:42-50`)]. A leftover from before the status endpoint existed.
11. **Test debt:** see section F. The highest-value gaps are:
    - the worker's exception path;
    - a real restart, or "pending with an empty queue";
    - the FK retry loop and forward-only stamp;
    - the 20-candidate cap;
    - gates driven by real recompute output at the API level.

## Code References

- `Endpoints/WatchedMoviesEndpoints.cs:51,73,93`: trigger call sites for rating add, update and delete
- `Endpoints/WatchlistEndpoints.cs:52,83,102`: trigger call sites for watchlist add, delete and mark-watched
- `Endpoints/ProfileEndpoints.cs:63-69`: `ToTriggeringResult` (city and display name)
- `Services/MatchRecomputeTrigger.cs:13-24`: separate stamp + enqueue; the self-heal blind-spot comment
- `Services/MatchRecomputeQueue.cs:51-98`: in-memory debounce queue
- `Services/MatchRecomputeWorker.cs:115-151`: poll loop; catch-log-drop
- `Endpoints/MatchesEndpoints.cs:17-33`: DI of the queue, trigger and worker; `:54-76` status + self-heal
- `Services/MatchService.cs:129-131`: pending rule; `:176-180` start stamp; `:279-283` forward-only `ComputedAt`; `:289-327` mirrored raw-SQL upsert; `:39,42,45,49` caps and retry constants
- `Services/MatchScoringService.cs:32-59`: scoring formulas
- `Services/FriendsService.cs:97-102`, `Services/ConversationService.cs:36-41`: access gates
- `Services/Clock.cs`: static clock
- `tests/MovieDate.Tests/MatchRecomputeTests.cs:76-161`: trigger and no-trigger theories
- `tests/MovieDate.Tests/PostgresAppFixture.cs:65-78`: no-worker fixture
- `tests/MovieDate.Tests/TestPairs.cs:47-65`: seeded mirror rows

## Architecture Insights

- **Recompute design:** "pending" is a DB fact (two stamps), so the in-memory queue is only an optimisation. The append-only, mirrored upsert makes concurrent recomputes idempotent. This is a deliberate, sound design. Its weak points are the edges: the stamp after commit, the absence of a sweep, and the dropped failures.
- **Access control:** the match list is effectively an access-control list for friends and messaging. This is a product-level coupling that the code keeps by convention (direction plus mirror), not by a shared abstraction.
- **Layering drift:** the cross-cutting rule "every successful write requests a recompute" sits at the endpoint layer. CLAUDE.md's module rule puts business logic in `Services/`.

## Historical Context (from prior changes)

- `context/archive/2026-11-03-async-match-recompute/plan.md`: the S-06 design (stamps, debounce, status self-heal). There is no `reviews/` folder; the slice was never impl-reviewed (repo map, discussion evidence).
- `context/archive/2026-11-03-ranked-match-list/plan.md`: the S-05 scoring, mirror and append-only design. No review either.
- `context/archive/2026-11-03-first-message-to-friend/`: research, plan and 2 impl-reviews; introduced the gates (commit e6f9fa6).
- `context/foundation/lessons.md`: "Keep migrations backward-compatible" constrains any Match or stamp schema change. "Key per-user data on the user id" is respected; the trigger is keyed on `AspNetUsers.Id` (`MatchRecomputeTrigger.cs:9`).
- `context/foundation/roadmap.md` (Parked): a separate recompute worker process, and observability beyond `/health`.

## Related Research

- `context/map/repo-map.md` and `context/map/evidence/*`: the repo map this research builds on.
- `context/changes/testing-critical-path-e2e/research.md`: research for the e2e path, which exercises this flow through `critical-path.spec.ts`.

## Open Questions

- [U] How many Fly machines actually run in production? The repo does not declare a count; `lessons.md` says 2. Are their clocks synchronised closely enough for debt item 5?
- [U] Does the CI e2e job run with the short `MatchRecompute` timings from `playwright.config.ts`? No `MatchRecompute` key appears in `.github/workflows`.
- [U] Is "the other user's open list doesn't refresh until they reload" acceptable product behaviour (debt item 7)? This is a product question, not a code question.
- [U] Will an account-deletion feature (not built) need to re-stamp the users who had the deleted user on their list?
- [U] The `EnableRetryOnFailure` / execution-strategy settings in `Data/DataServiceExtensions.cs` were not inspected. They affect how often the FK retry and the strategy wrapper actually repeat.
