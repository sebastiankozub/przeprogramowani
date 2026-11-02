# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Keep migrations backward-compatible with the previous release

- **Context**: fly.toml:9 — `[deploy] release_command = "dotnet movie-date.dll --migrate"`; EF Core migrations in `Data/Migrations`, applied by a Fly release machine before 2 web machines roll one by one.
- **Problem**: Migrations run before rollout and a Fly image rollback never reverts the schema, so the previous release's code and the new code share one schema during every deploy and after every rollback. A destructive or non-additive migration (drop/rename column, NOT NULL without default) breaks the still-running old machines mid-deploy and makes image rollback unsafe. No incident yet — InitialCreate is empty; S-01 (Identity tables) is the first real migration.
- **Rule**: Every migration must be backward-compatible with the previous release (expand/contract). Add nullable columns or new tables first; drop, rename, or tighten constraints only in a later deploy, after no running code depends on the old shape.
- **Applies to**: plan, plan-review, implement, impl-review

## Key per-user data on the user id, never on the email claim

- **Context**: Endpoints/AuthEndpoints.cs:82 — `GET /api/auth/me` reads `ClaimTypes.Email` from the Identity cookie principal.
- **Problem**: The email claim is a login-time snapshot (refreshed only on security-stamp validation, ~30 min) and the email itself can change. Fine for display, but S-02+ (ratings, want-to-watch, city, matches, messages) will be tempted to look up or store per-user rows by email — that breaks on email change and invites case/normalization mismatches.
- **Rule**: Identify the current user by `ClaimTypes.NameIdentifier` (`UserManager.GetUserId(User)` / `AppUser.Id`) in endpoints and services; foreign keys to users point at `AspNetUsers.Id`. Email is for display only.
- **Applies to**: plan, implement, impl-review
