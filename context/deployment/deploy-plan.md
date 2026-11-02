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
15. `fly tokens create deploy -a moviedate-sk` (app-scoped, not account-wide) → `gh secret set FLY_API_TOKEN --repo sebastiankozub/przeprogramowani`.
16. Committed and pushed (two commits: `L5: first deployment + scafffold angular`, `L5: add GitHub Actions workflow for manual Fly.io deploy`).
17. `gh workflow run deploy.yml` → completed `success`. Phase 5 checks re-run against the public URL, confirmed parity with the manual deploy.

## Files created/modified

**Agent**: `ClientApp/` (new), `Program.cs` (modified), `.dockerignore` (new), `Dockerfile` (new), `fly.toml` (new), `.github/workflows/deploy.yml` (new), this file.

**Human-only actions taken**: `fly auth login`, `fly apps create`, `fly deploy` (x2), `fly tokens create`, `gh secret set`, `gh workflow run`, `git tag`.

## What's still open (for Module 2, not this deployment)

- **Postgres**: ✅ resolved 2026-11-02 by change `prod-postgres-connection` (roadmap F-01). Neon free tier, project `moviedate-sk` (`patient-pond-88799426`), AWS Frankfurt, direct (non-pooled) endpoint. Fly secret `ConnectionStrings__Default` (Npgsql key-value form). Migrations run via `[deploy] release_command = "dotnet movie-date.dll --migrate"`; a failed migration or missing secret aborts the deploy. `/health/db` checks the DB on demand — deliberately NOT a Fly check (would keep Neon awake). Release **v5** (`registry.fly.io/moviedate-sk:deployment-01M6E9X1MYF9AGYT2H46XWQQV2`) is the new known-good image. **Rollback caveat:** rolling back to a pre-database image (v3/v4) with the current `fly.toml` would run `--migrate` on an image that doesn't know the flag — the release machine would start a web server instead of exiting. Roll back pre-DB images only with the `release_command` removed.
- **`worker` process group**: ❌ no longer planned — match recompute runs as an in-process hosted service (`tech-stack.md`, decisions 2026-11-02).
- **Fly trial limit**: the account is on the Fly trial — logs show `Trial machine stopping. To run for longer than 5m0s, add a credit card`. Machines stop after 5 minutes and cold-start on the next request, overriding `min_machines_running = 1`. Add a payment method before relying on NFR-01 (< 2 s) in a demo.
- **Duplicated process args**: ✅ fixed in code 2026-11-02 (impl-review F2) — the Dockerfile now uses `CMD ["dotnet", "movie-date.dll"]` instead of `ENTRYPOINT`, so Fly's process/release commands replace it instead of being appended (release v5 still runs `dotnet movie-date.dll dotnet movie-date.dll [--migrate]`). Verified locally; takes effect on the next deploy — check `fly logs` for `Preparing to run: \`dotnet movie-date.dll --migrate\``.
- **Npgsql log noise**: `Cannot load library libgssapi_krb5.so.2` on startup — Npgsql probes for Kerberos/GSSAPI, which the `aspnet` image lacks. Harmless (password auth); silence by installing `libgssapi-krb5-2` in the runtime stage if desired.
- **Auth (FR-001)**: not implemented, not deployed.

### Critical files

- `C:\Users\sebas\przeprogramowani\Program.cs`
- `C:\Users\sebas\przeprogramowani\movie-date.csproj`
- `C:\Users\sebas\przeprogramowani\fly.toml`
- `C:\Users\sebas\przeprogramowani\Dockerfile`
- `C:\Users\sebas\przeprogramowani\context\foundation\infrastructure.md`
- `C:\Users\sebas\przeprogramowani\context\foundation\tech-stack.md`
