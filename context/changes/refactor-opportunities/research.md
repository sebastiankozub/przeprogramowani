---
date: 2026-11-04T10:15:00+01:00
researcher: Claude Code (Opus 5.5) for the sole maintainer
git_commit: 8ccd119
branch: architect
repository: sebastiankozub/przeprogramowani-MovieDate
topic: "Refactor opportunities from the match-flow technical debt: which to fix, target shape, order"
tags: [research, refactor-opportunities, match-recompute, matching, technical-debt, verified]
status: complete
last_updated: 2026-11-04
last_updated_by: Claude Code (Opus 5.5)
last_updated_note: "Structural claims behind the ranking verified with ast-grep 0.51.1 (M4L4 m4l4-3); 2 counts corrected in place; ranking and intentionality verdicts unchanged."
verification_commit: 8ccd119 (code verified; report verification committed on top)
---

# Research: refactor opportunities (match flow)

**Input (treated as collected evidence, not re-derived):**
- `context/changes/match-flow-analysis/research.md`: Feature overview, Technical debt, §F tests, §G blast radius, ast-grep verification.
- Priors: `context/map/repo-map.md`, `context/foundation/lessons.md`.

**Method:** the candidate list is classified first. Each candidate was then explored by three read-only subagents:
1. **current shape**;
2. **history and intentionality** (plans, reviews, `git log -L`/blame, PRs);
3. **migration feasibility** (reuse vs new abstraction, existing guards and CI, first prerequisite).

No code was changed and no decision is made here. The ranking at the end is a proposal for the planning session.

Labels: **[E]** evidence (file:line / sha / path:line in a doc), **[I]** inference, **[U]** unknown. Code anchors refer to `8ccd119`; code is unchanged since `7734e90`.

## Candidate list and classification

The source is every problem recorded in `match-flow-analysis/research.md` (Technical debt items 1–11 and the §G "hand-kept contracts" list). A **candidate** is a problem whose fix would change code structure. Everything else is kept as an input to cost and feasibility.

| # | Problem (source) | Class | Why |
|---|---|---|---|
| C1 | The write and its pending stamp are not atomic (debt 1) | **candidate** | Moves the stamp into the write's transaction boundary |
| C2 | Recompute is triggered from 7 endpoint call sites, not from the write services; recompute DI sits inside `AddAppMatches` (debt 2) | **candidate** | Moves responsibility between layers |
| C3 | No startup sweep; self-heal runs only via `GET /status` (debt 3) | **candidate** | New recovery component plus a shared pending predicate |
| C4 | A failed recompute is logged and dropped, with no retry (debt 4) | **candidate** | Worker failure policy |
| C5 | Static `Clock.Now()` vs injectable `TimeProvider`; pending compares clocks from two machines (debt 5) | **candidate** | Replaces a static dependency |
| C6 | Access gates query `Matches` in one direction and rely on mirror rows (debt 6) | **candidate → domain** (see C6) | Root cause is the definition of "match" |
| C7 | The raw-SQL upsert duplicates the `Match` entity shape (debt 8) | **candidate** | Ties the SQL to the EF model |
| C8 | C# contracts ↔ hand-written TS types, no generated client (§G hand-kept 1) | **candidate** | Contract generation or a guard |
| — | The candidate side is never marked pending (debt 7) | not a candidate | A product non-goal: `async-match-recompute/plan.md:38` "No push to the *other* side" [E] |
| — | Redundant recomputes on unchanged values (debt 9) | not a candidate | Cost input; harmless work |
| — | Dead SPA 409 mapping (debt 10) | not a candidate | Trivial cleanup, no structural change |
| — | Test gaps (debt 11, §F) | not a candidate | Feasibility input: they decide the first prerequisite steps below |
| — | `MatchRecompute:*` config keys in 4 places; `profile_incomplete` literal (§G) | not a candidate | Blast-radius input |

## Cross-cutting findings that affect every candidate

