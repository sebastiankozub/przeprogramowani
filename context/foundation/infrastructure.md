---
project: movie-date
researched_at: 2026-11-01
recommended_platform: Fly.io
runner_up: Railway
context_type: mvp
tech_stack:
  language: C# / .NET
  framework: ASP.NET Core Web API (minimal API)
  runtime: net10.0
---

## Recommendation

**Deploy on Fly.io.**

Fly.io is the only researched platform with an official, auto-detecting `.NET` deploy guide (`flyctl launch` detects the project and scaffolds a Dockerfile + `fly.toml`), it already carries the strongest MVP fit on the interview constraints (persistent background worker via process groups, GA), and it's the choice already recorded in `tech-stack.md` from the stack-selection lesson — switching now would cost more time than the gap to the runner-up justifies. The anti-bias cross-check surfaced real operational risks (no one-command rollback, an expensive managed Postgres tier, an easy-to-miss always-on config for the worker) that are noted below and in the risk register rather than used to override the pick.

## Platform Comparison

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / integration |
|---|---|---|---|---|---|
| Fly.io | Partial | Pass | Pass | Partial | Partial |
| Railway | Partial | Pass | Pass | Partial | Partial |
| Render | Partial | Pass | Pass | Pass | Partial |
| Azure App Service | Pass | Pass | Pass | Pass (gated) | Partial | 

Azure App Service was researched but dropped before shortlisting: `.NET 10` shows as **Preview** on App Service's own runtime picker (regional rollout still in progress as of 2026-11-01), and the "Always On" setting required for a reliable continuous background worker is only available on Basic (B1)+ — not on the free F1 tier. Both are direct conflicts with this project's exact stack (`net10.0`) and MVP budget expectations, and the developer has no existing familiarity with Azure (interview Q3).

### Shortlisted Platforms

#### 1. Fly.io (Recommended)

Firecracker micro-VM platform, runs arbitrary Docker containers — no restricted runtime list. Official `.NET` guide: `flyctl launch` auto-detects a .NET project and generates a working Dockerfile + `fly.toml`. Background processes run as named process groups in `fly.toml` (`[processes]`), confirmed GA — only groups listed under `[[services]]` receive inbound traffic, so a `worker` group can run headless alongside `web`. Docs are public MDX on GitHub (agent-readable). GitHub Actions integration (`superfly/flyctl-actions`) is mature and GA.

Weak points: no dedicated rollback command (`fly releases --image` + manual redeploy instead); Fly Managed Postgres starts at $38/month, expensive for a solo MVP — the cheaper `fly postgres create` path is self-managed/legacy (no automatic backups); `fly mcp` is explicitly marked experimental; no official Dockerfile template for net8+/net10, only dated community examples (.NET 6-era).

#### 2. Railway

