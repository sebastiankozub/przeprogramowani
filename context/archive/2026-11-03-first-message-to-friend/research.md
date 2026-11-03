---
date: 2026-11-03T15:36:13+01:00
researcher: Claude (Opus 5.5) with Seb Ko
git_commit: 2886322
branch: main
repository: przeprogramowani (MovieDate)
topic: "S-07 + S-08: add a match as a friend, then send and read messages between the pair"
tags: [research, friends, messages, authorization, ef-core, angular, e2e]
status: complete
last_updated: 2026-11-03
last_updated_by: Claude (Opus 5.5)
---

# Research: S-07 + S-08 — friends and first message

**Date**: 2026-11-03T15:36:13+01:00
**Researcher**: Claude (Opus 5.5) with Seb Ko
**Git Commit**: 2886322
**Branch**: main
**Repository**: przeprogramowani (MovieDate)

## Research Question

This change folds roadmap slices S-07 (add-match-as-friend) and S-08 (first-message-to-friend) into one:
- a user adds a candidate from their own match list as a friend (one-sided);
- the user sends that friend a message and reads the stored conversation;
- nobody outside the pair can read it.

The research covers:
- what existing code this plugs into;
- what the authorization gate must check;
- how to model friendships and messages in this codebase;
- how to cover the privacy guardrail with integration and e2e tests in CI.

External grounding comes from Context7 (ASP.NET Core and EF Core docs) and Exa (messaging schema patterns).

## Summary

- **The gate data already exists and is stable.**
  - Match rows are keyed `(UserId, CandidateId)` (`Data/AppDbContext.cs:95`).
  - The rows are written mirrored: A→X and X→A (`Services/MatchService.cs:291-297`).
  - They are append-only: inserted or re-scored, never deleted except by account-delete cascade (`Services/MatchService.cs:30-35`).
  - So "X is on my match list" is a single primary-key lookup. Once true, it stays true. By mirroring, it is then also true from X's side.
- **The SPA already has the candidate id.** `MatchResponse.UserId` is the candidate's AspNetUsers.Id (`Contracts/MatchResponse.cs:9`), so a "Dodaj do znajomych" button can post it directly.
- **Authorization style is settled in this repo: per-user scoped queries, with foreign resources returned as 404 and never 403** (`Endpoints/WatchlistEndpoints.cs:24-26`).
  - Microsoft docs explicitly allow 404 for unauthorized access so that resource existence does not leak (Context7, `/dotnet/aspnetcore.docs`, configure-jwt-bearer-authentication.md note).
  - The policy-based `IAuthorizationService.AuthorizeAsync(user, resource, policy)` pattern exists. It is not used anywhere in this repo.
  - Adopting it for a single check would add a convention break for little gain. The scoped-query style already gives the 404 semantics.
- **Recommended data shape:**
  - `Friendships(UserId, FriendId)`: directional, one-sided add, the same composite-PK + cascade-FK + not-self check pattern as `Matches`.
  - `Messages(Id, SenderId, RecipientId, Body, SentAt)`, with an index serving "conversation between A and B ordered by time".
  - A separate `Conversations` table is unnecessary for 1:1 store-and-retrieve messaging with no read receipts. The pair *is* the conversation.
  - The Exa-sourced schemas use a conversation table mainly for groups, last-message previews and read state, which are all PRD non-goals or not required.
