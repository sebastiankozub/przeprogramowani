---
project: "MovieDate"
context_type: greenfield
created: 2026-09-16
updated: 2026-09-16
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "missing feature — no existing dating app matches on movie taste"
    - topic: "insight"
      decision: "movie taste is a stronger compatibility predictor than photos/bio"
    - topic: "primary persona scope"
      decision: "singles broadly, with messaging emphasis on movie/cinema fans"
    - topic: "auth model"
      decision: "standard login (email + password / OAuth)"
    - topic: "role model"
      decision: "flat — single 'user' role for MVP; moderation/admin deferred"
    - topic: "MVP first-flow scope"
      decision: "register/login, add rated movies + watchlist, set city, on-demand match, add friend, send message"
    - topic: "success criteria — secondary"
      decision: "user gets more than one ranked match option, not just one result"
    - topic: "success criteria — guardrails"
      decision: "exact location never shown publicly (city only); messages visible only to matched/accepted friends"
    - topic: "movie catalog (FR-002)"
      decision: "local seeded movie table with ID on MVP; external API (e.g. TMDB) later"
    - topic: "watchlist role (FR-003)"
      decision: "folded into matching algorithm as a shared 'movie to watch together' signal, not just informational"
    - topic: "city input (FR-004)"
      decision: "selected from seeded lookup table of Polish cities/counties/voivodeships; MVP scoped to Poland only"
    - topic: "matching cold-start (FR-005)"
      decision: "fallback to closest available candidates when no strong match exists"
    - topic: "friend/messaging model (FR-006/FR-007)"
      decision: "one-sided add on MVP; messaging open to anyone on matched list; no block/report on MVP"
    - topic: "business logic — proximity scoring"
      decision: "distance-decayed geographic score (not same-city binary), combined with shared movie-taste overlap; never reaches zero"
    - topic: "non-functional requirements"
      decision: "match request < 2s typical; credentials never stored/transmitted in plain text; exact location never exposed externally"
    - topic: "product type / scale"
      decision: "web-app; small user scale (self/handful) for MVP"
    - topic: "non-goals"
      decision: "no TMDB integration, no non-Poland locations, no moderation, no daily cron matching, no real-time chat — all on MVP"
    - topic: "CRUD business logic (FR-002/FR-003/FR-005)"
      decision: "add/edit/remove of watched or watchlist movies is not plain CRUD — each write triggers an asynchronous recompute of the user's match results (background job/thread; write itself doesn't block); frontend polls ~10s to observe updates"
    - topic: "match list persistence (FR-005)"
      decision: "match list is append-only per user — a candidate, once matched, is never removed by a later recompute; only their score updates"
  frs_drafted: 7
  quality_check_status: accepted
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 1
  hard_deadline: 2026-11-04
  after_hours_only: true
---

## Vision & Problem Statement

Singles browsing typical dating apps (Tinder, Bumble) match on photos and short bios, with no mechanism to filter for shared taste in film — leading to matches that fizzle at "we have nothing to talk about." This costs users time on conversations and matches that go nowhere, and they're left discovering shared interests (or their absence) only after the fact, through trial and error.

The insight: movie taste — what someone has watched, rated highly, and wants to watch — is a stronger predictor of compatibility than a curated photo set or a one-line bio. This hasn't been built into mainstream dating apps because they optimize for mass-market breadth, not for a specific shared-interest signal like film taste. The niche of committed film/cinema fans is underserved by platforms designed for the broadest possible audience.

## User & Persona

**Primary persona:** Singles broadly, using the app to find romantic/social connections — with the product's positioning and matching signal specifically appealing to and rewarding people who are fans of film and cinema (not just casual, occasional viewers).

## Success Criteria

### Primary
- A user can register/log in, add watched movies with ratings and a "want to watch" list, set their city, request an on-demand match, add a matched user as a friend, and exchange at least one message with them — end to end, without errors.

