---
change_id: testing-critical-path-e2e
title: Critical-path browser coverage (test rollout phase 1)
status: implemented
created: 2026-11-03
updated: 2026-11-03
archived_at: null
---

## Notes

Rollout Phase 1 of context/foundation/test-plan.md: "Critical-path browser coverage".
Risks covered: #1 (match list does not update within ~10 s after a rating/watchlist/city write without a click), #4 (primary path breaks in a real browser while API tests stay green). Test types planned: e2e (Playwright).
Risk response intent: #1 — after a rating write, /matches shows the new or re-scored candidate by itself and the pending indicator clears; #4 — a fresh user completes register -> name + city -> rate a movie -> /matches in a real browser against the real app and database.
Certification: this phase delivers the 10xBuilder "test set addressing a test-plan risk from the user's perspective" (deadline 2026-11-04).