- **CI runs, but nothing enforces it.**
  - [E] `.github/workflows/ci.yml` runs `backend` (build plus the C# integration suite with Testcontainers), `frontend` (`ng test` plus prod build) and `e2e` (Playwright) on every PR and every push to main.
  - [E] `gh api …/branches/main/protection` returns 404 "Branch not protected", and `…/rulesets` returns `[]`, so a red run blocks nothing.
  - [E] `deploy.yml` is `workflow_dispatch` only and does not depend on CI.
  - [E] `impl-review.yml` posts a status that fails only on REJECTED, can be bypassed with a label, and is not a required check.
  - [I] Making `backend`, `frontend` and `e2e` required on `main` is a settings change, and the cheapest prerequisite for every refactor below.
- **Execution strategy.**
  - [E] `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: 5s)` (`Data/DataServiceExtensions.cs:22-27`), so every explicit transaction must run inside `CreateExecutionStrategy()`.
  - [E] The pattern already exists twice: `WatchlistService.MarkWatchedAsync` (`Services/WatchlistService.cs:113-194`, with `verifySucceeded`) and `MatchService.RecomputeAsync` (`Services/MatchService.cs:147-162`).
  - This answers the research's open question about `EnableRetryOnFailure`.
- **`--migrate` mode.** [E] It exits before `app.Run()` (`Program.cs:22-25`), so no hosted service runs during the Fly release command.
- **e2e timings.** [E] The e2e job's recompute timings come from `ClientApp/playwright.config.ts:35-36` (`MatchRecompute__QuietPeriod=5s`, `MaxDelay=15s`), not from the workflow. This answers the research's open question about CI timings.
- **Decisions that constrain any refactor** [E]:
  - recompute stays in-process, with no job library or separate worker (`tech-stack.md:30`, `async-match-recompute/plan.md:37`; a separate worker is parked at `roadmap.md:229`);
  - **no scheduled or batch recompute** (PRD Non-Goals `prd.md:97`, parked at `roadmap.md:223`), so any sweep must be framed as recovery;
  - 2 Fly web machines rolled one at a time (`lessons.md:7`, `async-match-recompute/plan-brief.md:11`);
  - migrations stay backward-compatible (`lessons.md:5-10`);
  - the list is append-only and mirrored (`prd.md:82`, `ranked-match-list/plan.md:66`);
  - `POST /api/matches/recompute` stays, because scripts and tests use it (`async-match-recompute/plan.md:40`).

## Per candidate

### C1: Make the write and its pending stamp atomic

**Current shape**
- [E] The stamp is a separate autocommit `ExecuteUpdateAsync(MatchesRequestedAt, Clock.Now())` with `CancellationToken.None`, run after the write has committed (`Services/MatchRecomputeTrigger.cs:19-21`), then `queue.Enqueue` (`:23`).
- [E] Write shapes:
  - `SaveChangesAsync` with an implicit transaction: watched add `WatchedMoviesService.cs:84`, watched update `:118`, watchlist add `WatchlistService.cs:65`;
  - `ExecuteDeleteAsync` autocommit: watched delete `WatchedMoviesService.cs:125-127`, watchlist delete `WatchlistService.cs:84-86`;
  - a single `ExecuteUpdateAsync` on `db.Users` that already sets 2 columns: `ProfileService.cs:64-68`, `:90-94`;
  - one explicit, strategy-wrapped transaction: mark-watched `WatchlistService.cs:113-153`.
- [I] The stamp must not be written through a tracked `AppUser`: Identity's `ConcurrencyStamp` is a concurrency token, and `ProfileService` deliberately avoids tracked user writes (`ProfileService.cs:55-60`).

**Intentionality: deliberate shape; the atomicity trade-off was never analysed.**
- [E] `async-match-recompute/plan.md:46`: "Write path: endpoint succeeds → `MatchRecomputeTrigger.RequestAsync(userId)` stamps `MatchesRequestedAt = now` (one `ExecuteUpdate`)".
- [E] The trigger comment from 3edc850 (blame) admits the gap: "a missing stamp is the one case the status self-heal cannot detect".
- [E] No plan, brief or review considers stamping inside the write's transaction. S-06 was never reviewed.

**Feasibility**
- Reuse: the execution-strategy + transaction pattern, and multi-`SetProperty` `ExecuteUpdate`. Nothing new.
- Path:
  1. Split the trigger into *stamp* (inside the write's transaction or statement) and *enqueue* (after commit; it is in-memory and must stay after commit).
  2. Profile writes: add one `.SetProperty` to the existing statement. This is atomic for free.
  3. SaveChanges and ExecuteDelete writes: wrap them in a strategy transaction with the stamp `ExecuteUpdate`.
  4. Mark-watched: stamp once, inside its existing transaction.
- Guards [E]:
  - the HTTP-level theory over 8 routes (`tests/MovieDate.Tests/MatchRecomputeTests.cs:76-126`);
  - the theory asserting 11 error outcomes leave the stamps unchanged (`:128-161`). This is the key guard against phantom pendings from a rolled-back stamp.
- Gaps [E]: no no-trigger case exists for watchlist-add 409, mark-watched 400, or mark-watched 409/rollback. Those are exactly the rollback paths C1 touches.
- First prerequisite: add those 3 cases, plus a mark-watched rollback case asserting unchanged stamps.
- Size M; schema none; reversibility high.
- Target shape: the stamp commits atomically with the user's write; the enqueue happens after commit.

### C2: Move the recompute request into the write services; give recompute its own registration

**Current shape**
- [E] 7 `RequestAsync` call sites (`Endpoints/WatchedMoviesEndpoints.cs:51,73,93`, `Endpoints/WatchlistEndpoints.cs:52,83,102`, `Endpoints/ProfileEndpoints.cs:68` via `ToTriggeringResult`). The trigger is a parameter of 9 (report: 8) endpoint-file functions: the 8 route lambdas plus the `ToTriggeringResult` helper (`ProfileEndpoints.cs:64`).
- [E] Recompute DI (options + `ValidateOnStart`, `TryAddSingleton(TimeProvider.System)`, queue, trigger, worker) lives in matching's `AddAppMatches` (`Endpoints/MatchesEndpoints.cs:17-33`).
- [E] Services are sealed primary-constructor classes over `AppDbContext`, each with its own Outcome enum. Feature DI follows the `AddAppXxx` convention (`Program.cs:9-17`).
- [I] A naive move would trigger twice in mark-watched (nested `WatchedMoviesService` call), and without C1 it would stamp before a possible rollback.

**Intentionality: deliberate placement with no stated reason.**
- [E] `async-match-recompute/plan.md:110-113`: "After a **success** outcome only, the 8 handlers call `MatchRecomputeTrigger.RequestAsync`".
- [E] `:115` puts the DI in `AddAppMatches`. Implemented in 3edc850.
- [E] Service-level triggering was never compared.
- [I] The probable unstated reason is the nested-transaction rollback in mark-watched.
- [E] The placement contradicts CLAUDE.md's "business logic in `Services/`" rule, and nothing records that tension.

**Feasibility**
- Reuse: the existing trigger. The only new piece is an `AddAppMatchRecompute()` extension, per the CLAUDE.md module rule.
- Path:
  1. Pure DI extract (its own commit).
  2. Inject the trigger, or C1's split stamp/enqueue, into 3 services.
  3. Delete the 7 call sites and `ToTriggeringResult`.
  4. Make mark-watched trigger once.
- Guards [E]:
  - the HTTP-level theories survive the move;
  - the no-worker fixture removes the worker by implementation type (`PostgresAppFixture.cs:72-74`), so moving the registration is safe and renaming the worker fails loudly.
- Gap [E]: nothing compels a future write to trigger, and the trigger is sealed and concrete with no seam for service-level tests.
- First prerequisite: the C1 no-trigger additions, then the DI extract.
- Size M; schema none; reversibility high.
- Target shape: write services own "successful write ⇒ request recompute", and recompute has its own registration module.

### C3: Startup sweep for DB-pending users

**Current shape**
- [E] The only hosted service is the worker (`MatchesEndpoints.cs:31`).
- [E] Self-heal runs only in `GET /status` (`:69-72`), and only for the caller.
- [E] The pending rule is evaluated in C# after a projection (`MatchService.cs:129-131`). There is no queryable predicate.
- [E] There is no index on the stamps (`Data/Migrations/20261103085214_AddMatchRecomputeStamps.cs:14-21`; `AppDbContext.cs:48` indexes only `CityId`).
- [E] `Enqueue` is idempotent per user (`MatchRecomputeQueue.cs:33-42`).

**Intentionality: deliberate; the risk was accepted implicitly.**
- [E] `async-match-recompute/plan-brief.md:52`: "Queued work is lost on restart; healed only when the user (or their page) checks status or writes again."
- [E] `roadmap.md:171` asks the owner whether that recovery is acceptable ("Block: no"). [E] No written answer was found.
- A sweep was never named as a rejected option.

**Feasibility**
- Reuse: the queue and the worker. New: a one-shot startup hosted step and one shared `Expression<Func<AppUser,bool>>` pending predicate.
- [E] Safe in migrate mode by construction.
- [I] With 2 machines, a double enqueue is harmless: the upsert is idempotent, rows are ordered against deadlocks, and the stamp is forward-only.
- [I] Constraint: it must be framed as recovery, not as a scheduled batch (`prd.md:97`).
- Guards [E]: `MatchRecomputeTests.cs:214-239` covers self-heal through `/status` only. No test boots a host with a pending user.
- First prerequisite: extract the pending predicate and pin it with the existing status tests.
- Size S–M; schema none (an index is optional and additive); reversibility high.
- Target shape: a one-shot recovery step at start that enqueues every DB-pending user, sharing one predicate with `GetStatusAsync`.

### C4: Retry failed recomputes

**Current shape**
- [E] `catch (Exception)` → `LogError`, the job is dropped (`Services/MatchRecomputeWorker.cs:49-52`).
- [E] Inside `MatchService`, an FK-violation loop (`MaxAttempts = 3`, `:49`, `:157-162`) runs over the execution strategy's own retries.
- [E] The queue entry has no attempt state (`MatchRecomputeQueue.cs:72`).

**Intentionality: deliberate.**
- [E] `async-match-recompute/plan.md:53`: "an exception in one job must be logged and swallowed (the user stays pending and self-heals on the next status check)".
- [E] `:37` excludes a durable queue and Hangfire.

**Feasibility**
- New per-user attempt state, plus a seam to make `MatchService` throw. It is resolved by concrete type (`MatchRecomputeWorker.cs:41`) [E].
- [E] There is no test on the exception path at all.
- [I] C3 plus `/status` already give eventual retry without new worker code.
- First prerequisite: a "worker survives a throwing job and processes the next user" characterization test, which needs that seam.
- Size S–M; schema none (in-memory); reversibility high.
- Target shape: bounded, backed-off re-enqueue, with the final failure surfaced.

### C5: Injected `TimeProvider` instead of static `Clock`; one clock for pending stamps

**Current shape**
- [E] `internal static class Clock` (`Services/Clock.cs:3-13`, µs truncation).
- [E] 8 call sites in 6 `Services/` files (verified by ast-grep in `match-flow-analysis`).
- [E] `TimeProvider` is used only by the queue (`MatchRecomputeQueue.cs:26`), registered with `TryAddSingleton` in `AddAppMatches` (`:28`), and is already app-wide in DI.
- [E] Pending compares `RequestedAt` (trigger machine, `MatchRecomputeTrigger.cs:21`) with `start` (recompute machine, `MatchService.cs:180`).

**Intentionality: accidental.**
- [E] `Clock` came from b7e11e4, fixing want-to-watch-list impl-review F5 (`context/archive/2026-11-03-want-to-watch-list/reviews/impl-review.md:74-82`: "Move to a shared internal helper"). That was a de-duplication finding with no testability discussion.
- [E] `TimeProvider` first appears in 3edc850, which the S-06 plan does not mention.
- [E] S-08's plan already treats the two as interchangeable ("the existing `Clock`/`TimeProvider` service", `first-message-to-friend/plan.md:156`).

**Feasibility**
- Reuse `TimeProvider`.
- Path:
  1. Move its registration to an app-level extension (no-op).
  2. Add a `TimeProvider`-based helper that keeps the µs truncation.
  3. Migrate the 6 files one per commit, then delete `Clock`.
- Optional variant: take the two pending stamps from the DB clock, which removes cross-machine skew. [U] Npgsql translation of a DB clock inside `SetProperty` was not checked. [I] Postgres `now()` is transaction-start time, which matches `start` (captured after `BEGIN`).
- Guards [E]: queue tests with a manual `TimeProvider` (`MatchRecomputeTests.cs:362-430`).
- [I] The change enables the missing tests for forward-only `ComputedAt` and for a write during a running recompute.
- [I] Constraint: live debounce tests need real time in the fixture.
- First prerequisite: the registration move.
- Size M; schema none; reversibility high, per file.
- Target shape: app time comes from an injected `TimeProvider`; pending stamps come from one clock.

### C6: One-direction access gates over mirrored rows: **domain at root, stop**

**Current shape**
- [E] 4 `db.Matches` reads, all caller-side (`FriendsService.cs:102`, `ConversationService.cs:39`, `MatchService.cs:56,204`).
- [E] One writer writes both mirror rows in one statement (`MatchService.cs:295-327`).
- [E] `IX_Matches_CandidateId` already exists (`AppDbContext.cs:112-113`).
- [E] There is no shared "are matched" abstraction. `CanTalkAsync` bundles match and friendship checks; `FriendsService.MatchedCandidates` is a second copy.

**Intentionality: deliberate.**
- [E] `first-message-to-friend/plan.md:11-14`: "Matches are the gate … Rows are written in both directions … and never deleted … So 'X is on my list' is one stable PK lookup, and it is symmetric."

**Verdict.**
- [I] A query helper would only centralise today's assumption.
- [E] The PRD uses two notions:
  - a per-user list (`prd.md:64`, `:82`);
  - a pair relation ("two users who have matched", `prd.md:38`; "anyone on the user's matched list", `:52`, `:67`).
- Whether a match is a **symmetric pair** (the mirror as an invariant) or a **per-user list entry** (the gate must choose a direction) is a business-concept question. That is out of scope for a structural refactor and goes to the DDD notes (M4L5).
- The cheap characterization test (single-direction rows pinning today's 404/200) is useful either way. It also feeds test-plan Phase 3 (privacy, R2).

### C7: Raw-SQL mirrored upsert vs the EF model

**Current shape**
- [E] One `ExecuteSqlAsync` `INSERT … SELECT FROM unnest(…) ORDER BY … COLLATE "C" ON CONFLICT ("UserId","CandidateId") DO UPDATE` with 7 hand-typed columns (`MatchService.cs:313-326`) against 7 properties in `Data/Match.cs:9-21`.
- [I] EF Core 10 has no first-class upsert.

**Intentionality: deliberate, for concurrency.**
- [E] `ranked-match-list/plan.md:66`: "Write rows with `INSERT … ON CONFLICT … DO UPDATE` … Never issue a `DELETE` on `Matches`"; commit e9d9d2b.
- The duplicated column list was not discussed.

**Feasibility**
- Replacement means a new dependency or load-then-update, which loses single-statement ordered locking. **Keep the SQL.**
- [I] A new *nullable* `Match` column would pass silently without being written.
- First (and only) step: a guard test comparing the EF model's column names for `Match` with the SQL column list. Size S; reversibility high.
- Target shape: the set-based upsert stays, guarded against the model.

### C8: C# contracts ↔ hand-written TS interfaces

**Current shape**
- [E] `Microsoft.AspNetCore.OpenApi` 10.0.11 is referenced (`movie-date.csproj:13`); `MapOpenApi()` runs in Development only (`Program.cs:28-31`).
- [E] No build-time document generation.
- [E] 16 `Results.Ok` sites and 0 `TypedResults`/`.Produces<T>` in `Endpoints/`, so a generated document has no response schemas.
- [E] 19 contract records; TS interfaces mirroring them are hand-written in 6 (report: 7) service files, 12 interfaces in all. The 7th file exporting an interface, `ClientApp/src/app/shared/api-errors.ts:18` (`ErrorMessages`), is a UI type, not a contract twin.
- [E] No drift was found for Match/MatchStatus.

**Intentionality: unknown.** No decision is recorded either way. The pattern was set without debate in `rate-watched-movies/plan.md:153` [E].

**Feasibility**
- Path A (generate): type all endpoints first, then generate the document at build time, add a TS generator, and add a CI diff step. Size L.
- Path B (guard): a JSON key-set snapshot test per contract. Size S.
- Target shape: generated types, or guarded hand-written ones.

## Refactor opportunities (ranked)

Ranked by **debt cost vs change cost**, using only the evidence above. This is a proposal for the planning session; the choice is made there.

### 1. Atomic, service-owned recompute request (C1 + C2 together)

- **Current → target**
  - Today, 3 endpoint files call the trigger at 7 sites, after the write has committed, and the trigger stamps in a separate statement.
  - In the target, the 3 write services request the recompute themselves. The stamp is written in the same transaction (or statement) as the user's write; the in-memory enqueue happens after commit. Recompute gets its own `AddAppMatchRecompute()` registration.
- **Why first**
  - It is the only failure in the flow that loses a user's change silently and **cannot self-heal**; the code says so itself (`MatchRecomputeTrigger.cs:15-17`).
  - It also closes the module-rule drift (business rule at the endpoint layer).
  - The original placement was never justified (plan `:110-113` decides it; no alternative was weighed) and was never reviewed.
  - Doing C1 alone would split the rule across two layers, and C2 alone would stamp before a possible rollback. So they belong together.
- **Change cost:** medium. 3 services, 3 endpoint files, the trigger, DI and tests. No schema change. Every step reuses an existing pattern (strategy transaction, multi-`SetProperty`).
- **Blast radius** (`match-flow-analysis` §G, scenario a):
  - 3 service constructors and the 8 endpoint lambda signatures;
  - success-only and after-commit semantics;
  - mark-watched's nested call;
  - the HTTP-level theories survive.
- **Incremental path**
  1. Add the missing no-trigger tests and a rollback-keeps-stamps test.
  2. Pure DI extract to `AddAppMatchRecompute()`.
  3. Profile writes: add the stamp to the existing statement (atomic) and move their trigger into `ProfileService`.
  4. Watched add/update/delete in a strategy transaction with the stamp.
  5. Watchlist add/delete, then mark-watched (single stamp).
  6. Remove the endpoint call sites and `ToTriggeringResult`.
  - Each step is one green, revertible commit.
- **First prerequisite:** the 3 missing no-trigger cases (watchlist-add 409, mark-watched 400, mark-watched 409/rollback) and the rollback-keeps-stamps assertion. Optionally, make the CI jobs required on `main` first; that is a settings change.

### 2. One injected clock (C5)

- **Current → target:** a static `Clock` at 8 sites, beside a DI `TimeProvider` used only by the queue, becomes `TimeProvider` everywhere (µs truncation kept in one helper). Optionally, the two pending stamps are taken from the DB clock.
- **Why second**
  - It is the one candidate classified **accidental** (b7e11e4 de-duplication fix, F5).
  - It is the enabler for testing the guarantees ranked #1 depends on: forward-only `ComputedAt`, and a write during a running recompute, both untested (§F).
  - The DB-clock variant also removes the cross-machine skew risk (debt 5), which matters because production runs 2 machines.
- **Change cost:** medium, but mechanical and per-file revertible. No schema change.
- **Blast radius:** 6 service files plus DI. Live debounce tests must keep real time.
- **Incremental path**
  1. Move the `TimeProvider` registration app-wide (no-op).
  2. Add a helper with truncation.
  3. Migrate one file per commit.
  4. Delete `Clock`.
  5. Optional: switch the pending stamps to the DB clock.
- **First prerequisite:** the registration move. Also check Npgsql translation of a DB clock in `SetProperty` [U] before choosing the variant.

### 3. Startup recovery sweep with one pending predicate (C3, which also covers most of C4)

- **Current → target:** self-heal only via `/status` for the caller becomes a one-shot startup step that enqueues every DB-pending user, using one queryable pending predicate shared with `GetStatusAsync`.
- **Why third**
  - Today every deploy or restart leaves pending users stale until they open `/matches`.
  - It gives failed recomputes (C4) an eventual retry without new worker state.
  - Small and safe: migrate mode cannot run it, and double runs on 2 machines are idempotent.
  - Ranked below #1 and #2 because the loss is recoverable by design, and the risk was knowingly accepted (`plan-brief.md:52`). The owner question at `roadmap.md:171` is still unanswered.
- **Change cost:** small to medium. No schema change (an index is optional and additive).
- **Blast radius:** the C2 registration module, `MatchService.GetStatusAsync`, the test fixtures (the no-worker fixture would also run the sweep, which is harmless).
- **Constraint:** it must be framed as recovery, not as a scheduled batch (`prd.md:97`).
- **Incremental path**
  1. Extract the predicate.
  2. Add the hosted step.
  3. Add an integration test that boots with a pending user.
- **First prerequisite:** extract the pending predicate, pinned by the existing status tests.

**Cheap guards worth adding regardless of the ranking**
- C7 model-vs-SQL column guard test (S).
- C6 single-direction characterization test (S; feeds test-plan R2).
- Making the 3 CI jobs required checks on `main` (a setting).

### Considered and not ranked

- **C4 retry/backoff in the worker:** deliberate design (`plan.md:53`). Needs a new seam and new per-user state. #3 gives most of the benefit through the existing queue. Revisit only if failures show up in logs.
- **C6 gate direction / shared "are matched" helper:** domain at root (see C6). Goes to the DDD notes (M4L5); only the characterization test is in scope here.
- **C7 replacing the raw-SQL upsert:** deliberate for concurrency correctness. Replacing it costs a dependency or loses ordered locking. Guard it instead.
- **C8 generated TS client:** Path A is large (every endpoint retyped plus new tooling), and there is no observed drift or contract churn after delivery. Path B guard tests are possible later.

## Open questions (for planning)

- [U] Npgsql translation of a DB clock inside `SetProperty` (decides the C5 variant).
- [U] Owner's answer to `roadmap.md:171` (is "heal on next write or view" acceptable?). This decides the urgency of #3.
- [U] Whether to make CI jobs required on `main` before any refactor. This is a repository-setting decision for the owner.
- [U] Whether a match is a symmetric pair or a per-user list (C6), for the DDD notes.

## Claim verification (ast-grep)

Structural claims the ranking rests on were checked with ast-grep 0.51.1 (`ast-grep run -p` / `ast-grep scan --inline-rules`) on code at `8ccd119`. Every zero was cross-checked with plain `grep`. Wrong counts were corrected in place in the format "N (report: M)". The **Refactor opportunities (ranked)** section and the intentionality verdicts were not changed.

| Claim | Verdict | Evidence (file:line) | Method (pattern / rule) |
|---|---|---|---|
| 7 trigger call sites, all in 3 endpoint files (C2, ranked #1) | confirmed | `WatchedMoviesEndpoints.cs:51,73,93`; `WatchlistEndpoints.cs:52,83,102`; `ProfileEndpoints.cs:68` | `$T.RequestAsync($$$A)` |
| The trigger is injected into 8 endpoint lambdas (C2) | **refined**: 9 (report: 8) parameters = 8 route lambdas + the `ToTriggeringResult` helper | `WatchedMoviesEndpoints.cs:37,58,79`; `WatchlistEndpoints.cs:41,69,90`; `ProfileEndpoints.cs:38,49` + helper `:64` | rule `kind: parameter, regex ^MatchRecomputeTrigger\s` |
| Write shapes: 3 SaveChanges writes, 2 ExecuteDelete writes (+1 inside mark-watched), 2 profile ExecuteUpdate writes; mark-watched is the only write with an explicit transaction (C1, ranked #1) | confirmed | SaveChanges `WatchedMoviesService.cs:84,118`, `WatchlistService.cs:65`; ExecuteDelete `WatchedMoviesService.cs:125`, `WatchlistService.cs:84,143`; ExecuteUpdate `ProfileService.cs:64,90` (+ trigger `:19`, recompute `MatchService.cs:281`); BeginTransaction `WatchlistService.cs:123` (+ recompute `MatchService.cs:176`) | `db.SaveChangesAsync($$$A)`, `$X.ExecuteDeleteAsync($$$A)`, `$X.ExecuteUpdateAsync($$$A)`, `$X.BeginTransactionAsync($$$A)` |
| The strategy + transaction pattern exists twice and is reusable (C1) | confirmed | `WatchlistService.cs:113`, `MatchService.cs:150` | `$X.CreateExecutionStrategy()` |
| 3 write services each have their own Outcome enum (C2) | confirmed | `WatchedMoviesService.cs:8`, `WatchlistService.cs:9`, `ProfileService.cs:8` | rule `kind: enum_declaration, regex Outcome` |
| 8 `Clock.Now()` sites in 6 `Services/` files (C5, ranked #2) | confirmed | `MatchRecomputeTrigger.cs:21`, `WatchlistService.cs:60`, `ConversationService.cs:101`, `FriendsService.cs:62`, `WatchedMoviesService.cs:71,117`, `MatchService.cs:180,311` | `Clock.Now()` |
| `TimeProvider` registered once, inside `AddAppMatches` (C5) | confirmed | `MatchesEndpoints.cs:28` | `$S.TryAddSingleton(TimeProvider.System)` |
| The only hosted service is the worker, so there is no sweep (C3, ranked #3) | confirmed | `MatchesEndpoints.cs:31` | `$S.AddHostedService<$T>()` |
| No index on the stamps; `AspNetUsers` indexes only `CityId` (C3) | confirmed | 6 `HasIndex` in `AppDbContext.cs` (`:36,48,56,113,134,155`), user index only `:48`; `IX_Matches_CandidateId` at `:113` | `$E.HasIndex($$$A)` |
| 4 `db.Matches` reads, all caller-side (C6) | confirmed | `ConversationService.cs:39`, `FriendsService.cs:102`, `MatchService.cs:56,204` | rule `member_access_expression, regex ^db\.Matches$` |
| `Match` has 7 properties = 7 SQL columns (C7) | confirmed | `Data/Match.cs:9,11,13,15,17,19,21`; SQL column list `MatchService.cs:315` | rule `kind: property_declaration` |
| 16 `Results.Ok`, 0 `TypedResults` / `.Produces<T>` (C8) | confirmed; the zero was cross-checked with grep (`TypedResults\|\.Produces<` over `Endpoints/`: 0) | 16 sites across 10 endpoint files (e.g. `MatchesEndpoints.cs:48,74,90`) | `Results.Ok($$$A)`; rule `identifier ^TypedResults$` |
| 19 contract records (C8) | confirmed | `Contracts/*.cs` (19 `record` declarations) | rule `kind: record_declaration` |
| TS twins hand-written in 7 service files (C8) | **refined**: 6 (report: 7) service files with 12 contract interfaces; `shared/api-errors.ts:18` `ErrorMessages` is a UI type | `movies.service.ts:6,13`, `watchlist.service.ts:7`, `matches.service.ts:9,27`, `friends.service.ts:8,14,21,30`, `profile.service.ts:7,13`, `auth.service.ts:5` | rule `interface_declaration inside export_statement` (TS) |

**Effect on the ranking:** none of the corrections weakens a ranked candidate. For the planning stage, note that the 9th trigger parameter (the `ToTriggeringResult` helper) is one more site to remove in ranked #1, step 6. **To decide at the planning stage.** No claim was refuted.
