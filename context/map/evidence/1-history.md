# Evidence: history

- Window: whole history, effective 2026-11-01..2026-11-03 (HEAD 7756f44). Buckets: calendar day (contract deviation; ISO week would be one bucket).
- Commits counted: 77 non-merge (3 merges ignored). Bots excluded: 0. Mass excluded: 0.
- Agent-authored: 2 (`claude[bot]`, CI impl-review files 8570beb, f543f4c). 62/77 human commits carry a Claude co-author trailer — effectively the whole codebase is agent-assisted.
- Humans: 1. Every concentration figure is a baseline, not a finding (false-signals #6).
- Attribution: `attribute.mjs` + `capabilities.tsv` only; renames counted under the new path (folds `context/changes/<slice>` → `context/archive/<date>-<slice>`). Noise excluded: 198 changes. Counted: 548 changes, **0 unmapped**.
- Raw output: `context/map/.work/history-analysis.txt`, `context/map/.work/history-code-pairs.txt`.

## Activity per capability

| Capability | Changes | code / prose | Commits (any) | Commits (code) | Share of changes | Agent-authored commits |
|---|---|---|---|---|---|---|
| shared (catch-all) | 116 | 116 / 0 | 25 | 25 | 21.2% | 0 |
| platform | 74 | 35 / 39 | 30 | 17 | 13.5% | 1 |
| movie-ratings | 62 | 46 / 16 | 14 | 9 | 11.3% | 0 |
| profile-city | 52 | 37 / 15 | 13 | 8 | 9.5% | 0 |
| friends-messaging | 49 | 32 / 17 | 8 | 5 | 8.9% | 1 |
| matching | 47 | 35 / 12 | 13 | 10 | 8.6% | 0 |
| docs (prose) | 45 | 0 / 45 | 34 | 0 | 8.2% | 0 |
| identity | 43 | 26 / 17 | 12 | 8 | 7.8% | 0 |
| watchlist | 41 | 26 / 15 | 11 | 6 | 7.5% | 0 |
| match-recompute | 19 | 7 / 12 | 8 | 2 | 3.5% | 0 |
| unmapped | 0 | — | 0 | 0 | 0% | — |

evidence: business capabilities are near-uniform (41–62 changes, 8–14 commits) because each was built as exactly one roadmap slice with the same fixed workflow (p1 backend, p2 SPA, p3 prod verify, epilogue, impl-review fix, archive). Volume reflects slice size, not churn after delivery — no capability has been touched by a later, unrelated feature commit except via the cross-cutting commits named below.
inference: match-recompute's low code volume (7 code changes, 2 code commits) understates it — its whole backend landed in one 22-file commit (3edc850) that also edited the write endpoints of movie-ratings, watchlist and profile-city. Its own commit count is dominated by bookkeeping ("record phase SHA", epilogue, archive).
evidence (attribution caveat): `Contracts/MatchStatusResponse.cs` is listed under match-recompute in `capabilities.tsv` but the helper assigns it to **matching** (glob `Contracts/*Match*` beats the exact-path prefix). Reported, not corrected.

## Top files in top capabilities

Counts are changes in the window. `plan.md` files under `context/changes/` were moved to `context/archive/` (see freshness).

| Capability | Top code files (changes) | Top prose |
|---|---|---|
| shared | `Program.cs` 11, `ClientApp/src/app/app.html` 8, `ClientApp/src/styles.css` 8, `Data/Migrations/AppDbContextModelSnapshot.cs` 8 (mechanical), `movie-date.http` 8, `ClientApp/src/app/app.routes.ts` 7, `Data/AppDbContext.cs` 7, `ClientApp/src/app/home/home.component.html` 6 | — |
| platform | `.gitignore` 4, `appsettings.json` 3, `Data/MigrationRunner.cs` 3 | `context/deployment/deploy-plan.md` 10, prod-postgres-connection plan 4 |
| movie-ratings | `ClientApp/src/app/movies/movies.component.ts` 5, `movies.service.ts` 4, `Endpoints/MoviesEndpoints.cs` 4, `Endpoints/WatchedMoviesEndpoints.cs` 3, `tests/MovieDate.Tests/MoviesTests.cs` 3 | rate-watched-movies plan 7 |
| profile-city | `ClientApp/src/app/profile/profile.component.ts` 4, `Endpoints/ProfileEndpoints.cs` 3, `Services/ProfileService.cs` 3, `profile.service.ts` 3, `ProfileTests.cs` 3 | set-home-city plan 6 |
| matching | `ClientApp/src/app/matches/matches.component.{html,ts}` 4 each, `Services/MatchService.cs` 3, `matches.service.ts` 3 | ranked-match-list plan 6 |
| friends-messaging | `ClientApp/src/app/friends/*` 2 each (conversation, friends components) | first-message-to-friend plan 4 |
| identity | `Data/AppUser.cs` 4, `ClientApp/src/app/auth/{auth.service,login.component,register.component}.ts` 2 each | register-and-login plan 6 |
| watchlist | `Endpoints/WatchlistEndpoints.cs` 4, `watchlist.component.ts` 4, `Services/WatchlistService.cs` 3, `WatchlistTests.cs` 3 | want-to-watch-list plan 6 |

evidence: `Data/AppUser.cs` (identity) is the most-touched identity code file — edited by the identity slice and again by ranked-match-list (display name) and async-match-recompute (recompute stamps), i.e. the user entity accretes fields from downstream capabilities.

## Trend

Commits per day (11-01 / 11-02 / 11-03) | changes per day.

| Capability | Commits | Changes | Label | Campaign |
|---|---|---|---|---|
| identity | 0 / 7 / 5 | 0 / 36 / 7 | fading | register-and-login slice (11-02); 11-03 touches are spillover from ranked-match-list, recompute and e2e setup |
| movie-ratings | 0 / 7 / 7 | 0 / 41 / 21 | fading | rate-watched-movies slice (11-02); 11-03 = spillover (shared movie search, set-home-city fix, recompute triggers) |
| watchlist | 0 / 0 / 11 | 0 / 0 / 41 | rising (new) | want-to-watch-list slice |
| profile-city | 0 / 0 / 13 | 0 / 0 / 52 | rising (new) | set-home-city slice |
| matching | 0 / 0 / 13 | 0 / 0 / 47 | rising (new) | ranked-match-list slice + friends slice reuse |
| match-recompute | 0 / 0 / 8 | 0 / 0 / 19 | rising (new) | async-match-recompute slice |
| friends-messaging | 0 / 0 / 8 | 0 / 0 / 49 | rising (new) | first-message-to-friend slice (last feature) |
| platform | 1 / 13 / 16 | 5 / 41 / 28 | constant | 11-02 deploy + Postgres; 11-03 testing-critical-path-e2e + CI quality gates + agent PR review (course lessons M3L4, M5L3) |
| shared | 1 / 9 / 15 | 3 / 67 / 46 | constant | rides along every slice; last commit 19f9c1e loading indicators touches 6 capabilities' SPA views |
| docs | 3 / 12 / 19 | 10 / 15 / 20 | constant | course workflow (roadmap, test plan, lessons) |

inference: with three daily buckets and one slice per capability, "rising/fading" just reflects build order of the roadmap (S-01..S-08); no capability shows rework after its slice closed beyond the cross-cutting commits listed in Fix pressure and Co-change. **seasonal**: not applicable at this window length.

## Fix pressure

Heuristic: subject matches `\b(fix|fixes|fixed|bug|hotfix|revert|regression|broken|incident)\b` (case-insensitive); commit counted for every capability it touches. Reverts: **0** in the whole history. Cross-cutting fix commits (> 3 business capabilities): **0**.

**All 5 fix commits are `fix(<slice>): impl-review fixes F1–Fn`** — findings from the course workflow's agent review step, applied before the slice was archived. They are a workflow stage, **not production incidents**. No commit subject references a bug, incident, hotfix or regression.

| Capability | Fix commits / commits | Share | Own-slice review fix | Spillover from another slice's fix |
|---|---|---|---|---|
| movie-ratings | 3 / 14 | 21% | 2302983 (F1–F4, F6–F10) | 4689d48 set-home-city (7 files: `Endpoints/LikePattern.cs`, `MoviesEndpoints.cs`, `movie-search.component.ts`, `movies.service.ts`, tests); b7e11e4 want-to-watch-list (1 file: `Services/WatchedMoviesService.cs`) |
| watchlist | 2 / 11 | 18% | b7e11e4 (F1–F8) | 4689d48 set-home-city (3 SPA files) |
| shared | 4 / 25 | 16% | — | all four (`Program.cs`, `api-errors.ts`, `home.component.*`, `app.config.ts`, `angular.json`, `movie-date.http`, test fixture) |
| match-recompute | 1 / 8 | 13% | — | b7e11e4 (`Services/Clock.cs`) — the recompute slice did not exist yet; Clock.cs was introduced here and later folded into the capability |
| identity | 1 / 12 | 8% | 7cedbbb (F1–F7) | — |
| profile-city | 1 / 13 | 8% | 4689d48 (F1–F6) | — |
| platform | 2 / 30 | 7% | ea27938 (F2–F4) | 2302983 (`appsettings.json`) |
| docs | 2 / 34 | 6% | — | `lessons.md` written by two review-fix commits |
| matching | 0 / 13 | 0% | — | — |
| friends-messaging | 0 / 8 | 0% | — | — |

- evidence: after false-signals #12, movie-ratings' 21% is 1 own-slice fix + 2 spillovers. The spillover from set-home-city is substantive (7 movie-ratings files): the city-search work reused and then fixed the movie search path (`LikePattern.cs`, shared movie-search component) — **inference**: a shared search/LIKE-escaping helper lives inside movie-ratings and is depended on by profile-city.
- evidence: matching and friends-messaging show 0% because **no fix commit was recorded for their slices**, not because they were clean. ranked-match-list and async-match-recompute have no `reviews/impl-review*.md` in history at all; first-message-to-friend has CI (8570beb) and local review files but no `fix(...)` commit. → `unknown`, not "low fix pressure".
- false-signals #5: platform's fixes are a review pass on the Postgres/health wiring (Dockerfile, `HealthEndpoints.cs`, `Program.cs`); CI/e2e work had no trial-and-error fix commits. Not demoted, but low.

## Co-change between capabilities

Contract rule: counted commits, dropping commits touching > 5 of 10 capabilities (all paths: 3 dropped — 3edc850 async-match-recompute backend, 2b0f208 set-home-city p1, 19f9c1e SPA loading indicators; code-only: 2 dropped). Confidence = shared ÷ commits of the less active side. Code-only pairs shown (prose-only pairs reflect planning docs; docs+platform 13, docs+shared 12 are workflow, #8).

| Pair (code paths only) | Support | Confidence | Reading |
|---|---|---|---|
| platform + shared | 8 | 0.47 | `Program.cs` / `movie-date.csproj` wiring with deploy/DB/test infra — mechanical wiring (#4) |
| identity + shared | 6 | 0.75 | SPA shell (`app.config.ts`, `home.component`, routes) + interceptor/guard |
| movie-ratings + shared | 6 | 0.67 | routes, `app.html`, styles, `api-errors.ts` |
| matching + shared | 6 | 0.60 | routes, styles, `AppDbContext`/snapshot |
| shared + watchlist | 4 | 0.67 | routes/shell |
| profile-city + shared | 5 | 0.63 | routes/shell/home nudge |
| **friends-messaging + matching** | 3 | 0.60 | friends slice edited `Services/MatchService.cs`, `Contracts/MatchResponse.cs`, `TestPairs.cs`, and the `matches` SPA component (add-friend from match list); plus demo-accounts script |
| **movie-ratings + watchlist** | 3 | 0.50 | shared movie search component + atomic mark-watched writing WatchedMovies |
| matching + profile-city | 3 | 0.38 | display name / distance input, home city in match scoring |
| movie-ratings + platform | 3 | 0.33 | `appsettings.json`, seed scripts, production verification |
| match-recompute + {shared, watchlist, movie-ratings} | 1 each | 0.50 | only b7e11e4 (`Clock.cs`) — see below |

- evidence: shared-catch-all pairs dominate (#2/#4: the SPA shell and DI wiring are touched by every slice by design — fan-in, not a hidden contract).
- evidence (blind spot of the drop rule): the strongest real coupling is invisible in the table. **3edc850** (async-match-recompute backend, 9 capabilities, dropped) wired recompute triggers into `Endpoints/WatchedMoviesEndpoints.cs`, `Endpoints/WatchlistEndpoints.cs`, `Endpoints/ProfileEndpoints.cs` and `Endpoints/MatchesEndpoints.cs`, and added stamps to `Data/AppUser.cs`. inference: movie-ratings, watchlist and profile-city writes are runtime-coupled to match-recompute; co-change support for those pairs is 1 only because the coupling was introduced in a single commit.
- Business-capability pairs with real support are few (≤ 3) — expected for 3 days of slice-by-slice build.

## Hub files and freshness

Hub = file co-changing with the most distinct partner capabilities (partner seen in ≥ 2 commits, after the > 5-capability drop).

| File | Capability | Commits | Partners | What it is | Verdict |
|---|---|---|---|---|---|
| `context/foundation/roadmap.md` | docs | 15 | 8 | roadmap; each slice opens/closes here | prose (#8) — discussion, not code hub |
| `movie-date.http` | shared | 6 | 5 | manual HTTP request file, one block per endpoint | workflow lockstep, no build guard (#3 not satisfied, but low risk — not executed) |
| `Data/AppDbContext.cs` | shared | 6 | 4 | EF DbContext; every new table registers here | config-as-code fan-in (#2), compile-guarded |
| `Data/Migrations/AppDbContextModelSnapshot.cs` | shared | 6 (8 changes) | 4 | EF model snapshot | **mechanical** (#3, #7) — regenerated in lockstep with each migration |
| `Program.cs` | shared | 10 (11 changes) | 3 | DI + endpoint mapping | fan-in by design (#2); compile-guarded |
| `ClientApp/src/app/app.html`, `app.routes.ts` | shared | 7 each | 3 | SPA nav + route table | route table, config-as-code (#2) |
| `ClientApp/src/app/home/home.component.html` | shared | 6 | 3 | home screen, nudges for each feature | UI fan-in |
| `ClientApp/src/styles.css` | shared | 7 (8 changes) | 2 | global styles | UI polish fan-in |
| `Data/AppUser.cs` | identity | 4 | — | Identity user entity, receives display name + recompute stamps | inference: genuine cross-capability entity (identity ↔ matching ↔ match-recompute); not mechanical |

Freshness (`git cat-file -e HEAD:<path>`): all code files named in this report exist at HEAD. **Moved (history that no longer exists at that path, #11)**: `context/changes/{register-and-login,rate-watched-movies,want-to-watch-list,set-home-city,ranked-match-list,async-match-recompute,prod-postgres-connection,first-message-to-friend}/plan.md` → `context/archive/<date>-<slice>/`. No source files deleted or moved.

## Mechanical signals

- `Data/Migrations/*.Designer.cs` + `AppDbContextModelSnapshot.cs`: EF lockstep, regenerated per migration (#3, #7). Kept in counts, excluded from risk.
- `Program.cs`, `AppDbContext.cs`, `app.routes.ts`, `app.html`: registration fan-in by design (#2) — every slice must touch them.
- `roadmap.md`, `CLAUDE.md`, `deploy-plan.md`, per-slice `plan.md`/`change.md`: prose churn (#8); 39 of platform's 74 changes are prose.
- "fix" share: all 5 fix commits are workflow review-fix commits, not incidents; spillover of 4689d48 and b7e11e4 into movie-ratings, watchlist, match-recompute and shared reattributed (#12).
- Concentration: 1 human, 62/77 commits agent co-authored — baseline (#6, #7).
- Bookkeeping commits ("record phase N SHA", "close out plan (epilogue)", "chore(archive)") inflate every business capability's commit count by ~4–6 with 1–6 file prose-only changes.
- `c9eed1a` (46 files) and `2886322` (46 files): vendored `.claude/**` tooling, almost entirely noise; only `.github/workflows/impl-review.yml` counted.

## Unknowns

- unknown: fix pressure for matching and friends-messaging — no review-fix commits exist for them; the 0% reflects a missing workflow step, not quality.
- unknown: whether the backend contracts (`Contracts/*`) and their hand-written TypeScript twins in `ClientApp/src/app/<feature>/*.service.ts` stay in sync — they co-change only inside each slice commit; there is no generated client, so drift after delivery would not show in co-change. Same for the `movie-date.http` request file.
- unknown: true runtime coupling of rating/watchlist/profile writes to match-recompute — visible in only one dropped commit (3edc850); co-change cannot measure it. Structure evidence (call graph) needed.
- unknown: post-release behaviour — 3-day history, no production incident, issue or hotfix trail exists; history cannot speak to production fragility.
- unknown: attribution of `Contracts/MatchStatusResponse.cs` (helper says matching, inventory says match-recompute).
- Tooling: no install performed; `git log` + Node script used. A dedicated co-change tool (e.g. code-maat) would add temporal-coupling windows, but at 77 commits it would add little.

## Commands run

- `grep '^@' .work/git-log.txt | ...` (author/day counts, subject list)
- `node <scratchpad>/hist.mjs` → parses `.work/git-log.txt`, attributes via `node .work/attribute.mjs .work/capabilities.tsv`, writes `.work/history-analysis.txt` (activity, trend, fix, co-change, hubs, top files, per-commit caps)
- `node <scratchpad>/code-pairs.mjs` → code-only co-change from `.work/change-caps.tsv`, writes `.work/history-code-pairs.txt`
- `git -c core.quotepath=off show --name-only --format= <sha> | node .work/attribute.mjs .work/capabilities.tsv` for 4689d48, b7e11e4, 2302983, 7cedbbb, ea27938, c9eed1a, e6f9fa6, 14407d3, 68cc48a, 3edc850
- `git -c core.quotepath=off log --name-only -- 'context/changes/*/reviews/*' 'context/archive/*/reviews/*'`
- `git cat-file -e HEAD:<path>` for every file named above
