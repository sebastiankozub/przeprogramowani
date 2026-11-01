---
project: movie-date
assessed_at: 2026-09-26T12:10:28Z
agent_readiness: ready-with-compensation
context_type: brownfield
stack_components:
  language: C# (.NET 10, nullable reference types enabled)
  framework: ASP.NET Core Web API (minimal API template, net10.0)
  build_tool: dotnet SDK / MSBuild (SDK-style csproj)
  test_runner: null
  package_manager: NuGet (dotnet)
  ci_provider: null
  deployment_target: null
gates_passed: 3
gates_failed: 1
---

## Stack Components

**Language — C# on .NET 10.** Statically typed by the language. `movie-date.csproj` sets `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.

**Framework — ASP.NET Core Web API (minimal API style).** `Program.cs` uses `WebApplication.CreateBuilder` with `AddOpenApi()` and inline endpoint mapping (the weather-forecast sample). The only package reference is `Microsoft.AspNetCore.OpenApi` 10.0.11.

**Build tool — dotnet SDK / MSBuild.** SDK-style project (`Microsoft.NET.Sdk.Web`), single project, no `.sln`, no `global.json`.

**Test runner — not detected.** No test project exists.

**Package manager — NuGet** through the dotnet CLI (no lockfile committed yet).

**CI/CD and deployment — not detected.** No `.github/`, no `Dockerfile`, no `fly.toml`. The hand-off names Fly.io and GitHub Actions, but neither is set up yet.

**Instruction files — `CLAUDE.md` only**, and it holds course-toolkit notes, not project conventions. No `AGENTS.md`.

Note: this project is a fresh greenfield scaffold (PRD `context_type: greenfield`), so the PRD was not used to scope the assessment.

## Quality Gate Assessment

| Component   | Typed | Convention | Training Data | Documented | Verdict |
|-------------|-------|------------|---------------|------------|---------|
| Language    | ✓     | —          | —             | —          | pass    |
| Framework   | —     | ~          | ✓             | ✓          | pass    |
| Build tool  | —     | ✓          | ✓             | ✓          | pass    |
| Test runner | —     | —          | —             | —          | absent  |

Legend: ✓ = pass, ✗ = fail, ~ = partial, — = not applicable

### Gate Details

- **Typed (C#) ✓** — the language is statically typed, and `<Nullable>enable</Nullable>` in `movie-date.csproj` adds compile-time null checking.
- **Convention (framework) ~** — ASP.NET Core has strong conventions (DI, configuration via `appsettings*.json`, `Properties/launchSettings.json`), but the minimal-API template puts everything in `Program.cs` with no prescribed folder layout for endpoints, services or data access. Scored as partial until the layout is documented in an instruction file.
- **Convention (build tool) ✓** — SDK-style csproj with implicit globbing and standard `dotnet build/test/run` commands.
- **Training data ✓** — ASP.NET Core is the mainstream web framework within the .NET ecosystem (assessed within the C#/.NET family, not against JS).
- **Documented ✓** — versioned official docs at learn.microsoft.com/aspnet/core, current for .NET 10.
- **Test runner — absent** — `find` shows no `*.Tests.csproj` and no `xunit`/`nunit`/`MSTest` package reference anywhere.

## Gaps & Compensation

1. **No tests.** An agent has no fast feedback loop to check its own changes, and the course requires at least one user-perspective test. *Compensation:* add an xUnit test project (see rules below) and make "tests pass" a required check before any change is done.
2. **Layout is undocumented (partial convention).** The minimal-API template lets an agent scatter endpoints and logic through `Program.cs`. *Compensation:* document a fixed folder layout and endpoint-grouping rule.
3. **No project conventions in instruction files.** `CLAUDE.md` currently carries only course notes. *Compensation:* add the project rules below (async recompute, append-only match list, location privacy, Angular from `wwwroot`).
4. **No CI or deployment config yet.** Not a quality-gate failure, but agents cannot verify a deploy. *Compensation:* covered by Lesson 5 (Fly.io) and a GitHub Actions workflow later.

### Recommended Instruction File Additions

Paste into `CLAUDE.md` (or `AGENTS.md`):

```markdown
## Stack
- C# / ASP.NET Core Web API on .NET 10, minimal API style. Nullable reference types are ON: never suppress nullable warnings with `!` without a comment saying why.
- Build: `dotnet build movie-date.csproj`. Run: `dotnet run`. Tests: `dotnet test`. A change is not done until build and tests pass.

## Project layout (keep it; do not grow Program.cs)
- `Program.cs` — only service registration and `app.MapXxxEndpoints()` calls.
- `Endpoints/` — one static class per feature (`MoviesEndpoints`, `WatchlistEndpoints`, `MatchesEndpoints`, `MessagesEndpoints`), each exposing `MapXxxEndpoints(this IEndpointRouteBuilder)`.
- `Services/` — business logic (`MatchScoringService`, `MatchRecomputeService`). Endpoints stay thin.
- `Data/` — DbContext, entities, seed data (movies, Polish cities with coordinates).
- `Contracts/` — request/response records. Never expose entities directly from endpoints.
- Tests mirror this layout in `movie-date.Tests/`.

## Domain rules (from context/foundation/prd.md — do not violate)
- Match score = shared highly-rated movies + shared watchlist movies + distance-decayed proximity. Proximity never reaches zero.
- The match list is append-only: a recompute may update scores and add candidates, but never removes a candidate who has appeared.
- Add/edit/remove of a watched or watchlist movie returns immediately and triggers the recompute in the background. Never recompute inside the request.
- Exact location is never returned to other users. Only city-level data leaves the API.
- Movies and cities come from seeded tables (ids), never free text.
- Messages are readable only by the two users in the conversation.

## Conventions
- Async all the way: methods returning Task end in `Async`, always accept and pass a `CancellationToken`.
- Validate request records at the endpoint boundary and return `Results.ValidationProblem` on failure.
- Files: PascalCase.cs, one public type per file. Namespaces follow folders under `MovieDate`.
- Prefer built-in ASP.NET Core Identity/authorization attributes over hand-rolled auth. All endpoints except register/login require `.RequireAuthorization()`.
- Pin the framework: .NET 10. Docs: https://learn.microsoft.com/aspnet/core (check that the page matches .NET 10 before copying patterns).

## Frontend and deployment
- Angular lives in `ClientApp/`; its production build is copied to `wwwroot/` and served by this app (one deployment).
- Deployment target is Fly.io via Docker; CI is GitHub Actions with manual promotion. Do not add other hosting config.
```

## Summary

**Overall: ready-with-compensation.** Every present component passes the agent-friendliness criteria: C# is typed, ASP.NET Core and MSBuild are mainstream and well documented. Nothing about the stack itself needs replacing.

**Strengths:** static typing with nullable checks, mainstream framework with current versioned docs, and a scaffold that builds cleanly with 0 warnings.

**Gaps:** no test project (the biggest one, since it removes the agent's feedback loop), an undocumented layout on a minimal-API template, and no project conventions in instruction files.

**Next step:** paste the instruction-file additions above into `CLAUDE.md` (Lesson 4 covers this properly), add an xUnit project, then run `/10x-health-check`.
