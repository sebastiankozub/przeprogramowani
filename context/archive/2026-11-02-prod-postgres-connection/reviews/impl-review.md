<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Production Postgres Connection

- **Plan**: context/changes/prod-postgres-connection/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-02
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Plan drift: 17/17 planned items MATCH; benign extras only (compose pg_isready healthcheck, Neon entries in .gitignore/.dockerignore, CLAUDE.md architecture refresh, deploy-plan notes). Success criteria re-run 2026-11-02: build 0 warnings, tests 2/2, release_command present, prod /health 200, SPA fallback 200, release v5 complete. Public, unthrottled /health/db was already accepted in the plan ("What We're NOT Doing") and is not re-raised.

## Findings

### F1 — No rule for backward-compatible schema changes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: fly.toml:9 (release_command), deploy flow
- **Detail**: Migrations run before rollout on a release machine, then 2 web machines roll one by one; a Fly image rollback never reverts the schema. Old and new code therefore share one schema during every deploy and after every rollback. InitialCreate is empty so there is no risk today, but S-01 (Identity tables) is the first real schema change.
- **Fix**: Record an expand/contract rule in context/foundation/lessons.md (every migration must be compatible with the previous release's code; destructive changes split across two deploys).
  - Strength: Lands before the first real migration; consumed by /10x-plan and /10x-impl-review.
  - Tradeoff: Two-step migrations for renames/drops.
  - Confidence: HIGH — follows directly from release_command + rolling deploy + image-only rollback.
  - Blind spot: None significant.
- **Decision**: ACCEPTED-AS-RULE: Keep migrations backward-compatible with the previous release (lesson only; no code to change — InitialCreate is empty)

### F2 — Doubled process arguments (ENTRYPOINT + Fly commands)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Dockerfile:26, fly.toml:9, fly.toml:34
- **Detail**: Fly keeps ENTRYPOINT ["dotnet","movie-date.dll"] and appends the process/release command, so the app runs as `dotnet movie-date.dll dotnet movie-date.dll --migrate`. Works today (args.Contains), but any argument after --migrate would be parsed as its config value, and logs are confusing.
- **Fix**: Change the Dockerfile's ENTRYPOINT to CMD so Fly's commands replace it (requires a redeploy to verify).
- **Decision**: FIXED — Dockerfile ENTRYPOINT → CMD; verified locally (`--migrate` exit 0, default CMD serves /health + /health/db); takes effect on next deploy

### F3 — Migration test mutates the shared container

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/DatabaseTests.cs:20
- **Detail**: Migrate_applies_InitialCreate runs MigrateAsync on the shared fixture container; future tests in the collection that need tables would depend on test order.
- **Fix**: Run MigrateAsync once in PostgresAppFixture.InitializeAsync; the test asserts the applied migration.
- **Decision**: FIXED — fixture migrates once via MigrationRunner.RunAsync in InitializeAsync; test renamed Fixture_migration_applied_InitialCreate; tests 2/2

### F4 — Health-check registration lives in Program.cs

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Program.cs:10-11
- **Detail**: AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: [HealthEndpoints.DbTag]) sits in Program.cs, while the module layout rule keeps Program.cs to one-line wiring calls (AddAppDatabase is already an extension).
- **Fix**: Move it into an AddAppHealthChecks() extension next to MapHealthEndpoints.
- **Decision**: FIXED — AddAppHealthChecks() extension in Endpoints/HealthEndpoints.cs; Program.cs calls it; build 0 warnings, tests 2/2

### F5 — Retry budget may be tight for Neon cold resume on the release machine

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Data/DataServiceExtensions.cs:24
- **Detail**: 3 retries / max 5 s (~10–15 s total) usually covers Neon resume; a slow resume would abort a deploy (safe, but noisy). The same budget made local /health/db take 13 s to report 503.
- **Fix**: Leave as is; revisit only if a release aborts on a transient connect error.
- **Decision**: SKIPPED — keep 3 retries / 5 s; revisit only if a release aborts on a transient connect error
