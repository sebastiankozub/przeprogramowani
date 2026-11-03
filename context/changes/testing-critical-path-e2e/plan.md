# Critical-path browser coverage (test rollout phase 1) Implementation Plan

## Overview

Add the project's first browser-level test: one Playwright test that protects risks #1 and #4 of `context/foundation/test-plan.md`. A fresh user registers, sets name and city, rates a movie a partner also loves, opens `/matches`, sees "Trwa aktualizacja dopasowań…", and then sees the partner appear with no click. A seed test also covers the auth guard redirect. No production code changes.

## Current State Analysis

- Backend recompute is covered by 7 integration tests (`tests/MovieDate.Tests/MatchRecomputeTests.cs:55-239`). Nothing tests the SPA side: status check on load, pending indicator, 2 s polling, list reload (`ClientApp/src/app/matches/matches.component.ts:47-64,104-130`).
- There is no browser test at all. `@playwright/test` is not a dependency and there is no Playwright config.
- Serving: locally `wwwroot` is empty, because only the Dockerfile copies the SPA build (`Dockerfile:9,17`). Development uses `ng serve` (default port 4200), whose proxy sends `/api` to `http://localhost:5069` (`ClientApp/proxy.conf.json:2-4`). Schema and catalog seeding run only via `dotnet run -- --migrate` (`Program.cs:19-23`).
- Auth: register logs the user in automatically and lands on `/` (`auth/auth.service.ts:84-95`). The cookie session works on plain http only in Development (`Endpoints/AuthEndpoints.cs:35-42`).

## Desired End State

`cd ClientApp && npm run e2e` starts Postgres-backed API and SPA, then runs two green tests:

- **Guard seed test:** a logged-out user is sent from `/matches` to login and back after logging in.
- **Critical-path test:** the full path as described in the Overview.

Breaking the SPA's reload-after-pending makes the critical-path test red. `test-plan.md` §6.3 describes how to add the next e2e test, §2 risk #1 wording matches research, and §3 Phase 1 is `complete`.

### Key Discoveries:

- Pending is evaluated only when `/matches` loads (`matches.component.ts:47-64`), so the test must write first and then open `/matches`.
- City and movie are Material autocompletes. An option must be clicked; typing alone keeps the save button disabled (`profile.component.ts:46-49,88-90`, `movie-search.component.ts:70-78`).
- "Zapisz" needs `exact: true`, because "Zapisz nazwę" also exists (`profile.component.html:25,44`). After the first rating there are two "Ocena" comboboxes (`movies.component.html:7-12,25-58`).
- Strong pair = both rate the same movie ≥ 7 (`Services/MatchScoringService.cs:15,58-59`). Strong candidates are always added and mirrored (`Services/MatchService.cs:248-253,288-320`), independent of the `demo-*` users already in the local DB.
- Debounce comes from config section `MatchRecompute` (`Endpoints/MatchesEndpoints.cs:22-27`, defaults in `appsettings.json:10-14`).

## What We're NOT Doing

- No CI workflow. That is test-plan Phase 2.
- No privacy or ranking assertions. Phase 3 covers them; scores are not re-verified in e2e.
- No `data-testid` attributes and no other production-code changes.
- No test that an already-open `/matches` updates itself. This is out of scope for S-06 by design.
- No user cleanup endpoint. Isolation comes from unique emails and names per run.
- No e2e against production.

## Implementation Approach

Use `/10x-e2e-setup` for the runner, config and seed test, then `/10x-e2e` for the critical-path risk. The test drives only the user under test through the browser. The partner is created through the public API with Playwright's `request` context, following the `scripts/demo-accounts.mjs:110-129` sequence. Assertions wait on visible outcomes (role/label locators) and never sleep.

## Critical Implementation Details

- **Timing & lifecycle:** The e2e API must run with `MatchRecompute__QuietPeriod=00:00:05` and `MatchRecompute__MaxDelay=00:00:15`. With those values, opening `/matches` right after "Dodaj" reliably shows the pending state, and the recompute still lands within the SPA's 30 s poll window.
  - Because of this, the API webServer must NOT reuse a developer's already-running `dotnet run`, which has default timings. Set `reuseExistingServer: false` for the API, and stop local `dotnet run` before `npm run e2e`.
  - The migrate step (`dotnet run --launch-profile http -- --migrate`) must finish before the API server starts. Chain both in one webServer command.

