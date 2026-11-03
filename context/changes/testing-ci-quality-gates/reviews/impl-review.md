<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Quality gates in CI (test rollout phase 2)

- **Plan**: `context/changes/testing-ci-quality-gates/plan.md`
- **Scope**: Full plan (CI review on PR #1)
- **Date**: 2026-11-03
- **CI run**: https://github.com/sebastiankozub/przeprogramowani/actions/runs/41636386634
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Test Coverage | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Action pinning is inconsistent

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision, obvious and narrowly scoped fix
- **Dimension**: Pattern Consistency
- **Location**: .github/workflows/ci.yml:23
- **Detail**: `actions/checkout` and `actions/setup-node` are pinned to commit SHAs, but `actions/setup-dotnet@v4` (lines 23 and 74) and `actions/upload-artifact@v4` (line 90) use mutable tags. `impl-review.yml` pins `setup-dotnet` only by tag as well, so this matches a sibling, but it is still mixed within one file.
- **Fix**: Pin `setup-dotnet` and `upload-artifact` to commit SHAs with a version comment, as done for the other actions.
- **Decision**: PENDING

### F2 — Cancel-in-progress also applies to pushes to main

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision, obvious and narrowly scoped fix
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/ci.yml:15
- **Detail**: The plan specifies "concurrency per ref, cancelling in-progress runs", and this matches it. On `main`, however, two quick pushes cancel the first push's gate run, so that commit never gets a recorded result.
- **Fix**: Use `cancel-in-progress: ${{ github.ref != 'refs/heads/main' }}`, or leave as is (conscious skip is fine).
- **Decision**: PENDING

### F3 — No job timeouts, and plan Progress is not updated

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision, obvious and narrowly scoped fix
- **Dimension**: Success Criteria
- **Location**: .github/workflows/ci.yml:53
- **Detail**: The `e2e` job (which starts the API and `ng serve`) and the other jobs have no `timeout-minutes`, so a hung dev server holds a runner for the 6h default. Separately, at review time the `backend` and `e2e` jobs were still running, so plan criteria 1.1 and 1.3 (green jobs, 136/33/2 test counts) were not yet verifiable. Progress checkboxes in `plan.md` are unchecked and `change.md` was `implementing`.
- **Fix**: Add `timeout-minutes` (e.g. 15 for e2e, 10 for the others), and tick the Progress items once the CI run is green.
- **Decision**: PENDING

<!-- End of report -->
