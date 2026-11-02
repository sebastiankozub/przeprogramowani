# Production Postgres Connection — Plan Brief

> Full plan: `context/changes/prod-postgres-connection/plan.md`

## What & Why

Roadmap foundation F-01: the app has no database, and every user-facing slice (starting with S-01 register/login) needs one. This change connects the deployed app to a managed Postgres, makes schema migrations run automatically on deploy, and gives a one-curl way to prove the database is reachable in production.

## Starting Point

ASP.NET Core minimal API deployed to Fly (`moviedate-sk`, 2 web machines in `fra`) with only `/health`, a template endpoint, and an Angular shell. No data access packages, no connection string, no tests. Neon project `moviedate-sk` already exists (Frankfurt); its connection strings sit in the gitignored `.env.local`.

## Desired End State

A deploy runs migrations once, then rolls out; `https://moviedate-sk.fly.dev/health/db` returns 200 against Neon. Locally, `docker compose up -d` + `dotnet run` gives the same behaviour, and `dotnet test tests/MovieDate.Tests` proves migrations and the health check against a throwaway Postgres container.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Prod database | Neon free tier, AWS Frankfurt, direct endpoint | Managed backups at zero cost, next to Fly `fra`; direct endpoint avoids pooler/migration conflicts | Roadmap interview / Plan |
| Migrations on deploy | Fly `release_command` → `dotnet movie-date.dll --migrate` | Runs once per deploy on a temp machine (no race between 2 web machines) and aborts the deploy on failure | Plan |
| DB health check | `/health/db` on demand only; Fly keeps checking DB-free `/health` | A 30 s DB check would keep Neon awake and exceed the free-tier compute limit | Plan |
| Tests | New xUnit project with Testcontainers Postgres | Gives S-01+ a ready place for real-database tests | Plan |
| Local DB | Postgres 17 via `compose.yaml` | Offline, free, same major version as Neon | Plan |
| Config | `ConnectionStrings:Default`, required outside Development | Missing secret fails the release command instead of a half-working app | Plan |

## Scope

**In scope:** EF Core + Npgsql, empty `AppDbContext` + `InitialCreate`, `--migrate` mode, `/health/db`, `Endpoints/HealthEndpoints.cs`, compose file, test project, `fly.toml` release command, Neon project + staged Fly secret, deploy and docs update.

**Out of scope:** domain tables, Identity (S-01), DB in Fly checks, tests in CI, removing `/weatherforecast`, backup scripts, Neon pooled endpoint/branches.

## Architecture / Approach

`Program.cs` stays thin: it calls `AddAppDatabase(...)`, health-check registration, and `MapHealthEndpoints()`; with `--migrate` it applies migrations and exits. Fly runs that mode as the release step; the web machines then start with the same `ConnectionStrings__Default` secret.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Local DB + EF Core | DbContext, migration, `--migrate`, `/health/db`, compose | Health endpoint mapped after SPA fallback gets swallowed |
| 2. Test project | Testcontainers integration tests | Root csproj compiling `tests/**` unless excluded |
| 3. Production | Neon project, staged secret, release command, verified prod | Secret format (URL vs Npgsql key-value) or deploying before pushing |

**Prerequisites:** Docker running; `neon login` completed; Fly CLI authenticated; push access to `origin`.
**Estimated effort:** ~1 session across 3 phases.

## Open Risks & Assumptions

- Neon wake-up latency (~0.5–1 s) is assumed acceptable against NFR-01; revisit in S-05.
- Neon free-tier limits (storage, compute hours) assumed sufficient for MVP traffic.
- `/health/db` is public and unthrottled — anyone can wake Neon and spend compute hours; accepted for MVP.
- Tests inject the container connection string via `UseSetting` because minimal hosting reads configuration before the factory's overrides apply; if that fails on .NET 10, the connection string is resolved lazily instead.

## Success Criteria (Summary)

- Production `/health/db` returns 200 and Neon shows the `InitialCreate` history row.
- A deploy with a broken migration or missing secret never replaces the running release.
- `dotnet test tests/MovieDate.Tests` passes locally.
