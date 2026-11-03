---
project: MovieDate
version: 1
status: draft                    # draft | active | locked
created: 2026-11-02
updated: 2026-11-03
prd_version: 1
main_goal: speed
top_blocker: decisions
milestone_id: first-message-from-a-match
milestone_seq: 1
milestone_status: open           # open | done
---

# Roadmap: MovieDate

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; the next milestone continues this file. Archive only when an independent roadmap replaces it.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-01: First message from a movie-taste match** — Status: open

- **Intent:** A real user can go the whole primary path in production — register, rate movies, build a watchlist, set a city, get a ranked match list, add a match as a friend, and send them a message — proving the PRD's primary success criterion end to end.
- **Source materials:** `context/foundation/prd.md` (v1)
- **Done when:** every F-NN and S-NN below is `done`.
- **Scope anchors:** FR-001..FR-007 (all must-have), US-01, PRD §Business Logic, §Access Control, §Non-Functional Requirements.

## Vision recap

Mainstream dating apps match on photos and short bios, so matches often fizzle at "we have nothing to talk about." MovieDate bets that film taste — what someone has watched, rated highly, and wants to watch — is a stronger compatibility signal, and builds matching around shared taste plus city-level proximity in Poland.

## North star

**S-08: User sends a first message to a friend they found through their match list** — this is the PRD's primary success criterion taken literally: only when a message lands do we know the whole chain (account → taste data → ranked matches → friend → conversation) works end to end. Sequencing goal is speed, so everything not on the path to S-08 is parked or runs in parallel.

> "North star" here means the smallest end-to-end flow whose successful delivery proves the product works — every other item matters only insofar as it gets us there. In this project it sits at the end of the dependency chain because messaging needs a friend, a friend needs a match, and a match needs taste data.

## At a glance

| ID   | Change ID                | Outcome (user can …)                                                                 | Prerequisites    | PRD refs                          | Status   |
| ---- | ------------------------ | ------------------------------------------------------------------------------------ | ---------------- | --------------------------------- | -------- |
| F-01 | prod-postgres-connection | (foundation) production app reads/writes a managed Postgres; schema migrates on deploy | —                | FR-001, NFR-03                    | done |
| S-01 | register-and-login       | register with email + password, log in, and land on a gated home                     | F-01             | FR-001, US-01, NFR-03, §Access Control | done |
| S-02 | rate-watched-movies      | pick movies from the catalog and add, edit, remove them with a rating                 | S-01             | FR-002, US-01                     | done |
| S-03 | set-home-city            | set their city from a list of Polish locations                                       | S-01             | FR-004, US-01, NFR-04             | done |
| S-04 | want-to-watch-list       | add and remove movies on a "want to watch" list                                      | S-02             | FR-003, US-01                     | done |
| S-05 | ranked-match-list        | request a match and see a ranked list of candidates scored on taste and distance     | S-02, S-03, S-04 | FR-005, US-01, NFR-01, NFR-04, §Business Logic | in-progress |
| S-06 | async-match-recompute    | see their match list refresh within ~10 s after changing their movies, without waiting | S-05             | FR-002, FR-003, FR-005, NFR-02    | proposed |
| S-07 | add-match-as-friend      | add a candidate from their match list as a friend                                    | S-05             | FR-006, US-01                     | proposed |
| S-08 | first-message-to-friend  | send a message to a friend and read the stored conversation history                  | S-07             | FR-007, US-01, §Success Criteria (Guardrails) | proposed |

NFR numbering follows the order in PRD §Non-Functional Requirements: NFR-01 match list < 2 s, NFR-02 write is non-blocking + ~10 s visibility, NFR-03 credentials never plain text, NFR-04 only city-level location exposed.

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                 | Chain                                                   | Note                                                                                  |
| ------ | --------------------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| A      | Path to first message | `F-01` → `S-01` → `S-02` → `S-05` → `S-07` → `S-08`     | The shortest line to the north star; speed goal keeps this stream first.              |
| B      | Profile inputs        | `S-03`, `S-04`                                          | `S-03` branches after `S-01`, `S-04` after `S-02`; both join Stream A at `S-05`.       |
| C      | Match freshness       | `S-06`                                                  | Branches after `S-05`; runs in parallel with `S-07`/`S-08`, not on the north-star path. |