Also Docker-based (Railway's own buildpack, Railpack, explicitly does not support .NET — a Dockerfile is mandatory). Background worker is a clean, confirmed-GA pattern: add it as a second service in the project with no cron schedule, and it runs continuously. Railway ships an **official remote MCP server** (`mcp.railway.com`) with a dedicated multi-step debugging tool and confirmation gates on destructive actions — more concrete than Fly's "experimental" label, though its own release status isn't explicitly stated either. Postgres is GA and attaches via simple project-internal networking, cheaper than Fly's MPG floor.

Gap vs. the recommendation: rollback to an arbitrary prior deploy is dashboard-only — not available from the CLI at all (only redeploy-latest/restart are scriptable). Railway's own sample Dockerfiles use `.NET 9` image tags; `net10.0` would require an untested tag swap. No existing `tech-stack.md` commitment to Railway, so switching has a real (if small) cost.

#### 3. Render

Strongest documentation and automation story of the three: official `llms.txt` + per-page `.md` endpoints, and a dedicated REST API plus Deploy Hooks for scripted deploys — the most complete "stable deployment API" of any researched platform. Ships its own official, open-source hosted MCP server for Claude Code/Codex/Cursor (status not labeled beta, but not confirmed GA either).

Biggest gap for this project: **no native .NET support at all** — Docker-only, with no official guide, just a community forum warning about pinning base-image tags to avoid a runtime-mismatch failure. **Background Workers have no free tier** — $7/month minimum, with Postgres and the web service pushing a realistic always-on setup to ~$20/month, the highest of the three.

## Anti-Bias Cross-Check: Fly.io

### Devil's Advocate — Weaknesses

1. No dedicated rollback command — recovering from a bad deploy means finding the right prior image via `fly releases --image` and redeploying it manually, a slower and more error-prone path than a single `rollback` verb, especially under incident pressure.
2. Fly Managed Postgres floors at $38/month — several times the cost of the rest of the stack combined. The cheaper self-managed `fly postgres create` path exists but is a legacy, unmanaged service: no automatic backups or HA, meaning the solo developer inherits database operations with zero slack in the timeline to do them well.
3. No current official Dockerfile template for `net8+`/`net10` — the official guide is thin and doesn't cover production hardening (health checks, graceful shutdown, GC tuning). Community examples are stale (.NET 6-era), so the multi-stage image has to be assembled and tested from scratch under deadline pressure.
4. `fly mcp` is explicitly labeled experimental — any agent-driven ops workflow built around it today risks breaking or changing shape before the project ships.
5. The docs don't clearly state whether a non-service process group (the `worker`) is always-on by default — the guidance is to "confirm with `min_machines_running`" rather than a documented guarantee. If left at a default that allows scale-to-zero, the async recompute requirement (FR-005's core guarantee) can silently stop working with no visible error.

### Pre-Mortem — How This Could Fail

Week 1, `flyctl launch` auto-detected .NET and the first deploy went smoothly. By week 2, the `worker` process group had silently scaled to zero overnight — `min_machines_running` was never set explicitly, and the generated `fly.toml` didn't flag it as a decision to make. Match recompute quietly stopped for two days before anyone noticed, because nothing errored — it just stopped updating, exactly the failure mode FR-005's "append-only, always-current" guarantee was meant to prevent. Separately, a bad deploy shipped a null-reference bug in a new endpoint; "rollback" turned into a ten-minute scramble cross-referencing opaque image digests from `fly releases --image` against git log to find the last-known-good build, with the API down the whole time. And because the $38/month Postgres floor felt absurd for an app with a handful of users, the team had switched to self-managed `fly postgres create` to save money — only to discover, after a bad migration script, that there were no automatic backups and the match history was gone for good. In hindsight: the team under-weighted whether the "always-on" guarantee actually held without extra configuration, and trusted an auto-generated Dockerfile and `fly.toml` they never read line by line.

### Unknown Unknowns

- Fly's Machines can be configured to auto-stop/auto-start on the `web` service — a cold request after idle time can add latency the project's NFR (<2s p95 for viewing matches) may not tolerate, depending on what the auto-generated `[http_service]` block in `fly.toml` actually sets.
- Billing is per-second Machine runtime, not a flat monthly fee — continuous web + worker Machines realistically cost ~$6-12/month, higher than the "near-zero" estimate used when Fly was chosen over Azure's F1 tier back in the stack-selection lesson.
- Fly can replicate an app across multiple regions by default — for a single-region MVP (interview Q4: single region is fine), an unpinned region config could silently multiply Machine count and cost.
- `FLY_API_TOKEN` used in GitHub Actions is commonly account/org-wide unless deliberately scoped to a single app — a leaked CI secret has a wider blast radius than first-time setup usually assumes.
- Fly's proxy expects specific health-check wiring (`internal_port` + optional `[[services.http_checks]]`) that isn't .NET-specific — ASP.NET Core's own health-check middleware has to be deliberately connected to match what Fly's proxy probes, and the thin official `.NET` guide doesn't cover this.

## Operational Story

