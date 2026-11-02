# Production Postgres Connection Implementation Plan

## Overview

Roadmap item **F-01** (`prod-postgres-connection`): give the app its first database. The deployed app on Fly connects to a managed Neon Postgres (free tier, AWS Frankfurt), schema migrations run automatically on every deploy through a Fly `release_command`, and an on-demand `/health/db` endpoint proves connectivity. Local development uses the same engine in Docker. No domain tables — those arrive with the slices that need them (S-01 first).

## Current State Analysis

- `Program.cs` holds everything: OpenAPI, static files, an inline `/health` endpoint, the template `/weatherforecast`, and the SPA fallback that must stay last (`Program.cs:19-20`, `Program.cs:42`).
- `movie-date.csproj` references only `Microsoft.AspNetCore.OpenApi`; no data access packages, no `dotnet-ef` tool (no `.config/dotnet-tools.json`).
- `appsettings*.json` contain only logging; no connection string anywhere.
- No test project exists.
- Deploy: 3-stage `Dockerfile` (`COPY . .` at the backend stage), `fly.toml` with app `moviedate-sk`, region `fra`, **2 `web` machines** (`context/deployment/deploy-plan.md:68`), a Fly HTTP check on `/health` every 30 s, and a manual `workflow_dispatch` GitHub Actions deploy (`.github/workflows/deploy.yml`) that checks out the pushed repo state.
- Decisions already locked (`context/foundation/tech-stack.md`, "Decisions recorded 2026-11-02"): external free-tier Postgres, ASP.NET Core Identity later in S-01, in-process hosted service for recompute (the `worker` TODO in `fly.toml` is obsolete).
- Tooling present: .NET SDK 10.0.400, Docker 29.5, Neon CLI 8.16 (logged in as the project owner).
- Neon project already created via `neon init`: `moviedate-sk` (`patient-pond-88799426`), AWS Frankfurt, branch `main`. Repo link in `.neon`; `.env.local` holds `DATABASE_URL` / `DATABASE_URL_UNPOOLED` (both gitignored and dockerignored). `tests/` is already in `.dockerignore`.

## Desired End State

- Locally: `docker compose up -d` starts Postgres 17; `dotnet run -- --migrate` applies migrations and exits 0; `dotnet run` serves `/health/db` → 200.
- `dotnet test tests/MovieDate.Tests` spins up Postgres via Testcontainers and passes (migrations apply, `/health/db` → 200).
- Production: a deploy runs `--migrate` once on a release machine before rollout; `https://moviedate-sk.fly.dev/health/db` → 200, `/health` → 200, SPA routes unchanged. The Neon database contains `__EFMigrationsHistory` with the `InitialCreate` row.
- A failed migration or missing connection string aborts the deploy, leaving the previous release serving.

### Key Discoveries:

- 2 web machines (`deploy-plan.md:68`) → migrating at app startup would race; `release_command` runs once per deploy on a temporary machine.
- Fly check on `/health` every 30 s (`fly.toml` `[[http_service.checks]]`) → if it touched the DB, Neon's free-tier autosuspend would never trigger (~0.25 CU × 730 h ≈ 180 CU-h/month vs. ~100 CU-h limit). `/health/db` must stay out of Fly checks.
- Root-level `movie-date.csproj` with the Web SDK globs `**/*.cs` → a test project under `tests/` (including its generated `obj/*.cs`) would be compiled into the app unless excluded.
- New routes must be registered above `app.MapFallbackToFile("index.html")` (`Program.cs:42`).
- Minimal hosting + `WebApplicationFactory`: configuration added via the factory's `ConfigureAppConfiguration` is applied after `Program.cs` top-level code has already read `builder.Configuration`, so an eagerly read connection string would come from `appsettings.Development.json`, not the test container. Tests must inject it with `builder.UseSetting(...)`.
- Neon hands out `postgresql://` URLs; Npgsql requires the key-value form (`Host=…;Database=…;Username=…;Password=…;SSL Mode=Require`). Use the **direct** (non-pooled) endpoint — PgBouncer transaction pooling conflicts with migrations.
- Module layout rule (`CLAUDE.md`): endpoints in `Endpoints/`, DbContext in `Data/`, no growth of `Program.cs`.

## What We're NOT Doing

