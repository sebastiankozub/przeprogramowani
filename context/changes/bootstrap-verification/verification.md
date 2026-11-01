---
bootstrapped_at: 2026-09-16T22:49:09Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: movie-date
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: movie-date
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: fly
  ci_provider: github-actions
  ci_default_flow: manual-promotion
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
```

### Why this stack (from hand-off body)

Solo developer shipping a web-app MVP in 1 week (after-hours, hard deadline 2026-11-04) with prior .NET expertise — the recommended default for `(web, dotnet)` is ASP.NET Core webapi, which clears all four agent-friendly gates and is bootstrapper-verified, so scaffolding will be smooth. The PRD names auth (FR-001) and background-job-shaped async recompute (FR-002/003/005) as in-scope; ASP.NET Core doesn't ship these batteries-included the way Django or Rails would, so auth (ASP.NET Core Identity or Keycloak) and the recompute job (e.g. Hangfire or a hosted service) are manual additions after scaffolding. Deployment targets Fly.io, chosen over the starter's own Azure App Service default for its lower cost at this project's small scale — Azure's free F1 tier caps daily compute, while Fly's pay-as-you-go pricing stays near-zero for a low-traffic MVP. The Angular frontend (added separately; not part of this backend-only starter) is built to static files and served from the ASP.NET Core app's `wwwroot`, so the project ships as a single Fly deployment rather than two. CI runs on GitHub Actions with manual promotion after merge rather than auto-deploy, since the user wants an explicit gate before each deploy. No payments, realtime, or AI features are in scope, consistent with the PRD's Non-Goals.

## Pre-scaffold verification

| Signal             | Value                              | Severity | Notes                              |
| ------------------ | ----------------------------------- | -------- | ----------------------------------- |
| npm package        | not run                            | n/a      | language_family is dotnet, not js  |
| GitHub repo        | not run                            | n/a      | docs_url (learn.microsoft.com/aspnet/core) is not a GitHub URL |

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (`Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `movie-date.csproj`, `movie-date.http`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold
**.bootstrap-scaffold cleanup**: deleted

**Note**: `dotnet new webapi -n .bootstrap-scaffold` named the generated `.csproj`/`.http` files and the C# `RootNamespace` after the temp directory name (`_bootstrap_scaffold`). This is a known quirk of dotnet's `-n` flag — it names project artifacts, not just the directory. Before moving files up, these were renamed/rewritten to match `project_name` from the hand-off: `.bootstrap-scaffold.csproj` → `movie-date.csproj`, `.bootstrap-scaffold.http` → `movie-date.http`, `RootNamespace` → `MovieDate`, and the `.http` file's host-address variable → `MovieDate_HostAddress`. This cleanup is outside the scaffold-merge conflict matrix (which governs path collisions, not file content) but was necessary to avoid the temp-directory name leaking into the final project.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished — 0 findings total, no packages flagged

Audit ran after an implicit `dotnet restore` (triggered by the audit command itself, since scaffold used `--no-restore`). Clean tree: "The given project `movie-date` has no vulnerable packages given the current sources."

## Hints recorded but not acted on

| Hint                       | Value                              |
| -------------------------- | ----------------------------------- |
| bootstrapper_confidence    | verified                           |
| quality_override           | false                               |
| path_taken                 | standard                            |
| self_check_answers         | null                                |
| team_size                  | solo                                |
| deployment_target          | fly                                 |
| ci_provider                | github-actions                      |
| ci_default_flow            | manual-promotion                    |
| has_auth                   | true                                 |
| has_payments                | false                                |
| has_realtime                | false                                |
| has_ai                      | false                                |
| has_background_jobs         | true                                 |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep. (None were created this run.)
- Address audit findings per your project's risk tolerance — the full breakdown is in this log. (None this run — clean tree.)
- This hand-off named `has_auth: true` and `has_background_jobs: true`, but ASP.NET Core webapi does not ship either batteries-included. Add ASP.NET Core Identity or Keycloak for auth, and Hangfire or a hosted service for the async match-recompute job, before relying on those FRs.
- The Angular frontend is a separate scaffold not covered by this run (this starter is backend-only); set it up alongside and serve its static build from `wwwroot` per the tech-stack hand-off's deployment note.
