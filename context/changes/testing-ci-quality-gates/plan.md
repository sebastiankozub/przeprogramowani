# Quality gates in CI (test rollout phase 2) Implementation Plan

## Overview

Add `.github/workflows/ci.yml` so every pull request and every push to `main` runs the whole test suite: backend build + integration tests, SPA unit tests + production build, and the Playwright critical-path e2e against a real Postgres. This protects test-plan risk #5 (a red test or broken migration reaching production because nothing runs the suite before deploy). It is delivered as a PR so the agent review workflow (`.github/workflows/impl-review.yml`) reviews it against this plan.

## Current State Analysis

- The only workflows are `deploy.yml` (manual `workflow_dispatch` Fly deploy) and `impl-review.yml` (agent review). Nothing builds or tests on push or PR (`context/foundation/test-plan.md` §2 risk #5, §4 "CI none yet").
- The backend tests need Docker for Testcontainers (`tests/MovieDate.Tests/PostgresAppFixture.cs`). GitHub's `ubuntu-latest` runners provide Docker.
- The SPA unit tests run with `npx ng test --watch=false` (Vitest via `@angular/build:unit-test`); the production build is `npm run build` in `ClientApp/`.
- The e2e suite (`ClientApp/playwright.config.ts`) starts the API via `dotnet run --launch-profile http -- --migrate` and then `dotnet run`, plus `ng serve`. The API's Development connection string expects Postgres on `localhost:5432` with database/user `moviedate` and password `moviedate_dev` (`appsettings.Development.json`, `compose.yaml`).

## Desired End State

A PR shows three green CI jobs — `backend`, `frontend`, `e2e`. A red test in any of them makes the workflow fail. On e2e failure the Playwright report is downloadable as an artifact.

## What We're NOT Doing

- No change to `deploy.yml`. It stays manual; branch protection that requires these checks before merging is a repository setting, not code.
- No new tests. Phase 3 of the test plan adds the privacy/ranking contract tests.
- No caching beyond the setup actions' built-in npm/NuGet caching.

## Phase 1: CI workflow

### Changes Required:

#### 1. Workflow

**File**: `.github/workflows/ci.yml` (new)

**Intent**: Run the three gates of test-plan §5 on every PR and every push to `main`.

**Contract**:
- Triggers: `pull_request` and `push` to `main`.
- `permissions: contents: read`; concurrency per ref, cancelling in-progress runs.
- `backend` job: .NET 10 → `dotnet build movie-date.csproj` → `dotnet test tests/MovieDate.Tests`.
- `frontend` job (`ClientApp/`): Node 22 → `npm ci` → `npx ng test --watch=false` → `npm run build`.
- `e2e` job:
  - a `postgres:17` service with the Development credentials on port 5432;
  - .NET 10 + Node 22;
  - `npm ci` → `npx playwright install --with-deps chromium` → `npm run e2e`;
  - uploads `ClientApp/playwright-report/` when the job fails.

#### 2. Test plan status

**File**: `context/foundation/test-plan.md`

**Intent**: Record that the CI gates now exist. §3 Phase 2 points at this change. In §4, the CI row names the workflow. In §5, build, backend, SPA and e2e are marked "required (CI)".

**Contract**: Only §3 Status/Change-folder cells, the §4 CI row and the §5 "Where/Required?" cells.

### Success Criteria:

#### Automated Verification:

- The CI workflow runs on this PR, and the `backend`, `frontend` and `e2e` jobs are green
- The agent review workflow posts a review comment on this PR

#### Manual Verification:

- The Actions tab shows the CI run with three jobs, and the logs show 136 backend tests, 33 SPA tests and 2 e2e tests passing

## References

- Test plan: `context/foundation/test-plan.md` (risk #5, §3 Phase 2, §5)
- E2E setup: `context/foundation/test-stack.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: CI workflow

#### Automated

- [x] 1.1 CI jobs backend, frontend, e2e green on the PR — 31b5890
- [x] 1.2 Agent review comment posted on the PR — 31b5890

#### Manual

- [x] 1.3 Actions tab shows three jobs with the expected test counts — 31b5890