### Secondary
- The match request returns more than one ranked candidate, not just a single result, so the user has a real choice.

### Guardrails
- A user's exact location is never shown to other users — only city-level location is used for matching and ever surfaced.
- Messages are visible only between two users who have matched and been added as friends — never leaked to unmatched users.

## User Stories

### US-01: User finds and messages a movie-taste match

- **Given** a logged-in user who has rated at least a few movies, added movies to their watchlist, and set their city
- **When** they request a match
- **Then** they see a ranked list of candidate users ordered by shared highly-rated movies, shared watchlist movies, and city, can add one as a friend, and can send that friend a message

#### Acceptance Criteria
- Match ranking returns more than one candidate when more than one exists
- If no strong matches exist, the closest available candidates are shown instead of an empty result (fallback, not a hard empty-state)
- Only city-level location is shown/used — never exact location
- Messaging is possible with anyone on the user's matched list — MVP does not require mutual friend acceptance

## Functional Requirements

- FR-001: User can register and log in (email + password / OAuth). Priority: must-have
  > Socratic: Counter-argument considered: lack of email/phone verification enables fake profiles. Resolution: stands as written — verification hardening deferred past MVP.
- FR-002: User can add, edit, or remove a watched movie and its rating, selected from a movie table in the database (not free text). Adding, editing, or removing a watched movie triggers an asynchronous recompute of that user's match results — the write itself responds without waiting for the recompute to finish. Priority: must-have
  > Socratic: Counter-argument considered: free-text movie titles would cause duplicates/typos and corrupt matching data. Resolution: movies are selected from a local seeded movie table with an ID on MVP; swapped for an external catalog API (e.g. TMDB) later.
- FR-003: User can add or remove a movie on their "want to watch" list; shared watchlist movies feed into the matching algorithm (FR-005), and any change to the list triggers the same asynchronous recompute as FR-002. Priority: must-have
  > Socratic: Counter-argument considered: the watchlist doesn't affect matching, so it's cost without value. Resolution: rejected — watchlist overlap is folded into matching as a "movie to watch together" signal.
- FR-004: User can set their city, selected from a database list of Polish cities/counties/voivodeships with associated geographic coordinates (MVP scoped to Poland only). Priority: must-have
  > Socratic: Counter-argument considered: free-text city input causes inconsistent values ("Warszawa" vs "warszawa") and breaks location matching. Resolution: city is selected from a seeded lookup table with coordinates, not free text.
- FR-005: User can view their match list — a running, append-only list of candidates scored on shared highly-rated movies, shared watchlist movies, and geographic proximity (a distance-decayed score, never zero), with a fallback to the closest available candidates when no strong match exists. Once a candidate appears in a user's match list, they are never removed by a later recompute — only their score updates. Match results are kept current by the asynchronous recompute triggered on every watched-movie or watchlist write (FR-002, FR-003); the client observes the updated list becoming available shortly after, without blocking on the write itself. Priority: must-have
  > Socratic: Counter-argument considered: cold start (few users) means early users may see no meaningful matches. Resolution: fallback shows the closest available candidates instead of an empty result; proximity score already never reaches zero, so distance alone never fully excludes a candidate.
- FR-006: User can add a matched candidate as a friend; on MVP this is one-sided (no mutual acceptance required) and unlocks messaging with that candidate. Priority: must-have
  > Socratic: Counter-argument considered: one-sided friending could conflict with the "messages only between matched friends" guardrail. Resolution: MVP allows messaging anyone on the user's matched list — one-sided add is sufficient; mutual-acceptance flow is a future refinement.
- FR-007: User can send a message to a friend and view the stored message history with them. Messages are persisted records the user retrieves on request — there is no live delivery mechanism. Priority: must-have
  > Socratic: Counter-argument considered: no block/report mechanism is a real risk (unwanted contact) for a dating app. Resolution: accepted as an MVP limitation — moderation (block/report) is deferred past MVP, consistent with the flat role model decided in Access Control.

