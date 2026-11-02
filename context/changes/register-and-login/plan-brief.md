# Register and Login — Plan Brief

> Full plan: `context/changes/register-and-login/plan.md`

## What & Why

Roadmap slice S-01: the first user-facing flow. A visitor registers with email + password, logs in, sees a gated home page and can log out; every product route requires login (PRD FR-001, §Access Control). Every later slice (movies, city, matches, messages) is per-user, so identity comes first.

## Starting Point

F-01 delivered EF Core + Neon Postgres, migrations via Fly's release command, and a Testcontainers test fixture. The Angular app is the bare CLI shell (no routes, no HTTP). Production runs 2 Fly machines with ephemeral containers.

## Desired End State

On https://moviedate-sk.fly.dev a new user can register, is logged in on a home page showing their email, stays logged in across reloads and deploys, and can log out; opening any gated route while logged out redirects to login and back after signing in.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Session mechanism | `MapIdentityApi` + HttpOnly, SameSite=Strict cookie under `/api/auth`, plus own `logout` and `me` | Same-origin SPA — no token in JS, no CORS, least code |
| Data Protection keys | Persisted in Postgres (`PersistKeysToDbContext`) | 2 machines + ephemeral containers would otherwise reject each other's cookies and log everyone out on deploy |
| User model | `AppUser : IdentityUser` (empty) | Later slices add profile fields additively without changing the generic type |
| Passwords | Min 8 chars, no class rules, default lockout | NIST-style, simpler UX and tests |
| UI | Angular Material + reactive forms | Ready, accessible controls reused by later slices (autocomplete, lists) |
| Tests | API integration tests (Testcontainers) + Vitest guard test; browser flow manual | Automates the security boundary with existing infra |
| API unauthenticated | 401/403 instead of login redirects | SPA must see errors, not HTML |

## Scope

**In scope:** Identity tables + Data Protection keys migration (additive), `/api/auth/register|login|logout|me`, cookie hardening, integration tests, Angular Material, dev proxy, `AuthService`, login/register/home screens, `authGuard` + test, production deploy and verification.

**Out of scope:** email confirmation/password reset/2FA UI, OAuth, profile fields, roles, bearer tokens, breached-password checks, Playwright e2e, CI tests.

## Architecture / Approach

`Endpoints/AuthEndpoints.cs` provides `AddAppAuth()` and `MapAuthEndpoints()` (same pair pattern as health endpoints); `Program.cs` only wires them plus `UseAuthentication/UseAuthorization`. The SPA calls `/api/auth/*` same-origin (dev via `proxy.conf.json`), keeps the current user in a signal, and protects routes with a functional guard that redirects to `/login?returnUrl=…`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — Identity + API | Auth endpoints, migration, DP keys in DB, integration tests | Cookie redirects (302) instead of 401 for API calls |
| 2. Frontend — Material + screens | Login/register/home, guard, dev proxy | Dev proxy/cookie mismatch between ports |
| 3. Production | Verified flow on 2 machines | Cookie not shared across machines if DP keys misconfigured; `Secure` flag behind Fly's TLS proxy |

**Prerequisites:** F-01 done (it is); Docker running; Node for `ClientApp`; human runs the Fly deploy.
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- `MapIdentityApi` also exposes reset/2FA/confirm endpoints without a configured email sender — reachable but inert; accepted for MVP.
- Fly trial stops machines after 5 min; a cold start delays the first `/api/auth/me` but doesn't affect correctness.
- Data Protection keys are unencrypted at rest in Neon — database access implies cookie forgery; accepted for MVP.
- No rate limiting on register/login beyond per-account lockout; duplicate-email registration reveals that an account exists — accepted for MVP.
- Fly trial keeps machines stopped, so normal reloads may never reach the second machine — Phase 3 proves shared keys explicitly with `fly-force-instance-id` per machine and a restart check.

## Success Criteria (Summary)

- A new user can register, stay logged in across reloads and deploys, and log out on production.
- Logged-out visitors cannot reach `/` or `/api/auth/me` (redirect / 401).
- `dotnet test` and `ng test` pass, including 401-not-302 and password-policy cases.
