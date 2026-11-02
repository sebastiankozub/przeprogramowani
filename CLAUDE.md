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

## 10xDevs AI Toolkit - Module 2, Lesson 3

Review AI-generated code before merge with the **implementation review chain**:

```
/10x-implement -> /10x-impl-review -> triage -> (/10x-lesson | fix | skip | disagree)
```

`/10x-impl-review` is the lesson focus. Review is a quality gate, not an instruction to fix every finding.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Code review (lesson focus)** | |
| `/10x-impl-review <change-id>` | You have implemented code and want a structured review before merge. The skill checks plan adherence, scope discipline, safety and quality, architecture, pattern consistency, and success criteria, then presents findings for triage. |
| **Recurring lesson outcome** | |
| `/10x-lesson` | A finding reveals a recurring project rule or agent failure pattern. Record it in `context/foundation/lessons.md` instead of treating it as a one-off note. |

### Triage discipline

- Severity says how bad the finding is. Impact says how much the decision matters now.
- Valid outcomes: fix now, fix differently, skip, accept as risk, record as recurring rule (`/10x-lesson`), disagree.
- Fix critical findings. Do not burn hours on low-impact observations just because the agent found them.
- Conscious skipping of low-impact findings is a valid review outcome, not negligence.
- If you disagree with a finding, record why. Wrong agent reasoning is also signal.

### Review boundaries

- This lesson reviews implemented code. It does not create the plan, execute new phases, or teach CI review.
- Testing strategy and quality gates are introduced in Module 3.
- Do not use `/10x-contract` as a triage outcome in this lesson.

### Paths used by this lesson

- `context/changes/<change-id>/plan.md` - expected implementation contract
- `context/changes/<change-id>/reviews/` - review output
- `context/foundation/lessons.md` - recurring lessons

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