## Business Logic

The application computes a compatibility score between two users by combining shared movie taste (overlap in highly-rated movies and in watchlist movies) with geographic proximity, where proximity contributes a distance-decayed score that shrinks with distance but never reaches zero.

Inputs the rule consumes: each user's rated movies (with ratings), watchlist movies, and city (resolved to coordinates). Output: a growing, append-only list of candidate users, each with a composite compatibility score that can update over time but whose presence on the list never disappears. The user encounters this as their match list — a ranked list of people they've ever matched with, not a single yes/no verdict, and not a list that loses people as their data changes.

Proximity scoring follows a decay curve rather than a same-city cutoff: same city scores near the maximum, ~100km scores roughly half, ~500km scores roughly a quarter, and the score approaches but never reaches zero at any distance — so no candidate is ever fully excluded by location alone, which is also what makes the cold-start fallback (FR-005) meaningful rather than empty.

Adding, editing, or removing a watched or watchlist movie is not a plain data write — it carries the rule that the user's match results must reflect their current movie data, so each such write triggers a recompute of their match results. Two rules govern this recompute: (1) it happens asynchronously — the write responds without waiting for the recompute to complete, and (2) it only ever updates scores for candidates already on the list or adds newly-qualifying candidates — it never removes a candidate who has previously appeared.

## Non-Functional Requirements

- Viewing the match list returns a result within a user-perceived instant (< 2s typical), with visible feedback if it takes longer.
- A movie-list write (add/edit/remove) responds without waiting for the match recompute to finish; the updated match list becomes visible to the user within roughly 10 seconds of the write.
- Login credentials are never stored or transmitted in plain text.
- A user's exact location never leaves the system toward another user — only city-level granularity is ever exposed externally.

## Access Control

Standard login (email + password / OAuth). Flat role model for MVP — every authenticated user is a plain "user" with the same capabilities; no admin/moderator role yet. Moderation (reported profiles, banning) is deferred past MVP. Unauthenticated visitors cannot reach any matching, profile, or messaging functionality — login is required before any gated route.

## Non-Goals

- No integration with an external movie catalog API (e.g. TMDB) on MVP — movies come from a local seeded table with IDs; external catalog integration is a post-MVP upgrade.
- No support for locations outside Poland on MVP — city/county/voivodeship lookup is Poland-only.
- No moderation (block/report) on MVP — consistent with the flat role model from Access Control; deferred to a later phase.
- No daily automated match scheduling (cron) on MVP — recompute is triggered only by a user's own movie-list writes, not a scheduled batch job.
- No real-time chat (websockets, push notifications) on MVP — messaging is a simple send/receive flow.

## Forward: tech-stack

*(Informational — not part of the PRD schema. For the downstream tech-stack-selection step.)*

- Preferred stack direction: ASP.NET + Angular.
- Auth provider: user is weighing Keycloak vs. ASP.NET Identity. Decision depends on submission timeline — leaning ASP.NET Identity for a tighter first deadline (less setup overhead), with Keycloak as a possible post-MVP upgrade if pursuing the longer timeline. Final call belongs to tech-stack-selector.
- Match recompute mechanism: user wants the recompute (FR-002/003/005) implemented as a background job on a separate thread — the write request returns immediately without waiting for it. Frontend polls the match list endpoint roughly every 10 seconds to pick up updates. This is an implementation mechanism (thread/job/polling), not a PRD-level decision — the PRD-level facts are captured as NFRs (async write, ~10s visibility bound); the specific mechanism is for the implementation-planning step to decide (could end up as a background thread, a queue worker, or something else).
- Database: explicitly open — "baza danych do ustalenia w tech-stack-selectorze." Needs to support a geo-lookup table (Polish cities/counties/voivodeships with coordinates) and distance-decay scoring queries.

