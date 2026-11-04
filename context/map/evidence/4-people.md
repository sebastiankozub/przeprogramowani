# Evidence: people

- Window: whole history, 2026-11-01..2026-11-03, 77 non-merge commits (scan contract). `evidence`
- Humans in window: **1** — the sole maintainer (two display names in history, one identity; the second name appears only on the 3 PR merge commits). `evidence`
- Agent-authored: 2 commits by `claude[bot]` (impl-review files posted by the CI review action: 8570beb, f543f4c) — counted as activity, not as a person. `evidence`
- Agent co-authored: 61 of 75 human commits (81%) carry a Claude co-author trailer; the contract's figure of 62 includes bot commit 8570beb, which also carries one. `evidence`
- **Baseline (false-signals #6): small team — concentration is the baseline.** One person authored every human commit in every capability; no bus-factor or concentration finding is possible from this history. `inference`
- Attribution: `attribute.mjs` + `capabilities.tsv`, noise excluded, rename destination path used, cross-cutting commits counted for every capability touched (contract rules). Unattributed volume: 0%.
- No CODEOWNERS file is tracked (`git ls-files | grep -i codeowners` → empty). `evidence`

## People per capability

All capabilities are high/medium criticality or in the top 5 by commits, so all are listed. Only one person to list; the useful split is the topic groups and the period.

| Capability | Commits (code) | Person | Topic groups (from subjects) | Active period |
|---|---|---|---|---|
| platform | 30 (17) | sole maintainer | initial scaffold + first Fly deploy, manual deploy workflow, Postgres/EF wiring + Neon release command, integration-test project, Playwright setup, CI quality gates, agent PR review | 11-01..11-03 |
| shared | 25 (25) | sole maintainer | app wiring/routes touched by every slice's p1/p2 commits, SPA loading indicators, impl-review fix rounds | 11-01..11-03 |
| movie-ratings | 14 (9) | sole maintainer | catalog + ratings API, /movies screen, impl-review fixes F1–F10, shared movie search reused by watchlist | 11-02..11-03 |
| matching | 13 (10) | sole maintainer | scoring + match API, Matches schema + display name, /matches screen, demo accounts + production verification | 11-03 |
| profile-city | 13 (8) | sole maintainer | Polish city catalog + profile API, /profile city picker + home nudge, impl-review fixes F1–F6 | 11-03 |
| identity | 12 (8) | sole maintainer | backend Identity + auth API, Angular Material auth screens + guard, impl-review fixes F1–F8, test-user scripts | 11-02..11-03 |
| watchlist | 11 (6) | sole maintainer | watchlist API with atomic mark-watched, /watchlist screen, impl-review fixes F1–F8 | 11-03 |
| friends-messaging | 8 (5) | sole maintainer | friends + conversations backend, SPA friends/conversation pages, E2E messaging journey | 11-03 |
| match-recompute | 8 (2) | sole maintainer | timestamps + background recompute worker (one commit), touched again by watchlist review fixes | 11-03 |
| docs | 34 (0) | sole maintainer | foundation docs, roadmap, archive moves (prose only — discussion, not activity, false-signals #8) | 11-01..11-03 |

## Concentration

Small team — concentration is the baseline (1 human). Every capability has been touched by exactly one person; that is the team-wide norm, not a finding. `inference`

## Agent co-authorship

| Capability | Human commits | Agent co-authored | Agent-free human | Agent-free human touching code | Agent-authored (bot) |
|---|---|---|---|---|---|
| platform | 29 | 24 (83%) | 5 | 3 (initial scaffold, first deploy, manual deploy workflow) | 1 |
| shared | 25 | 23 (92%) | 2 | 2 (initial scaffold, first deploy) | 0 |
| movie-ratings | 14 | 13 (93%) | 1 | **0** | 0 |
| matching | 13 | 12 (92%) | 1 | **0** | 0 |
| profile-city | 13 | 12 (92%) | 1 | **0** | 0 |
| identity | 12 | 11 (92%) | 1 | **0** | 0 |
| watchlist | 11 | 10 (91%) | 1 | **0** | 0 |
| friends-messaging | 7 | 6 (86%) | 1 | **0** | 1 |
| match-recompute | 8 | 7 (88%) | 1 | **0** | 0 |
| docs | 34 | 22 (65%) | 12 | 0 (prose only) | 0 |

- The single agent-free commit in each business capability is the `chore(archive): close <slice>` move — prose only. **All seven business capabilities have zero agent-free human code commits**: every line of product code arrived in an agent co-authored commit. `evidence`
- Agent-free human code exists only in platform/shared, and only in the first-day scaffold and deploy commits. `evidence`
- Context, not a filter: the human steered every change (course workflow: research → plan → implement → impl-review). Co-authorship says nothing about review depth. `inference`

## Implications

- Before changing any capability, the person to ask is the sole maintainer; the better "person-like" source is the per-slice change folder (`context/archive/<date>-<slice>/` — research, plan, impl-review), which records what was tried and why. `inference`
- Matching / match-recompute: read the ranked-match-list and async-match-recompute archive folders first; match-recompute's code arrived in one commit (3edc850) plus a watchlist review fix (b7e11e4), so its reasoning lives almost entirely in plan/review prose. `inference`
- PRs to read: #1 testing-ci-quality-gates (CI gates + agent review), #2 first-message-to-friend (friends & messaging, includes a bot impl-review), #3 loading-indicators (cross-cutting SPA change). Earlier slices were committed directly without PRs. `evidence`
- Because effectively all product code is agent co-authored, knowledge of *why* a detail exists may sit in plan/review files rather than in the maintainer's memory — check the docs before assuming intent. `inference`

## Unknowns

- Whether anyone besides the sole maintainer will maintain this code (course project; no team roster). `unknown`
- Formal ownership: no CODEOWNERS, no ownership doc found by command. `unknown`
- Review depth of agent co-authored commits — trailers show assistance, not who verified what. `unknown`
- Forge-side reviewers/approvers on PRs #1–#3 not queried here (discussion agent's scope). `unknown`

Commands run:
- `cat` brief, scan contract, capabilities.tsv, false-signals.md; `head` of `.work/git-log.txt` and `.work/change-caps.tsv`
- `grep '^@' .work/git-log.txt | cut -f2 | sort | uniq -c` (authors); `git log --merges --format='%h %aN'` and `--format='%h %ad %s'`
- `git ls-files | grep -i codeowners`; `ls .github`
- `node .work/people.mjs > .work/people-raw.txt` (parses git-log.txt, attributes paths via `attribute.mjs capabilities.tsv`, counts human/agent-co-authored/agent-free/bot commits per capability)
