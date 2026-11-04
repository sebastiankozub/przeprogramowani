# Evidence: discussion

Sources: `gh` (sebastiankozub/przeprogramowani-MovieDate, read-only) + git + planning artifacts in `context/`. Window: whole history, 2026-11-01..2026-11-03 (HEAD 7756f44). Limits hit: none (3 PRs, 0 issues, 16 workflow runs, all fetched in one page). Baseline: **1 human**, 62/77 commits agent co-authored. Every number below describes a 3-day, single-author, course-workflow history; nothing here is a team-behaviour statistic.

Raw output: `.work/gh-prs.json`, `.work/gh-runs.json`, `.work/disc-percommit.tsv` (per-commit capability split, code/prose), `.work/disc-reviews.txt`, `.work/disc-markers.tsv`.

## Buzz per capability

PRs attributed via merge commit → `git diff --name-only ^1...^2` → `attribute.mjs`. Fix commits: all 5 are `fix(<slice>): impl-review fixes`, i.e. the agent review stage of the course workflow, not production incidents. Review findings = agent impl-/plan-review W(arnings)/O(bservations), never human review.

| Capability | Commits (code chg / prose chg) | PRs touching (files) | Human review | Reverts | Issues | Fix commits (own + spillover) | Agent review findings (W/O) | Plan artifacts | TODO/FIXME at HEAD |
|---|---|---|---|---|---|---|---|---|---|
| friends-messaging | 8 (32 / 17) | #2 (31 files, main subject), #3 (5) | 0 | 0 | 0 | 0 | local 2W/5O + CI 0W/2O | change, plan-brief, plan (416 l, largest), **research**, 2 impl-reviews | 0 |
| matching | 13 (35 / 12) | #2 (9), #3 (2) | 0 | 0 | 0 | 0 | **none — no review run** | change, plan-brief, plan (412 l) | 0 |
| match-recompute | 8 (7 / 12) | — | 0 | 0 | 0 | 0 own + 1 spillover | **none — no review run** | change, plan-brief, plan (305 l) | 0 |
| movie-ratings | 14 (46 / 16) | #3 (2) | 0 | 0 | 0 | 1 own + 2 spillover | 3W/7O (10 findings, most of any) | change, plan-brief, plan, impl-review | 0 |
| identity | 12 (26 / 17) | — | 0 | 0 | 0 | 1 own | impl 3W/5O + plan 1W/2O | change, plan-brief, plan, impl-review, **plan-review** | 0 |
| watchlist | 11 (26 / 15) | #3 (2) | 0 | 0 | 0 | 1 own + 1 spillover | 2W/6O | change, plan-brief, plan, impl-review | 0 |
| profile-city | 13 (37 / 15) | #2 (1), #3 (2) | 0 | 0 | 0 | 1 own | 1W/6O | change, plan-brief, plan, impl-review | 0 |
| platform | 30 (35 / 39) | #1 (4, main subject) | 0 | 0 | 0 | 1 own (+1 triage commit `ci(...)`) | prod-pg impl 1W/4O + plan 1W/2O; CI gates 0W/3O | prod-pg (5 files), testing-critical-path-e2e (research+plan), testing-ci-quality-gates (plan+review), bootstrap-verification | 0 |
| shared | 25 (116 / 0) | #2 (7), #3 (4) | 0 | 0 | 0 | 0 own + 4 spillover | — | — | 0 |
| docs | 34 (0 / 45) | #1, #2 | 0 | 0 | 0 | — | — | lessons (2), roadmap open questions (2), test-plan | — (markers exist only in prose) |