- No domain entities or tables (users, movies, cities) — S-01+ introduce them.
- No ASP.NET Core Identity — S-01.
- No Fly health check against the database.
- No running tests in CI — the workflow stays deploy-only.
- No removal of the template `/weatherforecast` endpoint.
- No self-written backup script — Neon manages backups/PITR for the free tier.
- No rate limiting or auth on `/health/db` — anyone can call it and wake Neon; accepted for MVP traffic.
- No use of Neon's pooled endpoint, Neon branches for dev, or the repo `.env` written by `neon init` as an app config source (it is only the source of the connection string for the Fly secret).

## Implementation Approach

EF Core with the Npgsql provider, one `AppDbContext` with no entities and an empty `InitialCreate` migration (creates only `__EFMigrationsHistory` — enough to prove the pipeline). The app gains a `--migrate` mode: build the host, apply migrations, exit. Fly's `[deploy] release_command` invokes that mode. Connectivity is checked by the standard ASP.NET Core health-check middleware with an EF Core DbContext check, mapped at `/health/db`. Connection string key: `ConnectionStrings:Default` (Fly secret `ConnectionStrings__Default`), required outside Development.

## Critical Implementation Details

- **Secret before deploy, staged:** set the Fly secret with `fly secrets set --stage …` so it doesn't restart the currently running machines; it takes effect with the next deploy. Deploy only after the secret exists, otherwise the release command fails (by design) and the deploy aborts.
- **Push before CI deploy:** the GitHub Actions workflow deploys what is on `origin/main`, so phase commits must be pushed before `gh workflow run deploy.yml`.
- **Neon cold start:** the first query after autosuspend can take ~0.5–1 s; enable Npgsql's `EnableRetryOnFailure` so a transient wake-up error is retried rather than surfaced.

## Phase 1: Local database + EF Core wiring

### Overview

Add data access, a local Postgres, the empty initial migration, the `--migrate` mode, and the health endpoints — all verifiable on the developer machine.

### Changes Required:

#### 1. Packages and local tool

**File**: `movie-date.csproj`, `.config/dotnet-tools.json` (new)

**Intent**: Add the Npgsql EF Core provider, EF Core design-time package, and the EF Core health-check package; pin `dotnet-ef` as a local tool so migrations are reproducible.

**Contract**: Package refs `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x), `Microsoft.EntityFrameworkCore.Design` (10.x, `PrivateAssets=all`), `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (10.x). Local tool `dotnet-ef` 10.x.

#### 2. DbContext and registration

**File**: `Data/AppDbContext.cs`, `Data/DataServiceExtensions.cs` (new)

**Intent**: An empty `AppDbContext` and an `AddAppDatabase(IConfiguration, IHostEnvironment)` extension that registers it with Npgsql + retry-on-failure and throws at startup when `ConnectionStrings:Default` is missing outside Development.

**Contract**: `MovieDate.Data.AppDbContext : DbContext`; connection string key `ConnectionStrings:Default`.

#### 3. Migration mode

**File**: `Data/MigrationRunner.cs` (new), `Program.cs`

**Intent**: When the process is started with `--migrate`, apply pending migrations and exit with code 0 (non-zero on failure) without starting the web server.

**Contract**: CLI flag `--migrate`; used verbatim by Fly `release_command = "dotnet movie-date.dll --migrate"` in Phase 3.

#### 4. Initial migration

**File**: `Data/Migrations/*` (generated)

**Intent**: `dotnet ef migrations add InitialCreate --output-dir Data/Migrations` on the empty context — creates only the history table on apply.

**Contract**: Migration name `InitialCreate`.

#### 5. Health endpoints

**File**: `Endpoints/HealthEndpoints.cs` (new), `Program.cs`

**Intent**: Move the existing `/health` into `MapHealthEndpoints()` unchanged (still DB-free, still the Fly check target) and add `/health/db` backed by the DbContext health check. Register health checks in DI. Mapped before the SPA fallback.

**Contract**: `GET /health` → 200 `{"status":"ok"}` (unchanged); `GET /health/db` → 200 Healthy / 503 Unhealthy.

#### 6. Local Postgres and dev config

**File**: `compose.yaml` (new), `appsettings.Development.json`

**Intent**: Postgres 17 service on `localhost:5432` with a named volume and dev-only credentials; matching `ConnectionStrings:Default` in Development settings.

