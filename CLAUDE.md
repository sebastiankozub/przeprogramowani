# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

- Build: `dotnet build movie-date.csproj`
- Run (dev): `dotnet run` — serves on `http://localhost:5069` / `https://localhost:7117`
- Format: `dotnet format`
- Dependency vulnerability check: `dotnet list package --vulnerable --include-transitive`
- Local DB for `dotnet run`: `docker compose up -d`
- Tests: `dotnet test tests/MovieDate.Tests` (requires Docker running — integration tests start their own throwaway Postgres container via Testcontainers; independent of the compose DB)

## Architecture

Single ASP.NET Core Web API project (`movie-date.csproj`, minimal-API style, target `net10.0`, nullable reference types + implicit usings enabled, root namespace `MovieDate`). `Program.cs` holds only wiring; EF Core + Npgsql data access lives in `Data/` (empty `AppDbContext`, migrations in `Data/Migrations`, `--migrate` mode used by Fly's release command), health endpoints in `Endpoints/HealthEndpoints.cs` (`/health` DB-free for Fly checks, `/health/db` on demand). The template `/weatherforecast` endpoint is still present.

Domain rules, functional requirements, and stack decisions are recorded in `context/foundation/` — read them before implementing instead of asking; don't duplicate their content here:
- `@context/foundation/prd.md` — functional requirements (FR-001..007), the matching/scoring business logic, access control, non-goals
- `@context/foundation/tech-stack.md` — chosen starter, deployment target (Fly.io), CI plan
- `@context/foundation/stack-assessment.md` — agent-friendliness gaps and the planned module layout below

**Module layout rule — follow this when adding any endpoint, do not grow `Program.cs`:** put endpoints in `Endpoints/` (one static class per feature, e.g. `MoviesEndpoints`, exposing `MapXxxEndpoints(this IEndpointRouteBuilder)`), business logic in `Services/` (e.g. `MatchScoringService`, `MatchRecomputeService`), DbContext/entities/seed data in `Data/`, and request/response records in `Contracts/` — endpoints must never return entities directly. This is a deliberate choice compensating for the minimal-API template's lack of a built-in convention (see `stack-assessment.md`), not yet validated by an A/B run — if you catch an agent ignoring it, note that in `context/foundation/lessons.md`.

Work order lives in `@context/foundation/roadmap.md`; database, auth, and recompute decisions are recorded at the end of `tech-stack.md`. The Angular SPA shell lives in `ClientApp/` and is served from `wwwroot`.

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 2

Turn one roadmap item into the first implementation cycle with the **change planning chain**:

```
/10x-roadmap -> /10x-new -> /10x-plan -> /10x-plan-review -> /10x-implement
```

`/10x-new`, `/10x-plan`, `/10x-plan-review`, and `/10x-implement` are the lesson focus. `/10x-frame` and `/10x-research` are not required rituals here; they are escalation paths introduced in the next lesson.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Change setup (lesson focus)** | |
| `/10x-new <change-id>` | You selected a roadmap item and need a stable change folder. Creates `context/changes/<change-id>/change.md` so planning, implementation, progress, commits, and later review all share one identity. Use AFTER roadmap selection, BEFORE `/10x-plan`. |
| **Planning (lesson focus)** | |
| `/10x-plan <change-id>` | You have a change folder and need a reviewable implementation plan. Reads roadmap context, foundation docs, codebase evidence, and any existing change notes; writes `plan.md` and `plan-brief.md` with phases, file contracts, success criteria, and `## Progress`. |
| **Plan readiness (lesson focus)** | |
| `/10x-plan-review <change-id>` | You have `plan.md` and need a light pre-code readiness check. Use it to catch missing end state, weak contracts, malformed progress, scope drift, or blind spots before code changes begin. |
| **Implementation (lesson focus)** | |
| `/10x-implement <change-id> phase <n>` | You have an approved plan and want to execute one phase with verification, manual gate, commit ritual, and SHA write-back to `## Progress`. |
| **Lifecycle closure** | |
| `/10x-archive <change-id>` | A change is merged or intentionally closed. Move it out of active `context/changes/` into archive state. |

### How the chain hands off

- `/10x-new` creates the durable change identity.
- `/10x-plan` turns that identity into an implementation contract.
- `/10x-plan-review` checks the plan before the agent mutates code.
- `/10x-implement` executes one planned phase, verifies, asks for manual confirmation when needed, commits, and records progress.

### Lesson boundaries

- Plan is the default router after roadmap selection. Start with `/10x-plan` unless the problem is unclear or external evidence is blocking.
- Do not run `/10x-frame + /10x-research` as ceremony for every change.
- Do not turn this lesson into a full end-to-end product build. A checkpoint with a planned and partially or fully implemented stream is valid.
- Code review of the implemented diff belongs to Lesson 3 via `/10x-impl-review`.
- Lifecycle closure via `/10x-archive` after a change is merged or intentionally closed.

### Paths used by this lesson

- `context/foundation/roadmap.md` - upstream roadmap
- `context/changes/<change-id>/change.md` - change identity
- `context/changes/<change-id>/plan.md` - implementation contract
- `context/changes/<change-id>/plan-brief.md` - compressed handoff
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