## Baseline

What's already in place in the codebase as of `2026-11-02` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — SPA shell scaffolded (`ClientApp/src/app`), empty routes; built into `wwwroot` and served by the backend.
- **Backend / API:** partial — minimal API in `Program.cs` with only `/health` and the template `/weatherforecast`; SPA fallback wired last.
- **Data:** absent — no data access layer, no connection string, no provisioned database.
- **Auth:** absent — no identity provider, no auth middleware. Provider decided: see `tech-stack.md` (2026-11-02 decisions).
- **Deploy / infra:** present — multi-stage `Dockerfile`, `fly.toml` (app `moviedate-sk`, region `fra`, `min_machines_running = 1`), manual-promotion GitHub Actions workflow `.github/workflows/deploy.yml`. The `worker` process-group TODO in `fly.toml` is obsolete per the in-process recompute decision.
- **Observability:** partial — `/health` endpoint wired to the Fly health check; default framework logging; no error tracking.

## Foundations

### F-01: Production database connection

- **Outcome:** (foundation) the deployed app connects to a managed Postgres outside Fly using a secret connection string, schema migrations are applied on deploy, and a database health check passes in production; local development runs the same database engine in a container.
- **Change ID:** prod-postgres-connection
- **PRD refs:** FR-001, NFR-03
- **Unlocks:** S-01 (first slice that persists data); production verification path for every later slice.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Which free-tier provider (Neon vs. Supabase) and which region closest to Fly `fra`; how cold-start on the free tier affects NFR-01 (< 2 s). — Owner: user. Block: no (decided as the first step of `/10x-plan prod-postgres-connection`).
- **Risk:** Kept deliberately minimal — connection, migration mechanism, health check, no domain tables; entities arrive with the slices that need them. Sequenced first because the deploy pipeline already exists, so a missing secret or migration-on-deploy failure surfaces now rather than mid-slice.
- **Status:** done

## Slices

### S-01: Register and log in

- **Outcome:** user can register with email + password, log in, log out, and land on a gated home page; an unauthenticated visitor hitting any product route is sent to login.
- **Change ID:** register-and-login
- **PRD refs:** FR-001, US-01, NFR-03, §Access Control
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Is OAuth sign-in required for MVP, or does email + password satisfy FR-001? — Owner: user. Block: no (email + password first; OAuth parked unless answered otherwise).
- **Risk:** Every later slice is per-user, so identity must exist first; the risk is the SPA ↔ API session handshake, which is easiest to debug while the app has a single feature.
- **Status:** done

### S-02: Rate watched movies

- **Outcome:** user can search the local movie catalog, add a watched movie with a rating, edit the rating, remove it, and see their rated list.
- **Change ID:** rate-watched-movies
- **PRD refs:** FR-002, US-01
- **Prerequisites:** S-01
- **Parallel with:** S-03
- **Blockers:** —
- **Unknowns:**
  - Rating scale (e.g. 1–5 vs. 1–10) — the PRD says "highly-rated" without defining it, and S-05 scoring depends on it. — Owner: user. Block: no (plan proposes, user confirms).
  - Source and size of the seeded movie catalog. — Owner: user. Block: no.
- **Risk:** First slice to introduce a domain table and the seeded local movie catalog; the asynchronous recompute part of FR-002 is deliberately deferred to S-06 so this slice stays a plain write.
- **Status:** done

### S-03: Set home city

- **Outcome:** user can pick their city from a predefined list of Polish cities/counties/voivodeships and see it on their profile; coordinates are stored but never shown to other users.
- **Change ID:** set-home-city
- **PRD refs:** FR-004, US-01, NFR-04
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-04
- **Blockers:** —
- **Unknowns:**
  - Which public dataset of Polish locations with coordinates to seed from, and its license. — Owner: user. Block: no.
