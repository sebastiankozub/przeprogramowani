<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Friends and first message (S-07 + S-08)

- **Plan**: `context/changes/first-message-to-friend/plan.md`
- **Scope**: Full plan (CI review on PR #2)
- **Date**: 2026-11-03
- **CI run**: https://github.com/sebastiankozub/przeprogramowani/actions/runs/41700285412
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 2 observations

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

Automated checks re-run in CI:

- `dotnet build movie-date.csproj`: 0 warnings, 0 errors.
- `dotnet test tests/MovieDate.Tests`: 147/147 passed.
- `npx ng test --watch=false`: 53/53 passed.
- `npm run build`: succeeded.
- Playwright e2e (`messaging.spec.ts`) was not re-run here. It is gated by the `e2e` job in `ci.yml`.

Review notes:

- The pair gate (`CanTalkAsync`) runs before body validation on POST. Both endpoints sit in groups with `RequireAuthorization()`, and the caller id comes from `users.GetUserId`.
- Responses expose only the display name and city, never the email or the caller's own id.
- All ten named integration tests from the plan exist and use the no-recompute fixture.
- The migration is additive. Concurrent-add and FK races are handled (409 and 404).

## Findings

### F1 — Unplanned supporting changes

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: scripts/demo-accounts.mjs
- **Detail**: The diff also touches `scripts/demo-accounts.mjs` (about 140 lines), `ClientApp/src/styles.css`, `ClientApp/e2e/support/profile.ts`, a refactor of `ClientApp/e2e/support/partner.ts`, `roadmap.md`, and an extra test `Own_id_conversation_returns_404`. None is in the plan's Changes Required, but none is excluded. They support step 3.4 (production demo accounts) and the e2e helpers, so they are benign.
- **Fix**: Mention the demo-accounts script in the plan or PR description so reviewers know it is intentional.
- **Decision**: PENDING

### F2 — Delivery steps still pending

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/first-message-to-friend/plan.md
- **Detail**: Progress items 3.2 (green CI jobs), 3.3 (this review) and 3.4 (production check with a third-account 404) are unchecked. Item 3.1 is checked without a commit SHA. These are expected pending items for an open PR, not failures.
- **Fix**: Tick 3.2 and 3.3 after merge-readiness, and 3.4 after the production deploy.
- **Decision**: PENDING

<!-- End of report -->
