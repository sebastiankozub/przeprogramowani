<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Friends and first message (S-07 + S-08)

- **Plan**: context/changes/first-message-to-friend/plan.md
- **Scope**: Full plan, pre-commit. All three phases are in the working tree; no phase commits exist yet. The delivery steps (3.2–3.4) haven't happened yet.
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated checks were re-run during this review:
- `dotnet build`: 0 errors.
- `dotnet test`: 146/146.
- `ng test`: 53/53.
- `npm run build`: OK.
- The e2e suite passed 3/3 (including `messaging.spec.ts`) in the implementation gate run before this review.

Plan drift check:
- All five backend contracts, the gate, the 10 named integration tests, the SPA contract, the e2e contract and the §6-only test-plan edit match the plan.
- The extras are benign:
  - a shared `completeProfile` e2e helper;
  - `.conversation-body` CSS;
  - a 409 that marks the row as a friend;
  - a back link in the 404 alert;
  - unique-key and foreign-key race catches.

## Findings

### F1 — Conversation read scans the pair's whole history

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Services/ConversationService.cs:61-68 (index at Data/AppDbContext.cs:155)
- **Detail**:
  - The query is `(A→B) OR (B→A)` ordered by `SentAt DESC, Id DESC` with `Take(100)`.
  - Postgres can only combine the two directions on the `(SenderId, RecipientId, SentAt, Id)` index with a BitmapOr. That loses the index order, so it reads and sorts every message the pair has ever exchanged before applying the limit.
  - The plan's performance note claims "one indexed query … capped at 100 rows", but that holds for the returned rows, not for the rows read.
  - This isn't a problem at MVP volumes. It grows linearly with history.
- **Fix A ⭐ Recommended**: two direction-specific queries, each `OrderByDescending(SentAt).ThenByDescending(Id).Take(100)`, then merge in memory, take the latest 100 and reverse.
  - Strength: each query is an ordered index range scan with a limit, so the cost is bounded at 200 rows. No schema change. It's a small edit inside one method, and `Conversation_returns_latest_100_messages` already pins the behaviour.
  - Tradeoff: two round trips instead of one, and slightly more code.
  - Confidence: HIGH — standard pattern for an OR across a composite index.
  - Blind spot: the query plan wasn't checked with EXPLAIN on real data.
- **Fix B**: accept for MVP and note it in the plan's performance section.
  - Strength: no code change before the 21:00 cut-off.
  - Tradeoff: the cost grows with each pair's history, and the plan's claim stays inaccurate.
  - Confidence: HIGH — volumes are tiny today.
  - Blind spot: none significant.
- **Decision**: FIXED (Fix A) — two per-direction queries merged in memory; Friends+Conversations tests 10/10

### F2 — Progress lags evidence; manual checks and delivery are pending

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/first-message-to-friend/plan.md (## Progress)
- **Detail**:
  - Rows 2.1, 2.2, 2.3 and 3.1 passed but are still `[ ]`.
  - The manual rows 1.4 and 2.4 haven't been confirmed.
  - The 3.2–3.4 delivery steps haven't happened yet.
  - No phase commits exist, so the "one commit per phase" plan is not met yet.
  - Rows 1.1–1.3 are `[x]` with no SHA yet, which is expected before the commit.
- **Fix**: flip 2.1–2.3 and 3.1, confirm 1.4 and 2.4 manually, then make the three phase commits through `/10x-implement`.
- **Decision**: FIXED — 2.1–2.3 and 3.1 ticked; 1.4, 2.4 and the commits remain with /10x-implement

### F3 — Conversation page doesn't reload when userId changes

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/friends/conversation.component.ts:39-49, 63
- **Detail**:
  - The conversation loads in `ngOnInit`, but `send()` reads `userId()` at the moment of sending.
  - Navigating directly from /friends/A to /friends/B reuses the component: A's messages would stay on screen while sends go to B.
  - No link in the app makes this navigation today.
- **Fix**: load from an `effect` keyed on `userId()` (or leave as is until a conversation-to-conversation link exists).
- **Decision**: FIXED — load moved to an effect on userId() with a stale-response guard; ng test 53/53

### F4 — No test for the self-id branch of the gate

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: tests/MovieDate.Tests/ConversationsTests.cs
- **Detail**:
  - These paths are untested: `userId == otherId` → 404 on GET and POST, the unique-key → 409 race, the foreign-key → 404 race, and GET returning 404 when the other user's profile is incomplete.
  - The self-id check is part of the gate.
- **Fix**: add `Own_id_conversation_returns_404`. The race paths are optional.
- **Decision**: FIXED — added Own_id_conversation_returns_404 (GET + POST); dotnet test 147/147

### F5 — `AlreadyFriendTitle` comment says the SPA keys on it; it doesn't

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Endpoints/FriendsEndpoints.cs:13
- **Detail**: the SPA branches on HTTP 409 only (`friends.service.ts:63`). Nothing in ClientApp reads `already_friend`.
- **Fix**: reword the comment to "Machine-readable problem title for an existing friendship".
- **Decision**: FIXED — comment reworded

### F6 — e2e local types declare `city` as string

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: ClientApp/e2e/messaging.spec.ts:15-21
- **Detail**: the API returns `city` as `{ id, name, voivodeship }`. The field is never read, so the test is unaffected.
- **Fix**: drop `city` from the local interfaces.
- **Decision**: FIXED — city dropped from local e2e interfaces; messaging.spec.ts green

### F7 — Malformed JSON returns 400 before the gate

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Endpoints/ConversationsEndpoints.cs:40
- **Detail**:
  - Framework binding rejects an unparseable body before the handler runs.
  - The response is the same whether or not the pair exists, so nothing leaks.
  - Well-formed bodies follow the gate-first rule (`ConversationsTests.cs:106`).
- **Fix**: none needed.
- **Decision**: ACCEPTED — framework binding 400 is identical for pair and stranger; no leak
