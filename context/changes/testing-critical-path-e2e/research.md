---
date: 2026-11-03T12:51:54+01:00
researcher: Claude (Opus 5.5) for Seb Ko
git_commit: fa3ef4a
branch: main
repository: przeprogramowani (MovieDate)
topic: "Ground test-plan rollout Phase 1 — critical-path browser coverage (risks #1, #4)"
tags: [research, e2e, playwright, matches, auth, spa]
status: complete
last_updated: 2026-11-03
last_updated_by: Claude (Opus 5.5)
---

# Research: Ground test-plan rollout Phase 1 — critical-path browser coverage

**Date**: 2026-11-03T12:51:54+01:00
**Researcher**: Claude (Opus 5.5) for Seb Ko
**Git Commit**: fa3ef4a
**Branch**: main
**Repository**: przeprogramowani (MovieDate)

## Research Question

For risks #1 (match list does not update within ~10 s after a write, without a click) and #4 (primary path breaks in a real browser while API tests stay green) from `context/foundation/test-plan.md`: where does the failure path run, what already covers it, what is the cheapest useful layer, and what does an e2e run need (serving, auth, timings, seed data, isolation)?

## Summary

- **Risk #1 is real but half-covered.** The server side (write → stamp → debounced worker → both users' lists) is covered by 7 integration tests in `tests/MovieDate.Tests/MatchRecomputeTests.cs` (e.g. `Rating_a_shared_movie_puts_both_users_on_each_others_lists_without_a_recompute_call`, lines 63-74). What no test covers is the SPA side: `/matches` reading `/api/matches/status`, showing the pending indicator, polling every 2 s and reloading the list (`ClientApp/src/app/matches/matches.component.ts:47-64, 104-130`). Only an e2e test exercises that join.
- **Guidance correction for #1:** the SPA checks `pending` only when `/matches` loads (`matches.component.ts:47-64`); a write made while `/matches` is already open does not restart polling. The provable behaviour is therefore "write, then open `/matches` → the list arrives by itself without a click", not "an open page updates". This matches S-06's accepted scope (`context/archive/2026-11-03-async-match-recompute/plan-brief.md`, Out of scope).
- **Risk #4 is real and uncovered.** No browser-level test exists; `@playwright/test` is not a dependency and there is no Playwright config (search depth 3 under `ClientApp/`). The SPA has its own guard, interceptor and cookie session that API tests bypass.
- **Cheapest useful layer:** one Playwright e2e test for the whole path; partner data seeded through the public API (not through the UI), so only the user under test drives the browser.
- **Serving:** `dotnet run` alone serves no SPA locally — `wwwroot` is filled only in the Dockerfile (`Dockerfile:9,17`). Use `ng serve` (port 4200, proxy `/api` → `http://localhost:5069`, `ClientApp/proxy.conf.json:2-4`) plus `dotnet run --launch-profile http`. Schema and catalog seeding run only via `dotnet run -- --migrate` (`Program.cs:19-23`, `Data/MigrationRunner.cs:26-34`).
- **Timing:** debounce defaults QuietPeriod 2 s / MaxDelay 5 s / PollInterval 250 ms (`appsettings.json:10-14`), overridable through standard configuration (env `MatchRecompute__QuietPeriod`, etc.; binding at `Endpoints/MatchesEndpoints.cs:22-27`, inferred from framework defaults, not run). To observe the pending indicator reliably, the e2e server should run with a longer QuietPeriod (e.g. 4-5 s); the list assertion must wait for a visible outcome, never sleep.

## Detailed Findings

### Risk #1 — write → background recompute → `/matches`

- Pending rule: profile complete AND (never computed OR requested after last compute) (`Services/MatchService.cs:102-125`); profile complete = display name AND city set (`Services/MatchService.cs:112`).
- Every successful movie/watchlist/city/name write stamps `MatchesRequestedAt` and enqueues (`Services/MatchRecomputeTrigger.cs:13-24`); a write re-extends the quiet period (`Services/MatchRecomputeQueue.cs:33-42`); the user becomes due after QuietPeriod or MaxDelay (`Services/MatchRecomputeQueue.cs:53-69`).
- SPA: `ngOnInit` loads status and list in parallel (`matches.component.ts:47-50`); if `pending`, it polls status every `STATUS_POLL_INTERVAL_MS = 2000` up to `STATUS_POLL_TIMEOUT_MS = 30000` (`matches.component.ts:11,13,89`); when not pending it reloads `GET /api/matches` (`matches.component.ts:104-130`).
- Indicator: `<div class="matches-status" role="status">` with `mat-progress-bar aria-label="Aktualizacja dopasowań"` and text "Trwa aktualizacja dopasowań…" (U+2026) (`matches.component.html:4-9`).
- Existing coverage (server side only): `MatchRecomputeTests.cs` — shared-movie rating puts both users on each other's lists without POST /recompute (63-74); all 8 triggers make the user pending until the worker runs (76-126); failed writes do not request (129-161); burst → one recompute (164-187); incomplete profile never pending (190-212); never-computed user healed by status (215-239). Fixture shortens timings to 300 ms / 2 s / 50 ms (`tests/MovieDate.Tests/PostgresAppFixture.cs:19-21`).

### Risk #4 — primary path in a browser

