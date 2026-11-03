# Friends and first message (S-07 + S-08) — Plan Brief

> Full plan: `context/changes/first-message-to-friend/plan.md`
> Research: `context/changes/first-message-to-friend/research.md`

## What & Why

A user adds someone from their match list as a friend and sends them a message, and the other person can read it and reply. This is the PRD's primary success criterion ("exchange at least one message", `prd.md:31`) and the last step of milestone M-01. The privacy guardrail has to hold: only a matched pair with a friendship can see their messages.

## Starting Point

- The ranked match list is done: S-05 plus async recompute in S-06.
- Match rows are mirrored and append-only, keyed `(UserId, CandidateId)`.
- `MatchResponse` already carries the candidate's id.
- Neither friends nor messages exist yet.
- CI already runs backend, SPA and every e2e spec on each PR.

## Desired End State

- **On /matches:** each row has "Dodaj do znajomych", which becomes "Napisz" once added.
- **/friends** shows "Znajomi" and "Dodali Cię do znajomych".
- **/friends/:userId** shows the last 100 messages and a send form.
- **Anyone outside the pair** gets 404 on read and send. Integration tests and one browser journey in CI prove this.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Recipient's rights | A friendship in either direction opens the pair; both read and send | Matches PRD "no mutual acceptance" and "exchange a message" (`prd.md:31,52`) | Plan |
| Unfriend | Out of scope | Add-only, like matches; the PRD doesn't ask for it | Plan |
| Limits | Body 1–2000 chars (trimmed); latest 100 messages, oldest first | Bounded responses with no paging UI | Plan |
| Friend state on /matches | Additive `IsFriend` on `MatchResponse` | One request, button state consistent with the list | Plan |
| Pair gate | `Matches(me,X)` AND friendship either way | Both are PK lookups; matches are symmetric and permanent | Research |
| Authorization style | Scoped queries, 404 for anyone outside the pair, gate before validation | Repo convention; no existence leak through 400 vs 404 | Research (Context7 + Exa) |
| Conversation model | Address by other user's id; no Conversations table | No guessable id; 1:1 store-and-retrieve needs no extra table | Research (Exa) |
| Message order and index | `(SentAt, Id)`, index `(SenderId, RecipientId, SentAt, Id)` | Fully unique order; keyset paging possible later | Research (Context7 EF Core) |

## Scope

**In scope:**
- `Friendships` and `Messages` tables (additive migration).
- 5 endpoints.
- `IsFriend` on matches.
- 3 SPA views and a nav link.
- 10 integration tests and SPA specs.
- 1 e2e spec in CI.
- A PR, deploy and production check.

**Out of scope:** unfriend, mutual acceptance, realtime and unread counts, paging past 100, a Conversations table, groups, editing.

## Architecture / Approach

The domain is a chain of write-time invariants. Each step checks the one before it, and every conversation call goes through the same gate first.

```
Match (mirrored) → Friendship (one-sided) → Message
```

- `FriendsService` adds and lists friends.
- `ConversationService` runs the gate and then reads or writes.
- Endpoints follow the existing `Endpoints/` + `Contracts/` + outcome-enum pattern.
- The SPA adds a `friends/` feature folder.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend | Tables, gate, endpoints, `IsFriend`, guardrail integration tests | Test isolation: a recompute top-up can match the "stranger" — use the no-worker fixture and seeded matches |
| 2. SPA | Add-friend button, /friends, conversation page, nav, specs | The existing "no button" spec breaks — narrow it |
| 3. E2E + delivery | `messaging.spec.ts` in CI, PR with agent review, prod deploy | Match appearance timing in e2e (≤ 30 s, same as critical path) |

**Prerequisites:** branch `first-message-to-friend` from main; Docker running for Testcontainers.
**Estimated effort:** about 4–5 h across 3 phases. Self-imposed cut-off: 21:00 2026-11-03 with green CI, otherwise the branch stays unmerged.

## Open Risks & Assumptions

- **Assumption:** everyone on a match list has a display name and city, so `FriendResponse` never lacks them (`MatchService.ListAsync` filter).
- **Risk:** the e2e journey adds about 30–40 s to the CI e2e job.
- **Not covered:** messages from an account that is later deleted disappear through cascade. That is acceptable, and there is no delete-account feature.

## Success Criteria (Summary)

- Two real users on production go from match to friend to a two-way message without errors.
- A third user can't read or post to that pair's conversation (404), proven by integration tests in CI.
- The CI e2e job runs the messaging journey on every PR.