- **Risk:** Introduces the seeded Polish location lookup with coordinates and fixes the location data shape that the distance-decay scoring in S-05 consumes; the guardrail "never expose exact location" is established here.
- **Status:** done

### S-04: Want-to-watch list

- **Outcome:** user can add a catalog movie to their "want to watch" list, remove it, and see the list.
- **Change ID:** want-to-watch-list
- **PRD refs:** FR-003, US-01
- **Prerequisites:** S-02
- **Parallel with:** S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low; reuses the movie catalog and picker from S-02. Kept separate so S-02 stays a single user action, and so the recompute trigger (S-06) has both inputs in place.
- **Status:** done

### S-05: Ranked match list

- **Outcome:** user who has rated movies, has a watchlist, and has set a city can request a match and see a ranked list of more than one candidate, scored on shared highly-rated movies, shared watchlist movies, and distance-decayed proximity; with no strong matches they see the closest candidates instead of an empty list, and a candidate once listed never disappears.
- **Change ID:** ranked-match-list
- **PRD refs:** FR-005, US-01, NFR-01, NFR-04, §Business Logic
- **Prerequisites:** S-02, S-03, S-04
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Relative weights of taste overlap vs. proximity, and the "highly-rated" threshold. — Owner: user. Block: no (PRD fixes the decay curve; weights proposed in the plan).
  - How to demonstrate > 1 candidate in production with few real users (seeded demo users vs. several test accounts). — Owner: user. Block: no.
- **Risk:** This is the core domain rule and the riskiest logic in the PRD (append-only list + never-zero decay + fallback); it gets the deepest testing. Recompute here is computed when the user requests a match; the background refresh is S-06.
- **Status:** in-progress

### S-06: Asynchronous match recompute

- **Outcome:** user who adds, edits, or removes a watched or watchlist movie gets an immediate response and sees their match list updated within roughly 10 seconds, with candidates only added or re-scored, never removed.
- **Change ID:** async-match-recompute
- **PRD refs:** FR-002, FR-003, FR-005, NFR-02
- **Prerequisites:** S-05
- **Parallel with:** S-07, S-08
- **Blockers:** —
- **Unknowns:**
  - Recompute runs in-process (per `tech-stack.md`), so queued work can be lost on restart or deploy — is "recompute again on next write or next list view" acceptable recovery? — Owner: user. Block: no.
- **Risk:** Not on the north-star path, so it runs alongside S-07/S-08; with `min_machines_running = 1` and auto-stop off in `fly.toml`, the in-process job doesn't silently stop the way a scaled-to-zero worker would.
- **Status:** proposed

### S-07: Add a match as a friend

- **Outcome:** user can add a candidate from their match list as a friend (one-sided, no acceptance needed) and see them in a friends list.
- **Change ID:** add-match-as-friend
- **PRD refs:** FR-006, US-01
- **Prerequisites:** S-05
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Small, but it is the gate the messaging guardrail relies on — only candidates actually on the user's match list may be friended.
- **Status:** proposed

### S-08: First message to a friend

- **Outcome:** user can send a message to a friend and open the stored conversation history with them; nobody outside that pair can read it.
- **Change ID:** first-message-to-friend
- **PRD refs:** FR-007, US-01, §Success Criteria (Guardrails)
- **Prerequisites:** S-07
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The north star; closes the primary success criterion. The main risk is the privacy guardrail (messages leaking to non-participants), so per-conversation authorization must be tested explicitly.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                | Suggested issue title                                       | Ready for `/10x-plan` | Notes |
| ---------- | ------------------------ | ----------------------------------------------------------- | --------------------- | ----- |
| F-01       | prod-postgres-connection | Connect production app to managed Postgres with migrations  | yes                   | Run `/10x-plan prod-postgres-connection`; pick Neon vs. Supabase first |
| S-01       | register-and-login       | User can register, log in, and reach a gated home           | no                    | After F-01 |
| S-02       | rate-watched-movies      | User can rate watched movies from the catalog               | no                    | After S-01; confirm rating scale |
| S-03       | set-home-city            | User can set their city from a Polish location list         | no                    | After S-01; parallel with S-02 |
| S-04       | want-to-watch-list       | User can manage a want-to-watch list                        | no                    | After S-02 |
| S-05       | ranked-match-list        | User sees a ranked match list based on taste and distance   | no                    | After S-02, S-03, S-04 |
| S-06       | async-match-recompute    | Match list refreshes within ~10 s after movie changes       | no                    | After S-05; parallel with S-07/S-08 |
| S-07       | add-match-as-friend      | User can add a match as a friend                            | no                    | After S-05 |
| S-08       | first-message-to-friend  | User sends a first message to a friend (north star)         | no                    | After S-07 |