- Routes: `login`, `register` public; `''`, `movies`, `watchlist`, `matches`, `profile` behind `authGuard` (`ClientApp/src/app/app.routes.ts:8-35`). Guard calls `GET /api/auth/me` and redirects to `/login?returnUrl=…` (`auth/auth.guard.ts:6-10`).
- Register auto-logs-in and navigates to `/` (`auth/auth.service.ts:84-95`, `auth/register.component.ts:35`). Session is the Identity cookie from `POST /api/auth/login?useCookies=true` (`auth/auth.service.ts:72`).
- Cookie: HttpOnly, SameSite=Strict, `SecurePolicy=SameAsRequest` in Development (`Endpoints/AuthEndpoints.cs:35-42`) — plain `http://localhost` works only in Development. No email confirmation; password ≥ 8 chars, no complexity (`Endpoints/AuthEndpoints.cs:21-29`).
- 401 interceptor sends to `/login?returnUrl=` for non-auth `/api/*` calls (`auth/auth.interceptor.ts:8,19-28`).
- Profile: name input "Twoja nazwa" + button "Zapisz nazwę"; city Material autocomplete "Wybierz miasto" (≥ 2 chars, 300 ms debounce) where an option **must be clicked** before "Zapisz" enables (`profile/profile.component.html:21-44`, `profile.component.ts:46-49,88-90`). Success shows "Twoja nazwa: …" / "Twoje miasto: …" (`profile.component.html:7,12`).
- Movies: "Szukaj filmu" autocomplete (option text "Title (OriginalTitle, year)"), `mat-select` "Ocena" 1-10, button "Dodaj" (`movies/movies.component.html:7-14`, `movie-search.component.ts:15-21,36`). After adding, a second "Ocena" combobox appears in the list row (`movies.component.html:25-58`).

### Selectors

- No `data-testid` anywhere in `ClientApp/src/app`. Role/label locators cover the path: `getByLabel('Email' | 'Hasło' | 'Twoja nazwa' | 'Wybierz miasto' | 'Szukaj filmu')`, `getByRole('combobox', {name: 'Ocena'})`, `getByRole('option', …)`, buttons by name ("Zapisz" needs `exact: true` because of "Zapisz nazwę"), `getByRole('status')` with text, score via `aria-label="Dopasowanie N na 100"` (`matches.component.html`). Material label wiring is standard but was not exercised in a browser.

### Seed data and isolation

- A visible candidate needs a second user with name + city; to be independent of other users in the shared compose DB (e.g. `demo-*` accounts), make the pair **strong**: both rate the same movie ≥ 7 (`Services/MatchScoringService.cs:15,58-59`). Strong candidates are always added (up to 20, `Services/MatchService.cs:39,248-253`) and the row is mirrored (`MatchService.cs:288-320`).
- No delete-user endpoint exists (only `DELETE /api/watched/{id}`, `/api/watchlist/{id}`). Isolation = unique emails per run (`e2e-<uuid>@moviedate.test`, same idea as `tests/MovieDate.Tests/TestUsers.cs:23`) plus a unique partner display name per run; optional SQL cleanup by email prefix like `scripts/cleanup-test-users.sql`.
- Partner seeding pattern: `scripts/demo-accounts.mjs:110-129` (register → login?useCookies → PUT display-name → PUT city via `GET /api/cities?q=` → POST watched via `GET /api/movies?q=`). In Playwright, `request.newContext()` keeps the cookie automatically.

## Code References

- `ClientApp/src/app/matches/matches.component.ts:11-13,47-64,89,104-130` — polling constants, load-time pending check, poll/reload loop
- `ClientApp/src/app/matches/matches.component.html:4-9,35-71` — pending indicator, list items
- `ClientApp/src/app/app.routes.ts:8-35`, `auth/auth.guard.ts:6-10`, `auth/auth.interceptor.ts:8-28` — guards and redirects
- `ClientApp/src/app/profile/profile.component.html:21-44` — name and city forms
- `ClientApp/src/app/movies/movies.component.html:7-14` — rating form
- `ClientApp/proxy.conf.json:2-4` — `/api` → `http://localhost:5069`
- `Program.cs:19-23,32-33,67` — `--migrate` mode, static files, SPA fallback
- `Endpoints/AuthEndpoints.cs:21-42` — password, cookie policy
- `Endpoints/MatchesEndpoints.cs:22-27,54-76` — options binding, status self-heal
- `Services/MatchRecomputeQueue.cs:6-18,33-69`, `appsettings.json:10-14` — debounce
- `Services/MatchService.cs:102-125,205-264`, `Services/MatchScoringService.cs:15,58-59` — pending, eligibility, strong threshold
- `tests/MovieDate.Tests/MatchRecomputeTests.cs:55-239` — existing server-side coverage
- `Dockerfile:9,17` — only place wwwroot gets the SPA

## Architecture Insights

- The SPA and API are developed apart (`ng serve` + proxy) and joined only in the Docker image; an e2e config has to start both, or build the SPA into `wwwroot` first. The two-server variant matches daily development and needs no build changes.
- The recompute's correctness lives in the backend and is well tested; the e2e test's unique value is the browser join (cookie session, guard, polling UI). Its assertions should stay at that level and not re-verify scores.

## Historical Context (from prior changes)

- `context/archive/2026-11-03-async-match-recompute/plan-brief.md` — out of scope: notifying an already-open `/matches` page; refresh on load + polling while pending. Supports the guidance correction above.
- `context/archive/2026-11-03-ranked-match-list/` — strong candidate = shared favorite (≥ 7) or shared watchlist; append-only lists.
- `context/deployment/deploy-plan.md` — production checks of this flow were manual; no automated browser check exists.

## Related Research

- Not applicable (first research under the test rollout).

## Open Questions

- Serving mode for e2e: `ng serve` + `dotnet run` (two webServers) vs. building the SPA into `wwwroot` — a plan decision; the former needs no build change.
- Whether to assert the pending indicator at all (needs a longer QuietPeriod in the e2e server) or only the eventual list — a plan decision; asserting it is what proves "information that recompute is on the way" from S-06.
- Env-var override of `MatchRecompute:*` and Material label wiring are inferred, not executed.
