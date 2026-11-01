---
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
---

## Why this stack

Solo developer shipping a web-app MVP in 1 week (after-hours, hard deadline 2026-11-04) with prior .NET expertise — the recommended default for `(web, dotnet)` is ASP.NET Core webapi, which clears all four agent-friendly gates and is bootstrapper-verified, so scaffolding will be smooth. The PRD names auth (FR-001) and background-job-shaped async recompute (FR-002/003/005) as in-scope; ASP.NET Core doesn't ship these batteries-included the way Django or Rails would, so auth (ASP.NET Core Identity or Keycloak) and the recompute job (e.g. Hangfire or a hosted service) are manual additions after scaffolding. Deployment targets Fly.io, chosen over the starter's own Azure App Service default for its lower cost at this project's small scale — Azure's free F1 tier caps daily compute, while Fly's pay-as-you-go pricing stays near-zero for a low-traffic MVP. The Angular frontend (added separately; not part of this backend-only starter) is built to static files and served from the ASP.NET Core app's `wwwroot`, so the project ships as a single Fly deployment rather than two. CI runs on GitHub Actions with manual promotion after merge rather than auto-deploy, since the user wants an explicit gate before each deploy. No payments, realtime, or AI features are in scope, consistent with the PRD's Non-Goals.
