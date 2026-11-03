# MovieDate — First Production Deployment Plan (Fly.io)

**Status: executed and verified, 2026-11-02.** Live at https://moviedate-sk.fly.dev — both manual (`flyctl`) and CI (`workflow_dispatch`) deploy paths confirmed working, verification checks green on both.

## Execution log (deltas from the original plan below)

- **App name**: `moviedate` was taken — reserved as **`moviedate-sk`** instead. `fly.toml`'s `app` field and `.github/workflows/deploy.yml`'s `--app` flag both use this real name, not the placeholder.
- **Region**: `waw` (Warsaw) turned out to be **deprecated on Fly** — not caught by research, only surfaced at deploy time. Switched to **`fra`** (Frankfurt) as the closest non-deprecated region to Poland.
- **`fly.toml` fix mid-deploy**: flyctl refused the config with "Service has no processes set but app has 1 processes defined" — once `[processes]` is declared at all, `[http_service]` must explicitly declare `processes = ["web"]`. This was originally left as a Module-2-only TODO comment; it's now a required, present setting. The Module 2 note stays: when a `worker` process group is added, it must NOT be listed here (keeps inbound traffic/health checks off the worker).
- **Known-good release reference** (for rollback): `registry.fly.io/moviedate-sk:deployment-01M6E3PX51V2BQ0HBASEDQ2YQ2` (Fly release `v3`, the first successful deploy after the region fix — Fly's own version numbering isn't "our" v1, don't confuse the two). Rollback command: `fly deploy --image registry.fly.io/moviedate-sk:deployment-01M6E3PX51V2BQ0HBASEDQ2YQ2 --strategy immediate --app moviedate-sk`.
- **Git tag**: `deploy-2026-11-02-v1`, pushed to `origin`.
- **GitHub Actions**: `FLY_API_TOKEN` set as a repo secret (scoped to `moviedate-sk` only, via `fly tokens create deploy -a moviedate-sk`), workflow run via `gh workflow run deploy.yml`, completed with status `success`. Post-CI-deploy verification matched the manual deploy exactly (all 4 checks green).
- **All verification checks passed, both after the manual deploy and after the CI deploy**: `/health` → 200 `{"status":"ok"}`, `/weatherforecast` → 200 JSON (5 items), `/` → 200 Angular HTML shell, `/some-nonexistent-route` → 200 (SPA fallback, confirms it doesn't swallow `/health`).

---

## Context

Lesson 5 of the course closes out Module 1 with an end-to-end proof: idea → PRD → stack → scaffold → agent onboarding → **a real deployment on a public URL**. The goal at this stage is deliberately *not* to ship MovieDate's actual features (auth, matching, background recompute) — those are Module 2 work and don't exist in code yet. The goal is to prove the deployment pipeline itself works: the current ASP.NET Core skeleton, plus a minimal Angular shell, served as one app from Fly.io, reachable at a public URL, with a repeatable path to redeploy.

This plan is the direct output of `/10x-infra-research` (→ `context/foundation/infrastructure.md`, which picked Fly.io after a researched comparison against Railway, Render, and Azure App Service, and ran an anti-bias cross-check that surfaced concrete risks) and the Plan Mode step the course calls for, fed by `@context/foundation/infrastructure.md` and `@context/foundation/tech-stack.md`. Every risk item from `infrastructure.md`'s risk register is addressed explicitly below, not silently dropped.

**Scope statement:** this deployment ships the current repo state only — the unmodified `/weatherforecast` scaffold plus a newly scaffolded Angular shell. **Auth (FR-001), the database/Postgres decision, and the async match-recompute background worker (FR-002/003/005) are explicitly out of scope, deferred to Module 2.** The Postgres choice from `infrastructure.md` is deliberately left open here — it becomes a decision for whichever Module 2 step first adds a `DbContext`.

## Locked decisions (not re-litigated here)

- **Platform**: Fly.io (researched + anti-bias-checked in `infrastructure.md`).
- **Single deployment**: Angular build copied into `wwwroot/` at image build time; one Fly app serves both API and SPA — no CORS, no second deployment.
- **Frontend folder**: `ClientApp/` (confirmed naming). Backend stays at repo root (not moved into `backend/`).
- **CI/CD**: GitHub Actions, `manual-promotion` (from `tech-stack.md`) — no auto-deploy on merge.
- **Permission model**: the agent writes code/config; a **human** runs anything that touches the live Fly app, Fly auth, or secrets (consistent with `infrastructure.md`'s Operational Story and the project's Lesson-3 permission policy).

## Judgment calls made

1. **Angular naming**: scaffold folder `ClientApp/`, npm/Angular project name `client-app` (lowercase — Angular project names aren't guaranteed npm-safe with uppercase). Dockerfile pins `ClientApp/dist/client-app/browser` — verified locally (confirmed exact match, no adjustment needed).
2. **CI sequencing**: first deploy was **manual, local `flyctl`, run by the human** — not GitHub Actions. Stacking an unverified Dockerfile *and* an unverified CI pipeline at once would have been the riskier path under a near-deadline. GitHub Actions (Phase 7) was exercised as a *second* deploy, once the app was already known-good — this paid off, since the region/processes issues surfaced during the manual run, not hidden inside a CI log.
3. **`UseHttpsRedirection()`**: restricted to `Development` only. Fly's edge proxy (`force_https = true`) terminates TLS before traffic reaches the container, which only listens on plain HTTP on 8080.
4. **`.dockerignore` excludes `ClientApp/node_modules/`**: a correctness requirement, not just speed — repo is edited on Windows, image builds for Linux; a host-installed `node_modules/` with Windows-native binaries would clobber the Linux ones `npm ci` installs inside the image.
5. **Region**: `fra` (Frankfurt) — see Execution log above for why `waw` wasn't usable.
6. **`min_machines_running = 1` for `web`**: set explicitly (Fly's default allows scale-to-zero) — avoids cold-start latency against the PRD's <2s NFR.

---

## Phase 0 — Local tooling (human) — done

1. Install `flyctl`, `fly auth login` (confirmed as `sebastiankozub@gmail.com`).

## Phase 1 — Scaffold Angular frontend (agent) — done

2. Scaffolded via `npx @angular/cli@latest new client-app --directory=ClientApp --routing --style=css --skip-git --ssr=false --package-manager=npm`.
3. Verified build output: `ClientApp/dist/client-app/browser/index.html` — matched the assumed path exactly.

## Phase 2 — Backend code and container config (agent) — done

4. `Program.cs`: added `UseDefaultFiles()`/`UseStaticFiles()`, a `/health` endpoint before the SPA fallback, moved `UseHttpsRedirection()` into the `Development`-only block, added `app.MapFallbackToFile("index.html")` as the last route registration.
5. `.dockerignore` created (repo root).
6. `Dockerfile` created — 3-stage build (`node:22` → `dotnet/sdk:10.0` → `dotnet/aspnet:10.0`), `EXPOSE 8080`.
7. `fly.toml` created, then fixed mid-deploy (see Execution log: `processes = ["web"]` added, region changed to `fra`).

## Phase 3 — Local Docker verification (agent) — done, all green

8. `docker build` + `docker run` + curl against `/health`, `/weatherforecast`, `/`, and a nonexistent route — all passed before touching the live Fly app.

## Phase 4 — First production deploy (human) — done

9. `fly apps create moviedate-sk` (name `moviedate` was taken).
10. `fly deploy --app moviedate-sk` — first attempt failed (deprecated `waw` region); fixed and redeployed successfully.
11. `fly status` / `fly checks list` confirmed 2 `web` machines in `fra`, health checks passing.

## Phase 5 — End-to-end verification (agent) — done, all green (run twice: after manual deploy, after CI deploy)

12. Public URL checks: `/health` → 200, `/weatherforecast` → 200 JSON, `/` → 200 HTML, nonexistent route → 200 (SPA fallback).

## Phase 6 — Tag the known-good release — done

13. `git tag deploy-2026-11-02-v1`, pushed. Known-good image reference recorded in Execution log above.

**Rollback procedure (reference only):** `fly releases --image --app moviedate-sk` to find a prior release's image reference, then `fly deploy --image <that reference> --strategy immediate --app moviedate-sk`. No single rollback command exists — budget minutes, not seconds.

## Phase 7 — GitHub Actions — done

14. `.github/workflows/deploy.yml` created — `workflow_dispatch` only, matches `manual-promotion`.
15. `fly tokens create deploy -a moviedate-sk` (app-scoped, not account-wide) → `gh secret set FLY_API_TOKEN --repo sebastiankozub/przeprogramowani-MovieDate`.
16. Committed and pushed (two commits: `L5: first deployment + scafffold angular`, `L5: add GitHub Actions workflow for manual Fly.io deploy`).
17. `gh workflow run deploy.yml` → completed `success`. Phase 5 checks re-run against the public URL, confirmed parity with the manual deploy.

## Files created/modified

**Agent**: `ClientApp/` (new), `Program.cs` (modified), `.dockerignore` (new), `Dockerfile` (new), `fly.toml` (new), `.github/workflows/deploy.yml` (new), this file.

**Human-only actions taken**: `fly auth login`, `fly apps create`, `fly deploy` (x2), `fly tokens create`, `gh secret set`, `gh workflow run`, `git tag`.

## What's still open (for Module 2, not this deployment)

- **Postgres**: ✅ resolved 2026-11-02 by change `prod-postgres-connection` (roadmap F-01). Neon free tier, project `moviedate-sk` (`patient-pond-88799426`), AWS Frankfurt, direct (non-pooled) endpoint. Fly secret `ConnectionStrings__Default` (Npgsql key-value form). Migrations run via `[deploy] release_command = "dotnet movie-date.dll --migrate"`; a failed migration or missing secret aborts the deploy. `/health/db` checks the DB on demand — deliberately NOT a Fly check (would keep Neon awake). Release **v5** (`registry.fly.io/moviedate-sk:deployment-01M6E9X1MYF9AGYT2H46XWQQV2`) is the new known-good image. **Rollback caveat:** rolling back to a pre-database image (v3/v4) with the current `fly.toml` would run `--migrate` on an image that doesn't know the flag — the release machine would start a web server instead of exiting. Roll back pre-DB images only with the `release_command` removed.
- **`worker` process group**: ❌ no longer planned — match recompute runs as an in-process hosted service (`tech-stack.md`, decisions 2026-11-02).
- **Fly trial limit**: the account is on the Fly trial — logs show `Trial machine stopping. To run for longer than 5m0s, add a credit card`. Machines stop after 5 minutes and cold-start on the next request, overriding `min_machines_running = 1`. Add a payment method before relying on NFR-01 (< 2 s) in a demo.
- **Duplicated process args**: ✅ fixed in code 2026-11-02 (impl-review F2) — the Dockerfile now uses `CMD ["dotnet", "movie-date.dll"]` instead of `ENTRYPOINT`, so Fly's process/release commands replace it instead of being appended (release v5 still runs `dotnet movie-date.dll dotnet movie-date.dll [--migrate]`). Shipped in release v6: web machines now log `Preparing to run: \`dotnet movie-date.dll\`` (single command). The release machine's own `Preparing to run` line had already rotated out of the log buffer when checked; its migration output (`20261102164639_AddIdentity`, "Database migrations applied successfully.") is present. **Confirmed in release v7** via `fly machine status <release machine> -d`: `init.cmd` is `["dotnet","movie-date.dll","--migrate"]` (no duplicated prefix).
- **Npgsql log noise**: `Cannot load library libgssapi_krb5.so.2` on startup — Npgsql probes for Kerberos/GSSAPI, which the `aspnet` image lacks. Harmless (password auth); silence by installing `libgssapi-krb5-2` in the runtime stage if desired.
- **Auth (FR-001)**: ✅ deployed 2026-11-02 by change `register-and-login` (roadmap S-01) as release **v6** (`registry.fly.io/moviedate-sk:deployment-01M6F2WNX9BHVVX8YBDHAWD78F`), the new known-good image. ASP.NET Core Identity via `MapIdentityApi` under `/api/auth` with an HttpOnly, SameSite=Strict, Secure cookie. Data Protection keys are persisted in Postgres (`DataProtectionKeys` table), so both web machines and restarts share them. Verified on production: register/login 200; `/api/auth/me` 200 on **each** machine via `fly-force-instance-id` (7812d31c3e7548, d8d1499c914258); still 200 on both after `fly machine restart`; logout 204 → `/me` 401; anonymous `/api/auth/me` 401. A throwaway `e2e-*@moviedate.test` account was created by this check. **Rollback caveat:** v5 is schema-compatible (the Identity tables are additive), but rolling back loses auth entirely.
- **Rated movies (FR-002, without async recompute)**: ✅ deployed 2026-11-02 by change `rate-watched-movies` (roadmap S-02) as release **v7** (`registry.fly.io/moviedate-sk:deployment-01M6F6PD1SDMS5R3V1Q75YZ6DZ`), the new known-good image. The release command now also seeds the movie catalog: after `MigrateAsync`, `MovieCatalogSeeder` upserts the embedded `Data/Seed/movies.json` (498 Wikidata films) by `WikidataId`, never deleting; a seed failure fails the release like a migration failure. Verified on production: anonymous `/api/movies` and `/api/watched` 401; search "ojciec"/"godfather" → Ojciec chrzestny I–III; add 201, duplicate 409, rating 11 → 400, update 200, list identical on both machines, delete 204. Another throwaway `e2e-s02-*@moviedate.test` account was created. **Rollback caveat:** v6 is schema-compatible (`Movies`/`WatchedMovies` are additive and unused by v6).
- **EF Core SQL logging floods Fly logs**: the seeder's ~500 `INSERT` statements are logged at Information (`Microsoft.EntityFrameworkCore.Database.Command`) and push everything else out of `fly logs`' 100-line buffer, including the release machine's start lines. ✅ Fixed in v8: `appsettings.json` sets that category to `Warning` (S-02 impl-review F7).
- **S-02 review fixes**: release **v8** (2026-11-02) shipped the `rate-watched-movies` impl-review fixes (FK-violation → 404, SPA list sync, 201 without Location, `q` cap, EF log level); verified by a browser click-through of `/movies`.
- **Want-to-watch list (FR-003)**: ✅ deployed 2026-11-03 by change `want-to-watch-list` (roadmap S-04); **v10** (`registry.fly.io/moviedate-sk:deployment-01M6F9Q831CH8RVZ1A5CZ18YC9`) is the new known-good image (v9 was an intermediate deploy of the same change). Migration `AddWatchlist` adds only `WatchlistItems`. Verified on production: anonymous `/api/watchlist` 401, `/health` ok; browser click-through of `/movies` (after the shared-search refactor) and `/watchlist` by the user; via API: rate 4 → add to watchlist → "Obejrzałem" with 9 → 200, watchlist empty, rating 9 (re-watch overwrites). Another throwaway `e2e-s04-*@moviedate.test` account was created. **Rollback caveat:** v8 is schema-compatible (`WatchlistItems` is additive and unused by v8).
- **S-04 review fixes**: release **v11** shipped the `want-to-watch-list` impl-review fixes (verifySucceeded for a lost commit ack, rollback tests, re-watch fallback, 600 kB bundle warning budget).
- **Home city (FR-004)**: ✅ deployed 2026-11-03 by change `set-home-city` (roadmap S-03) as release **v12** (`registry.fly.io/moviedate-sk:deployment-01M6FX82WZY81Y0XTDJ7VKD123`), the new known-good image. Migration `AddCities` creates `Cities` and adds a nullable `AspNetUsers.CityId` (FK restrict). The release command now also seeds the city catalog after the movies: `CityCatalogSeeder` upserts the embedded `Data/Seed/cities.json` (1,030 Polish localities from Wikidata — towns with city rights plus settlements ≥ 10k) by `WikidataId`, never deleting. Verified on production: anonymous `/api/cities`, `/api/profile` 401; `/health` ok; "war" → Warszawa, Warka, Warta; Kozy present, Kamiennik absent; profile `city: null` → PUT Kraków 200 → profile shows Kraków (małopolskie); no `latitude`/`longitude`/`population` in any response (NFR-04); browser click-through of `/profile` and home by the user. Another throwaway `e2e-s03-*@moviedate.test` account was created. **Rollback caveat:** v11 is schema-compatible (`Cities` and the nullable `CityId` are unused by v11).
- **Ranked match list + async recompute (FR-005, NFR-02)**: ✅ deployed 2026-11-03 by changes `ranked-match-list` (roadmap S-05) and `async-match-recompute` (S-06); the S-06 release is the new known-good image (read its number and image reference with `fly releases --image --app moviedate-sk` — the fly CLI was not available to the agent). Migration `AddMatchRecomputeStamps` only adds nullable `AspNetUsers.MatchesRequestedAt` / `MatchesComputedAt` (confirmed as the latest row of `__EFMigrationsHistory`). Every movie/watchlist/city/display-name write stamps `MatchesRequestedAt` and queues an in-process debounced recompute (2 s quiet, 5 s max); `GET /api/matches/status` reports pending and re-enqueues lost work (self-heal after deploy/restart). Verified on production: anonymous `/api/matches/status` 401, `/health` ok, served bundle has the pending indicator and no recompute button; via API, `mdtest-zp-1` rates Casablanca 9 → write 201 in 226 ms, status pending, cleared after 2.4 s, and `mdtest-zp-2`'s list gained the mirrored candidate with no recompute call; browser click-through by the user. Ten permanent demo accounts `demo-01..10@moviedate.test` (Kraków ×2, Zakopane, Warszawa, Wrocław, Gdańsk, Rzeszów, Białystok, Poznań, Łódź; password = `DEMO_PASSWORD`, not recorded here) were created with `scripts/demo-accounts.mjs`. S-05 check on production: a fresh account (`e2e-s05-*@moviedate.test`, Kraków, 3 ratings) got 9 ranked candidates 3.2 s after its last write, no emails or coordinates in the response, mirror visible on `demo-01`; warm `GET /api/matches` 58–69 ms (NFR-01 < 2 s). The 20 manual-test accounts `mdtest-*@moviedate.test` are still present — remove them with `scripts/cleanup-test-users.sql`. **Rollback caveat:** the S-05 release is schema-compatible (the new columns are nullable and unused by it), but rolling back restores the manual "Szukaj dopasowań" flow. **Known limit:** queued work lives in memory per machine; on the Fly trial machines stop after 5 min, so the first update after a cold start can exceed ~10 s.
- **Friends and first message (FR-006, FR-007)**: ✅ deployed 2026-11-03 by change `first-message-to-friend` (roadmap S-07 + S-08) via PR #2 (merge `8cb5cd9`) and `deploy.yml` run 41707013809; this release is the new known-good image (read its number and image reference with `fly releases --image --app moviedate-sk` — the fly CLI was not available to the agent). Migration `AddFriendsAndMessages` only creates `Friendships` and `Messages` (plus indexes and not-self checks). Verified on production: `/health` 200; anonymous `/api/friends` and `/api/conversations/{id}` 401 (before the deploy they fell through to the SPA shell); browser check by the user: A adds B from /matches, writes, B sees A under "Dodali Cię do znajomych" and replies, A sees the reply after reload; a third account opening the pair's `/friends/<id>` URL gets "Nie możesz pisać z tą osobą". `scripts/demo-accounts.mjs` now creates 20 demo accounts (9–11 ratings each; `DEMO_EMAIL_PREFIX` for a separate set; password not recorded here). The GitHub repo was renamed to `sebastiankozub/przeprogramowani-MovieDate` the same day. **Rollback caveat:** the S-06 release is schema-compatible (the two new tables are unused by it), but rolling back removes friends and messaging.

### Critical files

- `C:\Users\sebas\przeprogramowani\Program.cs`
- `C:\Users\sebas\przeprogramowani\movie-date.csproj`
- `C:\Users\sebas\przeprogramowani\fly.toml`
- `C:\Users\sebas\przeprogramowani\Dockerfile`
- `C:\Users\sebas\przeprogramowani\context\foundation\infrastructure.md`
- `C:\Users\sebas\przeprogramowani\context\foundation\tech-stack.md`
