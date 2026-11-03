---
change_id: testing-ci-quality-gates
title: Quality gates in CI (test rollout phase 2)
status: impl_reviewed
created: 2026-11-03
updated: 2026-11-03
archived_at: null
---

## Notes

Rollout Phase 2 of context/foundation/test-plan.md: "Quality gates in CI". Risk covered: #5 (a change with a failing test or a backward-incompatible migration reaches production because nothing runs the suite before deploy). Delivered as a pull request so the agent review workflow (.github/workflows/impl-review.yml, 10xChampion M5L3) reviews it against this plan.

## Review triage (impl-review CI on PR #1, verdict APPROVED)

- F3 (no job timeouts) — fixed on main: timeout-minutes 15 backend / 10 frontend / 20 e2e.
- F2 (cancel-in-progress also cancels main runs) — fixed on main: cancel only when ref is not refs/heads/main.
- F1 (setup-dotnet / upload-artifact pinned to tags, not SHAs) — accepted risk: official GitHub actions, low impact; revisit when the repo adopts a pinning policy (e.g. Dependabot for actions).