- **Preview deploys**: Fly does not provide automatic per-PR preview environments out of the box (unlike Cloudflare Pages or Render). A preview environment would need to be built manually (e.g. a separate Fly app per branch) — not assumed as free infrastructure for this MVP.
- **Secrets**: `fly secrets set KEY=value` stores env vars encrypted at the app level, injected into Machines at runtime — never committed to the repo. CI secrets (`FLY_API_TOKEN`) live in GitHub Actions repo/environment secrets, not in `fly.toml`.
- **Rollback**: no single command. Run `fly releases --image` to find a prior known-good image, then `fly deploy --image <that-image> --strategy immediate`. Budget several minutes, not seconds, and document the exact command sequence in the deploy plan so it isn't improvised during an incident.
- **Approval**: publishing to production (`fly deploy`) and rotating secrets (`fly secrets set/unset`) are human-run commands for this MVP; the agent can propose the command but a human executes anything that touches the live app or its secrets, consistent with the Lesson 3 permission model (`ask` tier for anything outward-facing).
- **Logs**: `fly logs` streams continuously by default (`--no-tail` to disable) — read-only, no dashboard required. This satisfies the agent-operability requirement without needing the experimental `fly mcp` tooling.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Background worker silently scales to zero, async recompute (FR-005) stops without error | Devil's advocate / Pre-mortem | M | H | Explicitly set `min_machines_running = 1` for the worker process group in `fly.toml`; do not rely on the auto-generated default. |
| No automatic backups if self-managed Postgres is chosen to avoid the $38/mo MPG floor | Devil's advocate / Pre-mortem | M | H | Either accept the MPG cost, or if self-managing, set up a scripted `pg_dump` backup on a schedule before any real user data exists — write this into the deploy plan, not left as a someday-task. |
| Rollback requires manually finding the right image digest under time pressure | Devil's advocate / Pre-mortem | M | M | Document the exact `fly releases --image` + `fly deploy --image ...` sequence in `context/deployment/deploy-plan.md` ahead of time, including how to identify "known-good" (e.g. tag releases in git). |
| `net10.0` has no official Fly Dockerfile template; community examples are stale (.NET 6-era) | Research finding | H | M | Write and test the multi-stage Dockerfile early (during the deploy-plan step), not as a last-minute blocker before the deadline. |
| Cold-start latency on the `web` service after idle could breach the <2s NFR | Unknown unknowns | M | M | Check and, if needed, override the `[http_service]` autostop/autostart settings Fly generates; consider `min_machines_running = 1` for `web` too if cost allows. |
| Actual hosting cost (~$6-12/mo web+worker, more with managed Postgres) is higher than the "near-zero" assumption from the stack-selection lesson | Unknown unknowns | H | L | Budget $10-50/month depending on the Postgres decision; not a blocker at this scale, but don't carry forward the earlier "near-zero" assumption unchallenged. |
| `fly mcp` is experimental and may change before the project ships | Devil's advocate | L | L | Don't build the deploy workflow's critical path around `fly mcp`; use `flyctl` directly (GA) for anything that must keep working. |

## Getting Started

1. Create a Fly.io account and install `flyctl` (the CLI): `curl -L https://fly.io/install.sh | sh` (or the Windows/PowerShell installer from the official docs — confirm the current install command at `fly.io/docs/flyctl/install` since this changes between releases).
2. `fly auth login` — authenticates the CLI against the Fly.io account.
3. From the project root, `fly launch` — detects the ASP.NET Core project and scaffolds a starting `Dockerfile` + `fly.toml`. **Review and rewrite the generated Dockerfile for `net10.0`** — the official guide and community examples are not current for this target framework; don't deploy the auto-generated file without inspection.
4. In `fly.toml`, explicitly add a `worker` process group for the async match-recompute job once it exists in code, and set `min_machines_running = 1` on it — do not leave this at the generated default (see Risk Register).
5. Decide the Postgres path before first deploy: `fly postgres create` (cheap, self-managed, no automatic backups — write a backup script first) vs. Fly Managed Postgres (from $38/mo, managed). This decision belongs in `context/deployment/deploy-plan.md`, not improvised at deploy time.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration (beyond the generated-vs-rewritten note above)
- CI/CD pipeline setup (GitHub Actions wiring happens in the deploy-plan step)
- Production-scale architecture (multi-region, HA, DR)