**Contract**: Service `db`, image `postgres:17`.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Local Postgres starts: `docker compose up -d` and `docker compose ps` shows `db` healthy/running
- Migration mode applies and exits 0: `dotnet run --project movie-date.csproj -- --migrate`
- Migration history present: `docker compose exec db psql -U <dev-user> -d <dev-db> -c "select \"MigrationId\" from \"__EFMigrationsHistory\""` lists `InitialCreate`
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5069/health/db` → `200` while `dotnet run` is up
- `/health` unchanged → `200` with `{"status":"ok"}`

#### Manual Verification:

- With `docker compose stop db`, `/health/db` returns 503 while `/health` still returns 200
- `Program.cs` only gained wiring calls (no inline endpoint bodies beyond what existed)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Test project

### Overview

Create the first test project with an integration test against a real Postgres in a container, and keep it out of the app's compilation and Docker image.

### Changes Required:

#### 1. Exclude tests from the app

**File**: `movie-date.csproj`, `.dockerignore`

**Intent**: Prevent the root Web SDK project from compiling anything under `tests/`, and keep `tests/` out of the Docker build context.

**Contract**: `<Compile Remove="tests/**" />` (plus `Content`/`None` removes if build warns); `.dockerignore` entry `tests/`.

#### 2. Test project

**File**: `tests/MovieDate.Tests/MovieDate.Tests.csproj` (new) and test files

**Intent**: xUnit project referencing the app, `Microsoft.AspNetCore.Mvc.Testing`, and `Testcontainers.PostgreSql`. A shared fixture starts a Postgres 17 container and boots the app via `WebApplicationFactory<Program>` with `ConnectionStrings:Default` pointed at the container.

**Contract**: The fixture injects the container connection string with `builder.UseSetting("ConnectionStrings:Default", <container>)` inside `ConfigureWebHost` — not `ConfigureAppConfiguration` (see Key Discoveries). Tests: (a) after migrating, applied migrations include `InitialCreate`; (b) `GET /health/db` → 200. If `Program` is not accessible to the test project, add `public partial class Program;` at the end of `Program.cs`. If `UseSetting` turns out not to reach `builder.Configuration` on .NET 10, fall back to resolving the connection string lazily inside `AddDbContext((sp, options) => …)`.

#### 3. Commands doc

**File**: `CLAUDE.md` (Commands section)

**Intent**: Replace "Tests: no test project exists yet" with the real test command and the Docker requirement; add `docker compose up -d` for local dev.

**Contract**: `dotnet test tests/MovieDate.Tests` (requires Docker running).

### Success Criteria:

#### Automated Verification:

- App still builds alone: `dotnet build movie-date.csproj`
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Docker image still builds: `docker build -t moviedate-local .`
- Tests are independent of the compose DB: `docker compose stop db && dotnet test tests/MovieDate.Tests` still passes

#### Manual Verification:

- Test output shows a Postgres container was started and torn down (no leftover containers in `docker ps -a` after the run)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production — Neon + Fly release command

### Overview

Point production at Neon and make every deploy run migrations first. Human performs everything that touches live Fly/Neon accounts, secrets, and deploys (permission model from `deploy-plan.md`).

### Changes Required:

#### 1. Neon project (human, CLI-assisted)

**File**: none in repo (`.env` written by `neon init` is gitignored)

**Intent**: Neon project `moviedate-sk` (`patient-pond-88799426`) in org `org-spring-paper-07031459`, AWS Europe (Frankfurt) — **already created**. Convert `DATABASE_URL_UNPOOLED` from `.env.local` (direct endpoint) to Npgsql key-value form with `SSL Mode=Require`, without printing the password to the terminal.

**Contract**: Fly secret `ConnectionStrings__Default` = `Host=<direct-host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require`.

#### 2. Fly configuration

**File**: `fly.toml`

**Intent**: Add `[deploy] release_command = "dotnet movie-date.dll --migrate"`. Remove the obsolete `worker` process-group TODO comments (in-process recompute decided in `tech-stack.md`). Leave the `/health` check untouched.

**Contract**: `[deploy]` section with `release_command`.

#### 3. Secret and deploy (human)

**Intent**: `fly secrets set --stage ConnectionStrings__Default="…" -a moviedate-sk`; push commits; `gh workflow run deploy.yml`; watch the release command in `fly logs`.

#### 4. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Update "What's still open": Postgres resolved (Neon, release_command migrations, `/health/db`), `worker` item obsolete; record the new known-good release image reference.

**Contract**: Section "What's still open (for Module 2…)".

### Success Criteria:

#### Automated Verification:

- `fly.toml` contains `release_command = "dotnet movie-date.dll --migrate"` under `[deploy]`
- Deploy workflow run completes `success`: `gh run list --workflow deploy.yml --limit 1`
- `curl -s -o /dev/null -w "%{http_code}" https://moviedate-sk.fly.dev/health/db` → `200`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`
- `curl -s -o /dev/null -w "%{http_code}" https://moviedate-sk.fly.dev/some-nonexistent-route` → `200` (SPA fallback intact)