## Phase 1: Playwright setup + seed test

### Overview

Install and configure Playwright in `ClientApp/`, start API and SPA from the config, and green one seed test for the auth guard (risk #4 at guard level). Driven by `/10x-e2e-setup`.

### Changes Required:

#### 1. Dependency and script

**File**: `ClientApp/package.json`

**Intent**: Add `@playwright/test` as a dev dependency and an `e2e` script, so the suite runs with one command.

**Contract**: `npm run e2e` → `playwright test`; Chromium browser installed via `npx playwright install chromium`.

#### 2. Playwright config

**File**: `ClientApp/playwright.config.ts` (new)

**Intent**: Define how e2e starts the app and where tests live.

**Contract**:
- `testDir: ./e2e`, `baseURL: http://localhost:4200`.
- Chromium only; traces and screenshots on failure; `workers: 1`, because both tests share one DB and one in-process queue.
- Two `webServer` entries:
  - **API:** cwd repo root, `dotnet run --launch-profile http -- --migrate && dotnet run --launch-profile http`, env `MatchRecompute__QuietPeriod=00:00:05` and `MatchRecompute__MaxDelay=00:00:15`, url `http://localhost:5069/health`, `reuseExistingServer: false`, timeout ≥ 120 s.
  - **SPA:** `npm start`, url `http://localhost:4200`, `reuseExistingServer: true`, timeout ≥ 120 s.
- `/10x-e2e-setup` may add a setup project with `storageState` if it fits. The critical-path test registers its own fresh user, so storage state is optional.

#### 3. Seed test

**File**: `ClientApp/e2e/auth-guard.spec.ts` (new)

**Intent**: Prove the guard round-trip in a real browser.

**Contract**:
1. A partner-style user is created via API.
2. A logged-out page goes to `/matches` and lands on `/login` with `returnUrl`.
3. Log in through the form ("Email", "Hasło", "Zaloguj się"). The page lands on `/matches` (heading "Dopasowania").

#### 4. Stack record and ignores

**File**: `context/foundation/test-stack.md` (new, written by `/10x-e2e-setup`), `ClientApp/.gitignore`

**Intent**: Record the e2e setup decisions. Keep `test-results/` and `playwright-report/` out of git.

**Contract**: test-stack.md names the runner, ports, startup chain, timing env and run command.

### Success Criteria:

#### Automated Verification:

- `cd ClientApp && npm run e2e -- auth-guard` passes (with docker compose DB running, no local `dotnet run`)
- SPA unit tests still pass: `cd ClientApp && npx ng test --watch=false`
- Backend tests still pass: `dotnet test tests/MovieDate.Tests`

#### Manual Verification:

- `npx playwright show-report` shows the seed test with a readable trace

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Critical-path test

### Overview

Write the risk #1 + #4 test with `/10x-e2e`: plan, generate, review, verify.

### Changes Required:

#### 1. Partner fixture

**File**: `ClientApp/e2e/support/partner.ts` (new)

**Intent**: Create a matchable partner through the public API, so the browser test always has a strong candidate.

**Contract**:
- `createPartner(request, { city, movieTitle, rating })` returns `{ email, displayName }`.
- Unique `e2e-<uuid>@moviedate.test` and display name `E2E Partner <short id>`.
- Uses register → `login?useCookies=true` → `PUT /api/profile/display-name` → `PUT /api/profile/city` (id from `GET /api/cities?q=`) → `POST /api/watched` (movie id from `GET /api/movies?q=`).
- Throws on any non-2xx status.

#### 2. Critical-path spec

**File**: `ClientApp/e2e/critical-path.spec.ts` (new)

**Intent**: Prove a fresh user completes the primary path in a browser and that `/matches` tells them an update is coming and then shows the partner without a click.

**Contract**:
- **Setup:** partner created via the fixture in Kraków with Casablanca rated 9.
- **Browser steps:**
  1. Register with a unique email via `/register`.
  2. Expect `/`.
  3. On `/profile`, set name "E2E User <id>" and save.
  4. Choose city "Kraków (małopolskie)" from the autocomplete and save; expect "Twoje miasto:" to show Kraków.
  5. On `/movies`, search "Casablanca", pick the option, choose rating "8" in the form combobox, click "Dodaj", and expect Casablanca in the list.
  6. Immediately navigate to `/matches`.
- **Assertions:**
  - `getByRole('status')` containing "Trwa aktualizacja dopasowań" is visible.
  - Then a list item containing the partner's display name and "Wspólne ulubione" is visible.
  - Then the pending status is hidden.
  - No button is clicked on `/matches`.
- **Behavior asserted:** the S-06 promise: the update is announced and arrives by itself.
- **Regression caught:** broken status polling or list reload; broken auth/guard/profile/rating flow in the browser.
- **Research source:** `research.md` §Risk #1 and §Risk #4.
- **Edge case covered:** the user's own data was written moments before opening `/matches`, so pending is observed at load.
- **Anti-patterns avoided:**
  - No `waitForTimeout`.
  - No API shortcut for the user under test.
  - No score assertion, so there is no oracle copied from production.
  - No CSS-class selectors.

### Success Criteria:

#### Automated Verification:

- `cd ClientApp && npm run e2e` passes (both specs)
- Break-check:
  - Remove the list reload when pending clears in `ClientApp/src/app/matches/matches.component.ts`.
  - Then `npm run e2e -- critical-path` fails.
  - Restore the file.
- SPA unit tests still pass: `cd ClientApp && npx ng test --watch=false`

#### Manual Verification:

- Trace of the critical-path run shows the pending indicator frame and the partner row without any click on `/matches`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Close out the test plan

### Overview

Feed what shipped back into `context/foundation/test-plan.md`.

### Changes Required:

#### 1. Cookbook and status

**File**: `context/foundation/test-plan.md`

**Intent**:
- Replace the §6.3 placeholder with the e2e recipe: location, naming, partner fixture, run command, timing env, and the "write then open the page" rule.
- Backport the research correction to §2 risk #1 wording and its guidance row: the list arrives by itself when `/matches` is opened after a write; an already-open page is not refreshed.
- Set §3 Phase 1 to `complete`, update the e2e row in §4 and the e2e gate in §5 to "required (local)", and bump the dates.

**Contract**: Sections §2, §3, §4, §5, §6.3 and §8 only. No file anchors in §2 (principle #3).

### Success Criteria:

#### Automated Verification:

- `grep -n "complete" context/foundation/test-plan.md` shows Phase 1 row `complete`
- `grep -n "TBD — see §3 Phase 1" context/foundation/test-plan.md` returns nothing

#### Manual Verification:

- §6.3 reads as a usable recipe for adding the next e2e test

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- None added. Existing SPA unit tests must stay green.

### Integration Tests:

- None added. Existing backend recompute tests remain the server-side oracle.

### Manual Testing Steps:

1. `docker compose up -d`, stop any local `dotnet run`, then `cd ClientApp && npm run e2e`.
2. `npx playwright show-report` and open the critical-path trace; confirm the pending frame and the partner row.

## Performance Considerations

The e2e run takes ~1-2 minutes including server start. The 5 s quiet period adds ~5 s to the critical-path test.

## Migration Notes

Not applicable. There are no schema or data changes. Test users accumulate in the local DB with the `e2e-` prefix and can be removed with the `scripts/cleanup-test-users.sql` pattern if needed.

## References

- Related research: `context/changes/testing-critical-path-e2e/research.md`
- Test plan: `context/foundation/test-plan.md` (risks #1, #4; Phase 1)
- Partner seeding pattern: `scripts/demo-accounts.mjs:110-129`
- Server-side coverage: `tests/MovieDate.Tests/MatchRecomputeTests.cs:63-74`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Playwright setup + seed test

#### Automated

- [x] 1.1 `npm run e2e -- auth-guard` passes
- [x] 1.2 SPA unit tests still pass
- [x] 1.3 Backend tests still pass

#### Manual

- [ ] 1.4 Seed test report shows a readable trace

### Phase 2: Critical-path test

#### Automated

- [x] 2.1 `npm run e2e` passes (both specs)
- [x] 2.2 Break-check: removing the list reload makes critical-path red
- [x] 2.3 SPA unit tests still pass

#### Manual

- [ ] 2.4 Trace shows the pending indicator and the partner row without a click

### Phase 3: Close out the test plan

#### Automated

- [ ] 3.1 Test plan §3 Phase 1 row is `complete`
- [ ] 3.2 No "TBD — see §3 Phase 1" placeholder remains

#### Manual

- [ ] 3.3 §6.3 reads as a usable e2e recipe