## Open Roadmap Questions

1. **PRD `hard_deadline: 2026-11-04` no longer reflects the working target (2026-12-06).** Should the PRD frontmatter be updated so downstream planning doesn't optimize for a deadline that has passed? — Owner: user. Block: none (sequencing unchanged under `main_goal: speed`).
2. **FR-001 says "email + password / OAuth" — is OAuth part of MVP?** Roadmap assumes email + password only and parks OAuth. — Owner: user. Block: none (S-01 proceeds with email + password).

## Parked

- **External movie catalog API** — Why parked: PRD §Non-Goals; MVP uses a local seeded catalog.
- **Locations outside Poland** — Why parked: PRD §Non-Goals.
- **Moderation (block/report), admin roles** — Why parked: PRD §Non-Goals and §Access Control (flat role model).
- **Scheduled (daily) match recompute** — Why parked: PRD §Non-Goals; recompute is triggered by the user's own writes only.
- **Real-time chat (websockets, push)** — Why parked: PRD §Non-Goals; messaging is store-and-retrieve.
- **OAuth sign-in** — Why parked: speed goal; email + password covers FR-001's must-have path (see Open Roadmap Question 2).
- **Email/phone verification** — Why parked: FR-001 Socratic note defers verification hardening past MVP.
- **Mutual friend acceptance** — Why parked: FR-006 makes one-sided friending sufficient for MVP.
- **Separate identity server** — Why parked: rejected in the 2026-11-02 decisions; adds a service to deploy and operate.
- **Separate background worker process on Fly** — Why parked: in-process recompute chosen for MVP; revisit if recompute load or restart losses become a problem.
- **Error tracking and observability beyond `/health`** — Why parked: speed goal; not required by any PRD NFR.
- **Architecture modernization and CI/CD pipeline extensions** — Why parked: later project phases, not part of this PRD; to be shaped as a future milestone.

## Milestone History

## Done

- **F-01: (foundation) the deployed app connects to a managed Postgres outside Fly using a secret connection string, schema migrations are applied on deploy, and a database health check passes in production; local development runs the same database engine in a container.** — Archived 2026-11-02 → `context/archive/2026-11-02-prod-postgres-connection/`. Lesson: `context/foundation/lessons.md` → "Keep migrations backward-compatible with the previous release".
- **S-01: user can register with email + password, log in, log out, and land on a gated home page; an unauthenticated visitor hitting any product route is sent to login.** — Archived 2026-11-02 → `context/archive/2026-11-02-register-and-login/`. Lesson: `context/foundation/lessons.md` → "Key per-user data on the user id, never on the email claim".
- **S-02: user can search the local movie catalog, add a watched movie with a rating, edit the rating, remove it, and see their rated list.** — Archived 2026-11-02 → `context/archive/2026-11-02-rate-watched-movies/`. Lesson: —.
- **S-04: user can add a catalog movie to their "want to watch" list, remove it, and see the list.** — Archived 2026-11-02 → `context/archive/2026-11-03-want-to-watch-list/`. Lesson: —.
- **S-03: user can pick their city from a predefined list of Polish cities/counties/voivodeships and see it on their profile; coordinates are stored but never shown to other users.** — Archived 2026-11-03 → `context/archive/2026-11-03-set-home-city/`. Lesson: —.