Cross-cutting commits (touching > 3 business capabilities): 2 (scaffold-era; `feat(spa)` loading indicators #3 touches 5 business capabilities' UI).

## Friction

- **No human review at all** (evidence). All 3 PRs merged by the author, 0 human reviews/comments; the only PR activity is `github-actions` posting the agent impl-review (PR #1, #2: `APPROVED`, 0 critical; PR #3: "no plan detected, skipping review"). Rule 7: automation, not review. PR #3 (cross-cutting UI, 5 business capabilities) merged with **no review of any kind**.
- **No closed-unmerged PRs, no reverts, no hotfixes, no issues** (evidence: `gh pr list` 3/3 merged; `gh issue list` empty; no `revert|hotfix` subject). CI: 16 runs, 16 success (CI 10, Deploy 3, Impl Review 3). No red pipeline in history.
- **Second attempts = impl-review fix round, every slice that had a review** (evidence; workflow stage): identity F1–F7, movie-ratings F1–F10, watchlist F1–F8, profile-city F1–F6, platform F2–F4. Findings were overwhelmingly fixed (FIXED 45, ACCEPTED/ACCEPTED-AS-RULE 4, SKIPPED 2, PENDING 5).
- **Recurring warning themes across slices** (evidence from finding titles; inference that they recur):
  - concurrency / race producing wrong status codes: watchlist F1 (lost commit ack → false 404), F3 (concurrent delete → misleading 404); profile-city F1 (concurrent Identity update can revert city), F5 (FK race → 500); movie-ratings F1 (deleted-but-cookied user → 500).
  - SPA state drift from server: identity F2 (session cached, no 401 handling), movie-ratings F2 (list drifts after 404/409), friends-messaging F3 (conversation page doesn't reload on userId change).
  - query logic in endpoints instead of `Services/`: movie-ratings F5, profile-city F7 (module-layout rule drift).
  - test-gap warnings: movie-ratings F3, watchlist F2 (atomic rollback untested), profile-city F3, friends-messaging F4.
- **Unreviewed high-criticality slices** (evidence): matching and match-recompute archives have no `reviews/` folder — no plan-review, no impl-review, no fix round. Absence of friction here is absence of review, not evidence of quality (inference).
- **Open review items** (evidence): friends-messaging CI review F1–F2 and testing-ci-quality-gates F1–F3 still `PENDING` in the review files; for the latter, commit eac884d's body says they were triaged (F2, F3 fixed; F1 accepted) — file not updated (bookkeeping drift, mechanical).
- **Bundle budget pressure** (evidence): watchlist F8 — initial bundle 499.77 / 500 kB; fixed by raising the warning budget to 600 kB; profile-city review later reports 502.25 kB. Shared/SPA concern.

## Planning attention

- Every business capability has a full plan (change + plan-brief + plan, 301–416 lines). Most planning per capability: **friends-messaging** (only slice with `research.md` and two impl-reviews; largest plan, 416 l; largest PR, +3514/−61), then **identity** and **platform** (only slices with a plan-review besides each other).
- Recent (2026-11-03): friends-messaging, matching, match-recompute, profile-city, watchlist, and both testing-* platform changes.
- `context/foundation/test-plan.md` risk map (5 risks): R1 recompute freshness (match-recompute + its writers movie-ratings/watchlist/profile-city), R2 privacy/ownership leaks (cross-cutting; friends-messaging, matching, profile-city), R3 ranking correctness (matching), R4 browser primary path (identity → matching), R5 failing test / incompatible migration reaching prod (platform). Rollout: Phase 1 e2e **complete**, Phase 2 CI gates **complete**, Phase 3 **"Privacy and ranking contract" (R2, R3) not started** — the open planned attention sits on matching and privacy.
- `lessons.md` (2 lessons, both born from impl-review findings): backward-compatible migrations (platform, from prod-pg F1); key per-user data on user id, not email claim (identity F8).
- Roadmap open questions (2, both owner = user, non-blocking): PRD `hard_deadline` 2026-11-04 vs working target 2026-12-06 (docs); OAuth in MVP? (identity). Parked list includes **error tracking/observability beyond `/health`** and separate recompute worker process (match-recompute).
- Unarchived change folders: testing-ci-quality-gates, testing-critical-path-e2e, bootstrap-verification (all platform).

## Debt markers

`grep -cE 'TODO|FIXME|HACK|XXX'` over every tracked non-noise code file per capability at HEAD: **0 in every capability** (0.00 per 1000 lines; ~16 kLOC scanned). Repo-wide, markers appear only in prose (`roadmap.md`, `tech-stack.md`, `deploy-plan.md`, an archived plan) and in `package-lock.json` (noise). Debt is tracked in review files and the roadmap's Parked list, not in code comments (inference).

## Mechanical signals

- **Fix spillover** (rule 12): movie-ratings' 3 fix commits include 2 spillover (set-home-city and want-to-watch-list fix rounds touched its files); watchlist 1 spillover (set-home-city); match-recompute 1 spillover (want-to-watch-list); shared 4 spillover. Only the own `fix(<slice>)` commit counts.
- **Fix share overall** (rule 5/7): all 5 fix commits are scheduled impl-review rounds — a workflow stage, not breakage. Not a risk signal on its own.
- **Prose-only commits** (rule 8): `record phase N SHA`, `close out plan (epilogue)`, `production verified`, `chore(archive)` — 3–5 per business slice, 12 platform, 15 docs. Bookkeeping lockstep of the course workflow; counted above as discussion, never as code activity.
- **docs/platform prose volume** (rule 4/8): docs 45 and platform 39 prose changes reflect the course scaffolding (test-plan, deploy plan, testing-* changes), not hot areas.
- **PENDING decisions** in testing-ci-quality-gates review: resolved per commit body; file stale.
- **Agent-authored review commits** (8570beb, f543f4c by `claude[bot]`): activity, not people, not review.

## Unknowns

- **Human review behaviour**: unknown — there is none to measure (single author, self-merged PRs). Review medians not computed.
- **Issue tracker**: GitHub issues empty; no other tracker referenced. User-reported bugs: unknown.
- **Production errors**: no error tracker in the repo (`git grep` for Sentry/Datadog/Rollbar/AppInsights/OpenTelemetry: no hits outside lock file); roadmap explicitly parks observability. Production failure history is unknown, not "none". Fly.io logs not inspected (out of scope).
- **Pre-PR history**: 70 of 77 commits went straight to `main` (only 7 arrived via PRs #1–#3) (PRs started 2026-11-03 12:31); discussion before that lives only in the planning artifacts.
- **Quality of matching/match-recompute**: unknown from discussion — never reviewed (see Friction).

## Commands run

- `git log --merges`; `git log $m^1..$m^2`, `git diff --name-only $m^1...$m^2 | node attribute.mjs` per merge
- `gh pr list --state all --limit 100 --json …`; `gh pr view N --json comments,reviews` (N=1..3); `gh issue list --state all`; `gh run list --limit 50`
- `node .work/disc-percommit.mjs` (parses `.work/git-log.txt` + `attribute.mjs`; per-capability commits, code/prose, fix, revert, cross-cutting)
- `grep` over `context/archive/*/reviews/*.md`, `context/changes/*/reviews/*.md` (findings, severities, decisions); `grep -c '- \[x\]'` over plans; headings of `roadmap.md`, `lessons.md`, `test-plan.md`
- `git ls-files | attribute.mjs` + `grep -cE 'TODO|FIXME|HACK|XXX'` per file; `git grep` for markers and error-tracker SDKs; `git show --stat eac884d`
