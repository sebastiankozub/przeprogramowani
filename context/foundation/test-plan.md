# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-11-03

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the
   team is worried about X, and the failure would surface somewhere in
   <area>" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `Endpoints/`, `Services/`, `Data/` (excluding `Migrations/` and `Seed/`), `Contracts/`, `ClientApp/src/app/` — last 30 days, 60 commits. The whole product was built inside that window, so churn reflects construction more than instability; it is used only as a tie-breaker.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | A user rates or removes a movie, edits the watchlist, or changes city, and their match list does not reflect it within ~10 s without any click — the product's core promise silently stops working | High | High | PRD FR-002, FR-003, FR-005, NFR-02; interview Q1 (stale match list), Q3 (matching logic); roadmap S-06 (in-process queue on 2 machines); hot-spot dir `Services/` (16 file changes/30d) |
| 2 | An API response exposes another user's email, exact coordinates or private ratings, or lets a logged-in user read or change a resource that belongs to someone else (abuse: IDOR / PII leakage) | High | Medium | PRD §Guardrails, NFR-04, §Access Control; interview Q1 (data leak), Q4 (privacy of API responses); roadmap S-07/S-08 add new per-pair endpoints |
| 3 | The match ranking is wrong: shared favorites or shared watchlist titles are ignored, proximity decay drifts from the PRD curve (~100 km ≈ half, ~500 km ≈ quarter, never zero), or a recompute removes a candidate who was listed before | High | Medium | PRD §Business Logic, FR-005; interview Q1 (wrong ranking), Q3 (matching logic) |
| 4 | The primary path breaks in a real browser — register or log in, set name and city, rate a movie, open `/matches` — while API-level tests stay green (session cookie, route guards, redirects, status polling) | High | Medium | PRD US-01; interview Q3 (SPA screens), Q4 (full path in a browser never tested); hot-spot dir `ClientApp/src/app/` (94 file changes/30d; `auth/` 13, `movies/` 16, `matches/` 10) |
| 5 | A change with a failing test or a backward-incompatible migration reaches production because nothing runs the suite before deploy | High | Medium | interview Q3 (migrations / data), Q4 (no automatic gate); `lessons.md` (keep migrations backward-compatible); `deploy-plan.md` (deploy is manual, no CI) |

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | After a rating write, opening `/matches` announces the pending update and then shows the new or re-scored candidate by itself, without pressing anything, and the indicator clears. (Research 2026-11-03: pending is checked when the page loads; an already-open `/matches` is not refreshed — out of S-06 scope, so the test writes first, then opens the page.) | "The write returned 201, so matches will update" | write → trigger → debounce → worker path; how the SPA learns that an update landed; which backend integration tests already cover the server side | e2e (one browser test on top of existing integration tests) | fixed `sleep` instead of waiting for a visible outcome; driving the API directly and calling it e2e |
| #2 | Every response describing another user carries only display name, city and score; requests for another user's resource id return 404/403 and change nothing | "Authenticated means authorized" | which endpoints return data about other users; ownership checks on per-user resources; what existing tests assert about response bodies | integration | happy-path-only; expected field list copied from the response DTO instead of from PRD NFR-04 |
| #3 | Scores match independent checkpoints taken from the PRD text; a candidate stays listed after the owner's data changes so the pair no longer qualifies | "The current output is correct" | where the decay and taste weights live; which existing tests compute their expected value with the production formula | unit (scoring) + integration (append-only) | oracle problem — expected value computed by the same formula as production |
| #4 | A fresh user completes the primary path in a real browser against the real app and database | "API green means the UI works" | auth/session shape between SPA and API, route guards, how the app is served for tests, data isolation between runs | e2e (single critical-path test) | many brittle e2e tests instead of one critical one; selectors tied to styling |
| #5 | A pull request with a red test cannot be merged or deployed unnoticed | "It passed on my machine" | how backend tests start Postgres (Testcontainers needs Docker on the runner); how SPA tests and e2e run headless | CI gate | a gate that silently skips the container-backed tests |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Critical-path browser coverage | Prove a fresh user can go register → profile → rate → `/matches` and see the list update by itself in a real browser | #1, #4 | e2e (Playwright) | complete | context/changes/testing-critical-path-e2e/ |
| 2 | Quality gates in CI | Prove that a red backend, SPA or e2e test blocks a pull request | #5 | CI gate (GitHub Actions) | complete | context/changes/testing-ci-quality-gates/ |
| 3 | Privacy and ranking contract | Prove responses never leak another user's private data and that ranking follows PRD checkpoints, with oracles taken from the PRD | #2, #3 | integration + unit | not started | — |

## 4. Stack

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| backend unit + integration | xUnit + `Microsoft.AspNetCore.Mvc.Testing` | xunit 2.9.3, Mvc.Testing 10.0.14 | 11 test classes, 136 tests; real API in-process |
| database for tests | Testcontainers.PostgreSql | 4.16.0 | throwaway Postgres per run; needs Docker |
| SPA unit | Vitest via `ng test` | vitest ^5.0.0, Angular ^22.2.0 | 8 spec files, 33 tests |
| e2e | Playwright Test (`ClientApp/playwright.config.ts`) | @playwright/test 1.64.1 | 2 specs in `ClientApp/e2e/`; starts API + `ng serve`; details in `context/foundation/test-stack.md` |
| CI | GitHub Actions `.github/workflows/ci.yml` | — | jobs backend, frontend, e2e on every PR and push to main; agent review in `impl-review.yml` |

