# Friends and first message (S-07 + S-08) Implementation Plan

## Overview

A user adds a candidate from their own match list as a friend (one-sided), then sends that person a message and reads the stored conversation. Either side's friendship opens the pair, so the recipient can read and reply. Nobody outside a matched pair with a friendship can read or write it.

This closes milestone M-01's north star: "exchange at least one message" (`context/foundation/prd.md:31`). It protects the PRD guardrail "messages visible only between matched friends" (`prd.md:38`) with integration tests and one e2e journey in CI.

## Current State Analysis

- **Matches are the gate.**
  - Table `Matches` has PK `(UserId, CandidateId)`, cascade FKs to AspNetUsers and a not-self check (`Data/AppDbContext.cs:93-112`).
  - Rows are written in both directions (`Services/MatchService.cs:291-297`) and never deleted (`Services/MatchService.cs:30-35`).
  - So "X is on my list" is one stable PK lookup, and it is symmetric.
- **The SPA already has the candidate's id:** `MatchResponse.UserId` (`Contracts/MatchResponse.cs:9`). The API never exposes the caller's own id (`Contracts/MeResponse.cs`).
- **Conventions:**
  - Per-feature `Endpoints/XxxEndpoints.cs` with `AddAppXxx` + `MapXxxEndpoints`, wired at `Program.cs:12-15,38-45`.
  - Group-level `RequireAuthorization()`.
  - The user comes from `users.GetUserId(user)`.
  - Services return outcome enum + result.
  - Foreign resources return 404, never 403 (`Endpoints/WatchlistEndpoints.cs:24-26`).
  - Contracts are one record per file.
- **SPA:**
  - Lazy guarded routes (`ClientApp/src/app/app.routes.ts:23-35`).
  - `provideRouter(routes)` without component input binding (`ClientApp/src/app/app.config.ts`).
  - Feature service + `mapErrors` (`ClientApp/src/app/shared/api-errors.ts:18-32`).
  - The matches row markup is at `matches.component.html:35-68`.
  - The spec at `matches.component.spec.ts:72-77` asserts the page has no button at all.
- **Migrations must be additive** (`context/foundation/lessons.md`). Latest: `20261103085214_AddMatchRecomputeStamps`.
- **CI:** `.github/workflows/ci.yml` runs backend tests, SPA tests + build, and every spec in `ClientApp/e2e/` against postgres:17. `impl-review.yml` reviews PRs against this plan.

## Desired End State

- **On /matches:** each candidate row shows a "Dodaj do znajomych" button, or a "Napisz" link once that user is a friend.
- **/friends** lists:
  - "Znajomi": people I added;
  - "Dodali Cię do znajomych": people who added me and whom I have not added.
  - Every entry links to the conversation.
- **/friends/:userId** shows:
  - the other person's name;
  - the latest 100 messages, oldest first, each marked as mine or theirs;
  - a "Wiadomość" field with a "Wyślij" button.
- A stranger, an unmatched user, or a matched user with no friendship in either direction gets 404 on both reading and sending.
- Verified by:
  - the integration tests in Phase 1;
  - the SPA specs in Phase 2;
  - `ClientApp/e2e/messaging.spec.ts` green in CI;
  - a manual pass on production.

### Key Discoveries:

- **Pair gate** = `Matches(me, other)` exists AND (`Friendships(me, other)` OR `Friendships(other, me)`).
  - Mirroring makes the match half symmetric.
  - Append-only matches make it permanent (`Services/MatchService.cs:30-35`).
