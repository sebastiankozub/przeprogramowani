# Critical-path browser coverage — Plan Brief

> Full plan: `context/changes/testing-critical-path-e2e/plan.md`

## What & Why

Rollout Phase 1 of `context/foundation/test-plan.md`. The test plan protects risks #1 (the match list does not arrive by itself after a write) and #4 (the primary path breaks in a browser while API tests are green). This phase adds the project's first e2e test, which is also the 10xBuilder requirement: "a test set addressing a test-plan risk from the user's perspective".

## Starting Point

- 136 backend integration tests already prove the server side of the recompute.
- 33 SPA unit tests exist.
- There is no browser test, no Playwright, and locally no built SPA in `wwwroot`. Development serves the SPA with `ng serve` and an `/api` proxy to `:5069`.

## Desired End State

`npm run e2e` in `ClientApp/` starts API and SPA and passes two tests:

- **Guard round-trip:** a logged-out user goes to `/matches`, lands on login, and returns to `/matches` after logging in.
- **Critical path:** register → name + city → rate Casablanca → `/matches` shows "Trwa aktualizacja dopasowań…" and then the partner, with no click.

A deliberate break of the SPA reload turns the second test red. The test plan's cookbook §6.3 explains how to add the next e2e test.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Serving | `ng serve` :4200 + `dotnet run` :5069 as two Playwright webServers | Matches daily development; no build or csproj change; reusable in CI. |
| Pending indicator | Asserted, with e2e API QuietPeriod 5 s / MaxDelay 15 s | Proves the S-06 promise that the user is told an update is coming, not only that the list arrives. |
| Partner data | Created through the public API per run, strong pair (same movie ≥ 7) | Guarantees a visible candidate regardless of other users in the local DB. |
| Location | `ClientApp/e2e/`, Playwright in `ClientApp/package.json` | Lives with the SPA and its npm toolchain. |
| Selectors | Role/label locators, no `data-testid` | The UI already exposes enough labels; no production change needed. |
| What is proven for #1 | Write, then open `/matches` → list arrives by itself | The SPA checks pending on load; refreshing an open page is out of S-06 scope. |

## Scope

**In scope:**
- Playwright dependency, config and `npm run e2e`.
- Guard seed test.
- Partner fixture.
- Critical-path test with a break-check.
- `test-stack.md`.
- Test-plan §2/§3/§4/§5/§6.3 updates.

**Out of scope:** CI (Phase 2), privacy/ranking contract tests (Phase 3), production-code changes, e2e against prod, user cleanup endpoint.

## Architecture / Approach

1. Playwright starts two servers: the API (migrate + run, slower debounce via env) and the SPA (`ng serve`).
2. The test creates a partner through the API using a `request` context.
3. The browser drives only the user under test through real forms.
4. Assertions wait on visible outcomes. There are no sleeps and no score assertions.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Playwright setup + seed test | Runner, two-server config, guard test, test-stack.md | `ng serve` / migrate start-up time; port 5069 already taken by a dev server |
| 2. Critical-path test | Partner fixture + full-path spec + break-check | Material autocomplete/select interaction; timing of the pending frame |
| 3. Close out the test plan | Cookbook §6.3, risk #1 backport, Phase 1 `complete` | — |

**Prerequisites:** Docker (compose DB), Node 22, .NET 10 SDK; no local `dotnet run` during e2e.
**Estimated effort:** ~2-3 hours across 3 phases (certification deadline 2026-11-04).

## Open Risks & Assumptions

- Env override of `MatchRecompute:*` follows standard ASP.NET configuration. This is inferred, and the first run confirms it.
- Material label wiring for `getByLabel` is standard but not yet exercised in a browser.
- The local compose DB keeps `e2e-*` users between runs. This is harmless, and the SQL cleanup pattern exists.

## Success Criteria (Summary)

- `npm run e2e` is green, and the critical-path test turns red when the SPA reload is removed.
- The trace shows the pending indicator and then the partner appearing without any click.
- `test-plan.md` §3 Phase 1 is `complete`, with a usable e2e recipe in §6.3.