Test-base profile: `meaningful` — backend integration suite across every feature plus SPA unit specs; no browser-level test and no automatic gate.

**Stack grounding tools (current session):**
- Docs: none — Context7 not available in current session; checked: 2026-11-03
- Search: none — Exa.ai not available in current session; checked: 2026-11-03
- Runtime/browser: Claude in Chrome / built-in browser available — possible exploratory aid for Phase 1, not a test layer; checked: 2026-11-03
- Provider/platform: Neon MCP available — read-only prod verification only, not used as a gate; GitHub CLI not available in this session; checked: 2026-11-03

## 5. Quality Gates

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| build (`dotnet build`, `ng build`) | local + CI | required (CI) | compile and template errors |
| backend integration (`dotnet test`) | local + CI | required (CI) | API, matching and persistence regressions |
| SPA unit (`ng test`) | local + CI | required (CI) | component and service regressions |
| e2e on the critical path (`npm run e2e` in `ClientApp/`) | local + CI on PR | required (CI) | broken primary user path, `/matches` not refreshing itself |
| privacy and ranking contract tests | local + CI | required after §3 Phase 3 | data leaks, ranking drift |

## 6. Cookbook Patterns

### 6.1 Adding a backend integration test

- TBD — see §3 Phase 3 (privacy and ranking contract). Existing suite under `tests/MovieDate.Tests/` is the current reference until then.
- Risk #2 for friends and messages (2026-11-03, `first-message-to-friend`): `tests/MovieDate.Tests/FriendsTests.cs` and `ConversationsTests.cs` prove a stranger gets 404 and no data; matched pairs are seeded with a shared helper on the no-recompute fixture, because the top-up can match strangers.

### 6.2 Adding a unit test for scoring rules

- TBD — see §3 Phase 3 (PRD-checkpoint oracle for proximity decay and taste weights).

### 6.3 Adding an e2e test

- **When**: only for a risk that crosses browser → auth → API → DB or lives only in the rendered UI. One test per risk; anything an integration test can prove goes to `tests/MovieDate.Tests/` instead.
- **Location / naming**: `ClientApp/e2e/<scenario>.spec.ts`, one test per file, provenance header naming the test-plan risk; shared helpers in `ClientApp/e2e/support/`.
- **Reference tests**: `ClientApp/e2e/auth-guard.spec.ts` (seed: signed-out path), `ClientApp/e2e/critical-path.spec.ts` (full path + background update), `ClientApp/e2e/messaging.spec.ts` (second journey: add friend → write → partner replies through the API; user set up via `page.request` + `completeProfile`, partner context kept alive).
- **Users and data**: never log in a shared account — create a throwaway `e2e-<id>@moviedate.test` user per test with `registerViaApi`; seed other users through the public API (`createPartner`, fresh `request.newContext`), drive only the user under test through the UI. No delete-account endpoint exists, so accounts stay in the local DB; unique ids keep runs independent.
- **Locators and waits**: `getByRole` / `getByLabel` only; Material autocompletes need the option clicked (`getByRole('option', …)`); "Zapisz" needs `exact: true`. Wrap the first form submit in `expect(async () => …).toPass()` (SPA hydration); never `waitForTimeout`.
- **Background recompute**: the e2e API runs with `MatchRecompute` quiet 5 s / max 15 s, so `/matches` opened right after a write still shows "Trwa aktualizacja dopasowań…". Write first, then open the page — an already-open page does not refresh.
- **Run locally**: `docker compose up -d`, stop any local `dotnet run`, then `cd ClientApp; npm run e2e` (one spec: `npx playwright test e2e/<name>.spec.ts`; trace: add `--trace on`, then `npx playwright show-report`).
- **Prove it protects**: break the behavior the risk targets, re-run, see red on the risk assertion, restore.

### 6.4 Adding a test for a new endpoint that returns data about other users

- TBD — see §3 Phase 3 (response contains only display name, city and score; foreign ids return 404/403).

### 6.5 Per-rollout-phase notes

- Phase 1 (2026-11-03, `testing-critical-path-e2e`): risk #1 had to be reframed to "write, then open /matches" — the SPA checks pending on load only. MaxDelay had to go up to 15 s because three profile/rating writes in a row could trigger the recompute before the page opened.

## 7. What We Deliberately Don't Test

- **Visual / snapshot tests of screens** — styling changes often; pixel diffs would break constantly and catch little. Re-evaluate if a design system is introduced. (Source: Phase 2 interview Q5.)
- **Seed catalog content** — which movies and cities the Wikidata seed contains is data, not behaviour. Re-evaluate if seeding logic gains rules beyond upsert. (Source: Phase 2 interview Q5.)
- **Load / performance** — MVP with a handful of users; NFR-01 (< 2 s) is checked manually on production. Re-evaluate before any public launch. (Source: Phase 2 interview Q5.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-11-03
- Stack versions last verified: 2026-11-03
- AI-native tool references last verified: 2026-11-03

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