- **One product decision is open and blocks the plan** (Open Questions #1): what the *recipient* of a first message can do when they have not friended the sender.
- **Tests:**
  - Integration tests follow the existing "other user can't see" pattern with two signed-in clients (`tests/MovieDate.Tests/WatchlistTests.cs:128`).
  - The e2e spec can reuse the two-user pattern from `ClientApp/e2e/critical-path.spec.ts:18-25`, keeping the partner's API context alive.
  - It then runs in the existing CI `e2e` job automatically (`.github/workflows/ci.yml:54-91`, `npm run e2e` picks up every spec in `e2e/`).

## Detailed Findings

### Match list: the gate for friending

- **Entity:** `Data/Match.cs:7-22`, table `"Matches"`. Columns: `UserId`, `CandidateId`, `Score`, `SharedFavorites`, `SharedWatchlist`, `FirstMatchedAt`, `UpdatedAt`.
- **Model config** (`Data/AppDbContext.cs:93-112`):
  - The PK is `(UserId, CandidateId)`.
  - Two FKs to `AppUser` with `DeleteBehavior.Cascade`.
  - Index on `CandidateId`.
  - Check constraint `CK_Matches_NotSelf`.
- **Writes:** only `MatchService.UpsertMirroredAsync` (`Services/MatchService.cs:288-320`), which writes both directions per candidate (`:291-297`) with `ON CONFLICT DO UPDATE` and keeps `FirstMatchedAt`.
  - The class doc states that rows are only inserted or re-scored, never deleted (`Services/MatchService.cs:30-35`).
  - Within the inspected service there is no delete path.
- **Read:** `MatchService.ListAsync` (`Services/MatchService.cs:52-99`).
  - Joins to users that have a city and a display name.
  - Orders by Score desc, then FirstMatchedAt, then CandidateId.
  - Live title lists are read at `:322-356`.
- **No "is X on my list" helper exists.** The natural check is `db.Matches.AnyAsync(m => m.UserId == me && m.CandidateId == x)`, served by the PK.
- **Consequence of mirroring:**
  - If A may friend B (B is on A's list), then A is on B's list.
  - Whatever the recipient rule (Open Question #1), the "matched" half of the PRD guardrail ("Messages are visible only between two users who have matched and been added as friends", `context/foundation/prd.md:38`) holds symmetrically for the pair.

### Current-user identification and endpoint conventions

- **Current user:** every endpoint resolves it with `users.GetUserId(user)` and returns `Results.Unauthorized()` on null (`Endpoints/MatchesEndpoints.cs:45-48`). This follows `context/foundation/lessons.md` ("Key per-user data on the user id").
- **Group-level auth:** `app.MapGroup(RoutePrefix).RequireAuthorization()` (`Endpoints/MatchesEndpoints.cs:39`), with a `RoutePrefix` const per class.
- **Services return an outcome enum plus a result record**, e.g. `MatchOutcome` (`Services/MatchService.cs:8-25`). Endpoints switch on the outcome:
  - 404 → `Results.NotFound()`.
  - 409 → `Results.Problem(statusCode: 409, title: ...)` (`Endpoints/MatchesEndpoints.cs:91-94`).
  - 400 → `Results.ValidationProblem` (worker anchor: `Endpoints/WatchlistEndpoints.cs:55-64`).
  - 201 → `Results.Json(value, statusCode: 201)` (worker anchor: `Endpoints/WatchlistEndpoints.cs:58`).
- **Wiring:** each feature has `AddAppXxx(IServiceCollection)` and `MapXxxEndpoints(IEndpointRouteBuilder)`, called from `Program.cs` (worker anchors `Program.cs:12-15`, `:38-45`). `/api` maps go above `MapFallbackToFile` (`Program.cs:66-67`).
- **Contracts:** one positional record per file in `Contracts/`, named `<Thing>Response` / `<Verb><Thing>Request`. Privacy notes go in XML docs (`Contracts/MatchResponse.cs:3-7`: city-level data only, never email).
- **Own id:** `/api/auth/me` returns only the email (`Contracts/MeResponse.cs`: `record MeResponse(string Email)`). The API never exposes the caller's own id.
  - Tests that need ids resolve them from the DB by email (worker anchor `tests/MovieDate.Tests/MatchesTests.cs:289-310`).
  - The browser flow gets the friend's id from `MatchResponse.UserId`, so it never needs its own id.

### Data model and migrations

- **`AppDbContext`** is `IdentityDbContext<AppUser>`. DbSets use the form `DbSet<T> X => Set<T>()`.
- **Conventions:**
  - Tables are named after the DbSet.
  - Per-user tables use a composite PK, `HasOne<AppUser>().WithMany().HasForeignKey(...).OnDelete(Cascade)`, and no navigation on AppUser.
  - Check constraints are named `CK_<Table>_<Name>` (`Data/AppDbContext.cs:93-112`).
- **Latest migration:** `Data/Migrations/20261103085214_AddMatchRecomputeStamps.cs`.
- **New migration:** `dotnet ef migrations add <Name> -o Data/Migrations` (as in archived plans, e.g. `context/archive/2026-11-03-ranked-match-list/plan.md:91`).
- **Migrations must be additive**, because they run before rollout through Fly's `release_command` (`context/foundation/lessons.md`, "Keep migrations backward-compatible"). Two new tables satisfy this.
- **Display name and city** live on `AppUser` (`Data/AppUser.cs:6-26`: `DisplayName` max 40, `CityId`). Anyone on a match list has both (`Services/MatchService.cs:54-55` comment, per worker).

### External grounding (Context7 + Exa, checked 2026-11-03)

- **ASP.NET Core, resource-based authorization** (Context7 `/dotnet/aspnetcore.docs`):
  - `IAuthorizationService.AuthorizeAsync(ClaimsPrincipal, object resource, string policyName)` with an `AuthorizationHandler<TRequirement, TResource>` is the documented pattern.
  - The docs return `Forbid`/`Challenge` on failure. They also note that HTTP allows 404 for unauthorized access, to avoid leaking a resource's existence.
  - Exa also returned the ASP.NET Core 10 minimal-API security page (learn.microsoft.com, published 2026-08-19), which covers policies via `AddAuthorizationBuilder().AddPolicy(...)` and `.RequireAuthorization("policy")`.
  - **Fit here:** the "pair may talk" check needs DB state (friendship + match), so it would be an imperative handler call per request. The repo's scoped-query + 404 style already gives the stronger non-disclosure semantics, with no new mechanism.
- **EF Core pagination** (Context7 `/dotnet/entityframework.docs`, querying/pagination.md):
  - Keyset pagination must order on a fully unique key (e.g. date + id).
  - It needs a matching composite index.
  - EF Core cannot express row-value comparisons `(Date, Id) > (...)` in LINQ (issue #26822). Two-clause predicates are needed.
  - For MVP-sized conversations, returning the latest N (e.g. 100) ordered by `(SentAt, Id)` is enough. Keyset paging can be added later without schema change if the index is `(…pair…, SentAt, Id)`.
- **1:1 messaging schemas** (Exa: NENE2 how-to `hideyukimori.github.io/NENE2/howto/direct-messaging-system`, sweatyimposter chat schema, Convayto DATABASE_DESIGN.md):
  - All three use a conversations table plus a messages table. The extras it carries (`last_message`, member read state, group kind) are features this PRD does not require. PRD §Non-Goals: no realtime (`context/foundation/prd.md:98`). FR-007 is store-and-retrieve (`prd.md:68`).
  - The anti-patterns they list do apply here:
    - a missing not-self check;
    - duplicate conversations per direction, which is avoided if the pair query is direction-agnostic (`(Sender=A ∧ Recipient=B) ∨ (Sender=B ∧ Recipient=A)`);
    - validating the body before the participant check, which leaks existence through 400 vs 404 — check access first;
    - an index on the pair alone without the ordering column.
  - **Index choice without a conversations table:** a direction-agnostic query over `(SenderId, RecipientId, SentAt)` needs either two index seeks (an OR of two pairs, one index used twice) or a canonical pair key. A canonical pair key means `UserLowId = least(A,B)` and `UserHighId = greatest(A,B)` stored on each message, with an index `(UserLowId, UserHighId, SentAt, Id)`. The canonical-pair approach appears in the Habr example (`user_low`/`user_high` via LEAST/GREATEST).

### SPA patterns to copy

- **Routes:** `ClientApp/src/app/app.routes.ts:23-28` uses lazy `loadComponent` + `canActivate: [authGuard]` + `title`. New routes go before the `'**'` redirect (`:35`).
  - No route takes a parameter yet. `app.config.ts` uses `provideRouter(routes)` without `withComponentInputBinding()`.
  - A `friends/:userId` conversation route therefore needs either that feature added to the router config or `ActivatedRoute`.
- **Nav:** `ClientApp/src/app/app.html:4-25`, where `<a mat-button routerLink routerLinkActive ariaCurrentWhenActive="page">` links show only when signed in.
- **Matches page:**
  - Component: `ClientApp/src/app/matches/matches.component.ts` (signals, `firstValueFrom`, polling at `:86-130`).
  - Template: `matches.component.html:35-68` renders `<ol>` / `<li class="movies-item">` rows ending in the score.
  - The `Match` model is at `matches.service.ts:9-22`. It has no friend flag.
  - **Breakage to expect:** `matches.component.spec.ts:72-77` asserts the page has *no* `button`. Adding "Dodaj do znajomych" breaks it, so narrow it to "no search button".
- **Service pattern:**
  - One root-provided service per feature, holding a base-URL const and `mapErrors({...})` (`ClientApp/src/app/shared/api-errors.ts:18-32`).
  - Polish messages are exported as consts.
  - Errors appear inline through `<p class="auth-error" role="alert">`. There is no snackbar.
- **Form + list model:** `ClientApp/src/app/watchlist/watchlist.component.ts:26-70` with `watchlist.component.html:4-59`. The form uses `(submit)`, `mat-flat-button type="submit"` and an `<ul class="movies-list">`.
- **Unit tests:**
  - Component specs stub the service with `of(...)` / `throwError` (`matches.component.spec.ts:37-55`).
  - Service specs use `provideHttpClientTesting` + `HttpTestingController.expectOne` (`matches.service.spec.ts:25-49`).

### Tests: integration and e2e

- **Integration:**
  - `PostgresAppFixture` uses Testcontainers postgres:17 with a shared DB per collection, so tests must use unique users (`tests/MovieDate.Tests/PostgresAppFixture.cs:16-62`).
  - `TestUsers.NewSignedInClient` gives one cookie client per user (`tests/MovieDate.Tests/TestUsers.cs:26-38`).
  - Existing privacy tests to mirror: `WatchlistTests.Other_user_cannot_see_remove_or_mark_watched_entry` (`WatchlistTests.cs:128`), `ProfileTests.Other_user_does_not_see_my_display_name` (`:123`).
  - Building a matched pair needs the private `MatchesTests.NewUser` + recompute helpers (`MatchesTests.cs:289-310`, `:365-380`). Extract them into a shared helper rather than duplicating them.
  - For deterministic matching, use `PostgresAppWithoutRecomputeWorkerFixture` (`PostgresAppFixture.cs:70-89`) plus explicit `POST /api/matches/recompute`.
- **E2E:**
  - `ClientApp/e2e/critical-path.spec.ts:18-25` creates a partner through a separate `apiRequest.newContext`.
  - For messaging, keep that context alive. The partner then reads the conversation and replies through the API, and the browser user sees the reply after reload (no realtime).
  - The partner's API calls need the browser user's id. The partner can get it from its own `GET /api/matches` (mirrored row) or from the friend/conversation list, depending on Open Question #1.
  - `playwright.config.ts` runs `workers: 1` with a 5 s quiet period and 15 s max delay on recompute. A match appears within 30 s (`critical-path.spec.ts:69`).
  - CI: the `e2e` job runs `npm run e2e` over all of `e2e/` (`.github/workflows/ci.yml:86-91`), so a new spec is gated with no workflow change.

## Code References

- `Data/AppDbContext.cs:93-112` — Match entity config (composite PK, cascade FKs, CandidateId index, not-self check): the template for Friendships.
- `Data/Match.cs:7-22` — Match entity.
- `Services/MatchService.cs:30-35` — append-only, mirrored contract.
- `Services/MatchService.cs:52-99` — `ListAsync`, the current user's list read.
- `Services/MatchService.cs:288-320` — mirrored upsert.
- `Services/MatchService.cs:8-25` — outcome enum + result record pattern.
- `Endpoints/MatchesEndpoints.cs:39-50` — group auth + `GetUserId` pattern.
- `Endpoints/WatchlistEndpoints.cs:24-26` — "foreign entries surface as 404" rule.
- `Contracts/MatchResponse.cs:8-17` — `UserId` of the candidate, privacy doc.
- `Contracts/MeResponse.cs` — `/me` returns email only.
- `ClientApp/src/app/app.routes.ts:23-35` — lazy guarded routes.
- `ClientApp/src/app/app.config.ts` — `provideRouter(routes)` without component input binding.
- `ClientApp/src/app/matches/matches.component.html:35-68` — match row markup.
- `ClientApp/src/app/matches/matches.component.spec.ts:72-77` — "no button" assertion that will break.
- `ClientApp/src/app/shared/api-errors.ts:18-32` — `mapErrors`.
- `ClientApp/e2e/critical-path.spec.ts:18-25,68-71` — two-user e2e pattern, match row locator.
- `tests/MovieDate.Tests/WatchlistTests.cs:128` — "other user cannot see" test template.
- `tests/MovieDate.Tests/MatchesTests.cs:289-310,365-380` — matched-user helpers (private today).
- `.github/workflows/ci.yml:54-91` — e2e CI job.

## Architecture Insights

- **Privacy by query scope, not by policy objects.** Every per-user read filters on the caller's id, and anything else is "not found". Messages fit this as follows:
  - a read of the conversation with X is allowed only if (gate holds for caller and X); otherwise 404;
  - the messages query itself is filtered by the pair;
  - so there is no resource id a stranger could guess.
  - Using `/api/conversations/{friendUserId}` (the other user's id) rather than a conversation id avoids an IDOR surface entirely.
- **The gate is a composition of two facts, and both are cheap PK lookups:**
  - "X is on my match list": `Matches` PK, stable because append-only;
  - "a friendship exists": `Friendships` PK.
- **The domain chain** Matching → Friendship → Message is a sequence of invariants. Each step checks the previous one at write time. This is worth naming explicitly for the later domain distillation.
- **Testing layers (cost × signal):**
  - The guardrail (stranger, non-matched or non-friend → 404 on read and send) belongs in integration tests. That layer is cheap and deterministic.
  - The e2e covers only the one browser journey: match → add friend → send → partner replies via API → reply visible.

## Historical Context (from prior changes)

- `context/foundation/roadmap.md:175-197`:
  - S-07: one-sided friending, gate = candidate on own match list. Its stated risk is "the gate the messaging guardrail relies on".
  - S-08: send and read history, "nobody outside that pair can read it", per-conversation authorization "must be tested explicitly".
  - Both are `proposed`.
- `context/foundation/roadmap.md` Parked:
  - mutual acceptance is parked (FR-006);
  - realtime chat is parked (PRD non-goal).
- `context/foundation/prd.md:52`: "Messaging is possible with anyone on the user's matched list — MVP does not require mutual friend acceptance". `prd.md:66-67` (FR-006 Socratic note) resolves the one-sided-friending vs guardrail tension the same way.
- `context/foundation/test-plan.md`:
  - risk #2 (data leak / IDOR) is mapped to integration tests;
  - §3 Phase 3 (privacy + ranking contract) is not started;
  - this change's guardrail tests partly serve it.
- `context/archive/2026-11-03-ranked-match-list/` — origin of the mirrored, append-only match list and of `MatchResponse.UserId`.

## Related Research

- `context/archive/2026-11-03-ranked-match-list/research.md` — match storage design.
- `context/changes/testing-critical-path-e2e/research.md` — e2e two-user pattern and recompute timing.

## Open Questions

1. **The recipient's side (blocks the plan).** A friends B one-sided and sends a message. B has A on their match list (mirrored) but has not friended A. Options:
   - **(a) Either side's friendship opens the pair.** If A friended B *or* B friended A, both may read and send. B finds the conversation through an "incoming" section on the friends page. This matches PRD US-01 ("exchange at least one message") and `prd.md:52`.
   - **(b) Read-only for B until B friends A back.** The friends list shows "X wrote to you, add as friend to reply".
   - **(c) Only the friender can see anything.** B never learns of the message. This contradicts "exchange at least one message" (`prd.md:31`).

   (a) is the reading most consistent with the PRD. It is a product decision — Owner: user.
2. **Unfriend.** Is removing a friend in scope? The PRD does not mention it. If it is added under (a), the messages remain stored but become unreadable while neither direction holds. Suggest out of scope for MVP.
3. **Message limits.** Max body length (suggest 2000 chars, trimmed, non-empty) and history size returned (suggest the latest 100, ascending). No PRD source exists; this is a plan-level default.
4. **Friend state on /matches.** Should `MatchResponse` gain an `IsFriend` flag (one extra join), or should the SPA call `GET /api/friends` and cross-reference? The flag is simpler for the button state. The contract change is additive.