#### Manual Verification:

- `fly logs -a moviedate-sk` shows the release machine applying `InitialCreate` (or "no migrations to apply" on re-deploy) before machines roll
- Neon console (SQL editor) shows `__EFMigrationsHistory` with `InitialCreate`
- Neon compute goes idle (suspended) after ~5 minutes without `/health/db` calls — confirms the Fly check isn't keeping it awake
- `deploy-plan.md` updated with the new known-good image reference

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- None — no logic beyond wiring in this change.

### Integration Tests:

- Postgres container + `WebApplicationFactory<Program>`: migrations apply (`InitialCreate` present), `/health/db` → 200.

### Manual Testing Steps:

1. Local: stop the compose DB → `/health/db` 503, `/health` 200; start it again → 200.
2. Prod: curl `/health/db`, `/health`, a SPA route after deploy.
3. Prod: confirm the release command log line and the Neon history row.

## Performance Considerations

Neon free tier autosuspends; the first request after idle pays ~0.5–1 s wake-up. Within NFR-01 (< 2 s) for now; revisit if S-05 match-list latency suffers.

## Migration Notes

First migration on an empty Neon database; nothing to preserve. Rollback of a bad deploy = previous image via `fly deploy --image …` (procedure in `deploy-plan.md`); the empty `InitialCreate` needs no down-migration.

## References

- Roadmap item: `context/foundation/roadmap.md` → F-01
- Decisions: `context/foundation/tech-stack.md` → "Decisions recorded 2026-11-02"
- Deploy history and permission model: `context/deployment/deploy-plan.md`
- Neon risks/costs context: `context/foundation/infrastructure.md`
- SPA fallback ordering: `Program.cs:41-42`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Local database + EF Core wiring

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 2a2965e
- [x] 1.2 Local Postgres starts: `docker compose up -d` and `docker compose ps` shows `db` healthy/running — 2a2965e
- [x] 1.3 Migration mode applies and exits 0: `dotnet run --project movie-date.csproj -- --migrate` — 2a2965e
- [x] 1.4 Migration history present: `__EFMigrationsHistory` lists `InitialCreate` — 2a2965e
- [x] 1.5 `/health/db` → `200` while `dotnet run` is up — 2a2965e
- [x] 1.6 `/health` unchanged → `200` with `{"status":"ok"}` — 2a2965e

#### Manual

- [x] 1.7 With `docker compose stop db`, `/health/db` returns 503 while `/health` still returns 200 — 2a2965e
- [x] 1.8 `Program.cs` only gained wiring calls — 2a2965e

### Phase 2: Test project

#### Automated

- [x] 2.1 App still builds alone: `dotnet build movie-date.csproj` — ef8f4f3
- [x] 2.2 Tests pass: `dotnet test tests/MovieDate.Tests` — ef8f4f3
- [x] 2.3 Docker image still builds: `docker build -t moviedate-local .` — ef8f4f3
- [x] 2.5 Tests are independent of the compose DB — ef8f4f3

#### Manual

- [x] 2.4 No leftover test containers after the run — ef8f4f3

### Phase 3: Production — Neon + Fly release command

#### Automated

- [x] 3.1 `fly.toml` contains `release_command` under `[deploy]` — 43f221d
- [x] 3.2 Deploy workflow run completes `success` — 43f221d
- [x] 3.3 Prod `/health/db` → `200` — 43f221d
- [x] 3.4 Prod `/health` → `{"status":"ok"}` — 43f221d
- [x] 3.5 Prod SPA fallback → `200` — 43f221d

#### Manual

- [x] 3.6 `fly logs` shows the release machine applying migrations before rollout — 43f221d
- [x] 3.7 Neon console shows `__EFMigrationsHistory` with `InitialCreate` — 43f221d
- [x] 3.8 Neon compute suspends after ~5 minutes idle — 43f221d
- [x] 3.9 `deploy-plan.md` updated with the new known-good image reference — 43f221d