- **Gate before validation** on POST. Body validation (400) only runs after the gate passes, so a stranger cannot tell an existing pair from a missing one by comparing 400 with 404 (research, Exa anti-pattern list).
- **Address conversations by the other user's id** (`/api/conversations/{userId}`). There is no conversation id to guess, and responses carry `FromMe`, so the caller's own id is never exposed.
- **EF Core** cannot express row-value keyset predicates (Context7, EF docs, issue #26822). Order on `(SentAt, Id)` so the order is fully unique, and index it.
- **Top-up can match strangers.** The matching top-up fills lists to 5 with the closest profile-complete users (S-05). A "stranger" in tests stays unmatched only if nothing recomputes. Use `PostgresAppWithoutRecomputeWorkerFixture` (`tests/MovieDate.Tests/PostgresAppFixture.cs:70-89`) and seed match rows directly.

## What We're NOT Doing

- Unfriend or any delete path for friendships or messages.
- Mutual friend acceptance (PRD FR-006, parked).
- Realtime delivery, push, unread counts or read receipts (PRD non-goals).
- Paging past the latest 100 messages. The index supports adding keyset paging later without a schema change.
- A `Conversations` table, group chats, message editing.
- Changes to matching or scoring, or to `deploy.yml`.

## Implementation Approach

- **Order:** data model → services with the gate → endpoints → SPA → e2e.
- **One gate method** is used by every conversation operation:
  - `FriendsService` owns adding and listing friends.
  - `ConversationService` owns reading and sending, and calls the gate first.
- **Tests:**
  - The guardrail is proven at the cheapest real layer: integration tests against Testcontainers Postgres.
  - The single e2e spec proves the browser journey only.
- **Delivery:**
  - Work on branch `first-message-to-friend`, one commit per phase.
  - Open a PR to main so CI and the agent review run.
  - Merge, then deploy with the manual `deploy.yml` (`flyctl deploy --remote-only --app moviedate-sk`).

## Critical Implementation Details

- **Test isolation.** Friend and conversation integration tests must use `PostgresAppWithoutRecomputeWorkerFixture`. Matched pairs are created by inserting both mirrored `Match` rows through `AppDbContext`, not through recompute. With the worker on, a profile write can trigger a recompute whose top-up silently matches the "stranger" user and turns a 404 assertion into 200.
- **Per-phase CI.** Each phase must leave `matches.component.spec.ts` green. When Phase 2 adds the button, narrow the "has no search button" assertion (`:72-77`) to the "Szukaj dopasowań" button instead of deleting it.

## Phase 1: Backend — friends and conversations

### Overview

Two new tables, the pair gate, five endpoints, `IsFriend` on matches, and integration tests that pin the guardrail.

### Changes Required:

#### 1. Entities and model config

**File**: `Data/Friendship.cs`, `Data/Message.cs` (new), `Data/AppDbContext.cs`

**Intent**: Store one-sided friendships and pair messages, following the `Match` config pattern (composite PK, cascade FKs to `AppUser`, not-self check).

**Contract**:
- `Friendship(UserId, FriendId, CreatedAt)`:
  - PK `(UserId, FriendId)`;
  - index on `FriendId` (for "who added me");
  - FKs to AspNetUsers with Cascade;
  - check `CK_Friendships_NotSelf`.
- `Message(Id long identity, SenderId, RecipientId, Body string max 2000, SentAt DateTimeOffset)`:
  - FKs to AspNetUsers with Cascade;
  - check `CK_Messages_NotSelf`;
  - index `(SenderId, RecipientId, SentAt, Id)`.
  - Expose `Message.BodyMaxLength = 2000` as a const, like `AppUser.DisplayNameMaxLength`.
- DbSets `Friendships`, `Messages`.

#### 2. Migration

**File**: `Data/Migrations/<timestamp>_AddFriendsAndMessages.cs` (generated)

**Intent**: Create the two tables. The migration is additive only, so it is safe with the previous release running (lessons.md).

**Contract**: `dotnet ef migrations add AddFriendsAndMessages -o Data/Migrations`. The generated migration creates exactly two tables plus their indexes and checks, and nothing else.

#### 3. Friends service and endpoints

**File**: `Services/FriendsService.cs`, `Endpoints/FriendsEndpoints.cs` (new), `Contracts/AddFriendRequest.cs`, `Contracts/FriendResponse.cs`, `Contracts/FriendsResponse.cs`, `Program.cs`

**Intent**: Let a user add someone from their own match list and list both directions.

**Contract**:
- `POST /api/friends` `{ userId }`:
  - 201 `FriendResponse` on success;
  - 404 when `userId` is not on the caller's match list (this covers unknown ids and self);
  - 409 problem `already_friend` when the friendship already exists.
- `GET /api/friends` → `FriendsResponse(IReadOnlyList<FriendResponse> Friends, IReadOnlyList<FriendResponse> AddedMe)`.
  - `AddedMe` contains people who friended the caller and whom the caller has not friended.
  - Both lists are ordered by display name.
- `FriendResponse(string UserId, string DisplayName, CityResponse City)`. City-level data only, never the email (same privacy doc as `MatchResponse`).
- Wire `AddAppFriends` + `MapFriendsEndpoints` in `Program.cs`, above `MapFallbackToFile`.

#### 4. Conversation service and endpoints

**File**: `Services/ConversationService.cs`, `Endpoints/ConversationsEndpoints.cs` (new), `Contracts/SendMessageRequest.cs`, `Contracts/MessageResponse.cs`, `Contracts/ConversationResponse.cs`, `Program.cs`

**Intent**: Read and send messages for a pair that passes the gate. Any other caller sees 404.

**Contract**:
- Gate `CanTalkAsync(me, other)`: a `Matches(me, other)` row exists AND a Friendship exists in either direction.
- `GET /api/conversations/{userId}`:
  - gate fails → 404;
  - otherwise `ConversationResponse(FriendResponse With, IReadOnlyList<MessageResponse> Messages)` with the latest 100 messages of the pair (both directions), returned oldest first.
- `MessageResponse(long Id, bool FromMe, string Body, DateTimeOffset SentAt)`.
- `POST /api/conversations/{userId}/messages` `{ body }`:
  - gate first → 404;
  - then the trimmed body must be 1–2000 chars, else a 400 ValidationProblem on `body`;
  - success → 201 `MessageResponse`.
- `SentAt` comes from the existing `Clock`/`TimeProvider` service.

#### 5. Friend flag on matches

**File**: `Contracts/MatchResponse.cs`, `Services/MatchService.cs`

**Intent**: Let /matches render the right button without a second request.

**Contract**: An additive `bool IsFriend` on `MatchResponse`. It is true when the caller has friended that candidate (the caller's own direction only). It is set in `ListAsync` with one lookup over the caller's friendships.

#### 6. Integration tests

**File**: `tests/MovieDate.Tests/FriendsTests.cs`, `tests/MovieDate.Tests/ConversationsTests.cs` (new), shared helper (e.g. `tests/MovieDate.Tests/TestPairs.cs`)

**Intent**: Pin the gate and the guardrail at the integration layer.

**Contract**:
- Collection: `PostgresAppWithoutRecomputeWorkerCollection`.
- The helper creates signed-in users with display name + city through the API, resolves their ids (same approach as `MatchesTests.NewUser`, `MatchesTests.cs:289-310`), and seeds mirrored `Match` rows via `AppDbContext`.
- Named tests:
  - `Add_friend_from_own_match_list_returns_201_and_lists_both_directions`
  - `Add_friend_not_on_match_list_returns_404`
  - `Add_friend_twice_returns_409`
  - `Matches_list_marks_friend_with_IsFriend`
  - `Friend_pair_can_send_and_read_messages_oldest_first`
  - `Recipient_who_did_not_add_sender_can_read_and_reply`
  - `Stranger_cannot_read_or_send_to_pair_returns_404`
  - `Matched_without_friendship_cannot_read_or_send_returns_404`
  - `Empty_or_too_long_body_returns_400_for_pair_but_404_for_stranger`
  - `Conversation_returns_latest_100_messages`

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build movie-date.csproj`
- Migration adds only the two new tables (reviewed in the generated file)
- All backend tests pass, including the new friends and conversation tests: `dotnet test tests/MovieDate.Tests`

#### Manual Verification:

- Locally, with two accounts matched to each other, the friend and conversation endpoints behave as specified when called from the browser devtools or an HTTP client

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: SPA — add friend, friends page, conversation

### Overview

Three UI pieces plus a nav link, following the existing feature-service + signals + inline-alert patterns.

### Changes Required:

#### 1. Add-friend button on /matches

**File**: `ClientApp/src/app/matches/matches.service.ts`, `matches.component.ts`, `matches.component.html`, `matches.component.spec.ts`

**Intent**: Turn a match into a friend in one click, then offer the conversation.

**Contract**:
- `Match` gains `isFriend`.
- Each row shows either:
  - `<button mat-flat-button>Dodaj do znajomych</button>`, disabled while busy. On success it flips the row to friend.
  - a `Napisz` link to `/friends/:userId` when `isFriend`.
- Errors appear in the existing inline `role="alert"`.
- The spec's "no button" assertion is narrowed to "no Szukaj dopasowań button", and specs are added for the button and for the link state.

#### 2. Friends feature

**File**: `ClientApp/src/app/friends/friends.service.ts`, `friends.component.{ts,html}`, specs (new)

**Intent**: One service for `/api/friends` and `/api/conversations`, plus the friends list page.

**Contract**:
- Service methods: `list()`, `add(userId)`, `conversation(userId)`, `send(userId, body)`, each piped through `mapErrors`.
- Page title "Znajomi".
- Two lists:
  - "Znajomi";
  - "Dodali Cię do znajomych", rendered only when non-empty.
- Each entry shows name + city and a `Napisz` link.
- Empty state when both lists are empty, pointing to /matches.

#### 3. Conversation page

**File**: `ClientApp/src/app/friends/conversation.component.{ts,html}`, spec (new), `ClientApp/src/app/app.config.ts`

**Intent**: Read the stored history and send a message.

**Contract**:
- Route param `userId`, read through component input binding: add `withComponentInputBinding()` to `provideRouter`.
- Heading: the other person's display name.
- `<ol>` of messages, each labelled "Ty" or with the other person's name, plus the time.
- Form:
  - `Wiadomość` textarea with maxlength 2000;
  - `Wyślij` button, disabled when the trimmed text is empty or a send is in progress.
  - On success it appends the message and clears the field.
- A 404 shows an inline "Nie możesz pisać z tą osobą" alert instead of the form.

#### 4. Routes and nav

**File**: `ClientApp/src/app/app.routes.ts`, `ClientApp/src/app/app.html`

**Intent**: Expose the new pages to signed-in users only.

**Contract**:
- Lazy routes `friends` and `friends/:userId`, both with `canActivate: [authGuard]`, titles "Znajomi" and "Rozmowa", placed before `'**'`.
- Nav link "Znajomi" after "Dopasowania".

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `cd ClientApp; npx ng test --watch=false`
- Production build passes: `cd ClientApp; npm run build`
- Existing e2e suite still passes: `cd ClientApp; npm run e2e`

#### Manual Verification:

- Locally, in two browsers, user A adds B from /matches, sends a message, and B sees A under "Dodali Cię do znajomych", opens the conversation, and replies; A sees the reply after a reload

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 3.

---

## Phase 3: E2E journey, PR and production

### Overview

One browser spec for the full journey, gated in CI, then delivery through a PR and a production deploy.

### Changes Required:

#### 1. Messaging e2e spec

**File**: `ClientApp/e2e/messaging.spec.ts` (new)

**Intent**: Prove the north-star path in a real browser. The spec protects test-plan risk #4 (browser path) and the guardrail's happy path; strangers are covered in Phase 1.

**Contract**:
- Partner: created with `createPartner` in a separate `apiRequest.newContext`, kept alive for the whole test.
- Browser user: set up through `page.request`, which shares the browser cookie (register + profile + the same favorite movie via the existing helpers).
- Browser steps:
  - open /matches and wait for the partner row (≤ 30 s);
  - click "Dodaj do znajomych", then "Napisz";
  - send a unique message.
- Partner steps (API):
  - `GET /api/friends` finds the user under `addedMe`;
  - `GET /api/conversations/{id}` contains the message with `fromMe: false`;
  - `POST` a reply.
- Browser reloads and sees the reply.
- Role and label locators only. The header comment names the risk, like `critical-path.spec.ts`.

#### 2. Test plan cookbook note

**File**: `context/foundation/test-plan.md`

**Intent**: Record that risk #2 now has integration coverage for messages and that a second e2e journey exists.

**Contract**: One line each in §6 (e2e and integration recipes). The frozen §1–§5 stay unchanged.

#### 3. Delivery

**Intent**: Gate through CI and the agent review, then ship.

**Contract**:
- Push branch `first-message-to-friend` and open a PR to main.
- Merge after green CI and a non-REJECTED `impl-review-ci/verdict`.
- Run the `deploy.yml` workflow.
- Record the release in `context/deployment/deploy-plan.md`.

### Success Criteria:

#### Automated Verification:

- New spec passes locally: `cd ClientApp; npx playwright test e2e/messaging.spec.ts`
- PR shows green `backend`, `frontend` and `e2e` CI jobs
- PR receives an impl-review verdict that is not REJECTED

#### Manual Verification:

- On https://moviedate-sk.fly.dev, two demo accounts that match each other complete add friend → message → reply, and a third account gets "Nie możesz pisać z tą osobą" on the pair's conversation URL

---

## Testing Strategy

### Unit Tests:

- Matches component: button vs link state, busy state, error alert.
- Friends component: both sections, empty state.
- Conversation component: rendering "Ty" vs name, send disabled on blank, append on success, 404 state.
- Friends service: URLs and error mapping (HttpTestingController).

### Integration Tests:

- The ten named tests in Phase 1 §6. The guardrail (stranger, matched-without-friendship, gate-before-validation) is proven here.

### Manual Testing Steps:

1. Locally: two browsers (one private window), the A→B add + message, B replies.
2. Production: the same with demo accounts, plus a third account hitting the pair URL directly.

## Performance Considerations

- The conversation read is one indexed query over `(SenderId, RecipientId, SentAt, Id)`, run for both directions and capped at 100 rows.
- The gate costs two PK lookups.
- `IsFriend` adds one query per match list read.

## Migration Notes

- The migration only adds tables, so it is safe to roll back the image (the old code ignores the new tables).
- No data backfill.

## References

- Research: `context/changes/first-message-to-friend/research.md`
- Roadmap: `context/foundation/roadmap.md` S-07, S-08
- PRD: `context/foundation/prd.md` FR-006, FR-007, US-01, Guardrails
- Pattern: `Data/AppDbContext.cs:93-112` (Match config), `Endpoints/WatchlistEndpoints.cs:24-26` (404 rule), `tests/MovieDate.Tests/WatchlistTests.cs:128` (other-user test), `ClientApp/e2e/critical-path.spec.ts` (two-user e2e)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — friends and conversations

#### Automated

- [x] 1.1 Build passes — e6f9fa6
- [x] 1.2 Migration adds only the two new tables — e6f9fa6
- [x] 1.3 All backend tests pass, including the new friends and conversation tests — e6f9fa6

#### Manual

- [x] 1.4 Friend and conversation endpoints behave as specified for a matched local pair — e6f9fa6

### Phase 2: SPA — add friend, friends page, conversation

#### Automated

- [x] 2.1 SPA unit tests pass — 14407d3
- [x] 2.2 Production build passes — 14407d3
- [x] 2.3 Existing e2e suite still passes — 14407d3

#### Manual

- [x] 2.4 Two-browser local check: add, message, recipient replies — 14407d3

### Phase 3: E2E journey, PR and production

#### Automated

- [x] 3.1 New messaging spec passes locally
- [ ] 3.2 PR shows green backend, frontend and e2e CI jobs
- [ ] 3.3 PR receives a non-REJECTED impl-review verdict

#### Manual

- [ ] 3.4 Production check with demo accounts, including a third-account 404
