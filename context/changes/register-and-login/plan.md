# Register and Login Implementation Plan

## Overview

Roadmap slice **S-01** (`register-and-login`): a visitor can register with email + password, log in, log out, and land on a gated home page; an unauthenticated visitor hitting any product route is sent to login. Backend uses ASP.NET Core Identity (`MapIdentityApi` with cookie sessions) on the existing EF Core + Neon setup from F-01; frontend gets Angular Material, auth screens and a route guard. PRD refs: FR-001, US-01, NFR-03, §Access Control.

## Current State Analysis

- Backend after F-01: `AppDbContext : DbContext` (empty, `Data/AppDbContext.cs`), `AddAppDatabase` (`Data/DataServiceExtensions.cs`), `--migrate` mode via `MigrationRunner`, `Endpoints/HealthEndpoints.cs` (`AddAppHealthChecks` + `MapHealthEndpoints`), `Program.cs` holds only wiring; SPA fallback `app.MapFallbackToFile("index.html")` is the last route. No auth middleware.
- Tests: `tests/MovieDate.Tests` (xUnit v2 + Testcontainers Postgres 17 + `WebApplicationFactory<Program>`, fixture migrates once via `MigrationRunner.RunAsync`, connection string injected with `UseSetting`).
- Frontend: `ClientApp/` is the bare Angular 22 CLI shell — `routes = []` (`src/app/app.routes.ts`), `app.config.ts` has no `provideHttpClient`, `app.html` is the CLI placeholder, no proxy config, unit tests via Vitest (`ng test`). Built into `wwwroot` by the Dockerfile; served same-origin in production.
- Production: Fly `moviedate-sk`, **2 web machines**, ephemeral containers, `release_command` runs `--migrate` before rollout. Fly trial stops machines after 5 min.
- Decisions: ASP.NET Core Identity (`tech-stack.md`, 2026-11-02); email/phone verification deferred past MVP (FR-001 Socratic); OAuth parked (roadmap Open Question 2).
- `context/foundation/lessons.md`: migrations must be backward-compatible with the previous release (expand/contract).

## Desired End State

- `POST /api/auth/register` creates an account; `POST /api/auth/login?useCookies=true` sets an HttpOnly, SameSite=Strict auth cookie; `GET /api/auth/me` returns the signed-in user's email (401 without a valid cookie); `POST /api/auth/logout` clears the cookie.
- Passwords: minimum 8 characters, no character-class rules; Identity's default lockout (5 failures / 5 min); passwords stored hashed by Identity (NFR-03).
- Data Protection keys live in Postgres, so a cookie issued by one Fly machine is accepted by the other and survives deploys/restarts.
- SPA: `/register` and `/login` screens (Angular Material), a gated `/` home showing "logged in as <email>" with a logout button; any gated route visited without a session redirects to `/login?returnUrl=…`, and after login the user returns to that URL.
- Verified on production at https://moviedate-sk.fly.dev.

### Key Discoveries:

- 2 Fly machines + ephemeral containers → default Data Protection keys are per machine and lost on restart; cookies would fail across machines and every deploy would log everyone out. Persist keys to the database (`PersistKeysToDbContext`).
- Identity's cookie handler redirects unauthenticated requests (302 to `/Account/Login`) by default; API calls must get 401/403 so the SPA can react.
- `MapIdentityApi` has no logout endpoint — add one.
- Fly terminates TLS; the container sees plain HTTP, so the cookie's `Secure` flag must be forced (`CookieSecurePolicy.Always`) outside Development rather than derived from the request scheme.
- Auth routes must be registered above the SPA fallback (`Program.cs`), and `UseAuthentication`/`UseAuthorization` must run before endpoints.
- `ng serve` (port 4200) and the API (5069) are different origins in development — an Angular dev proxy for `/api` keeps the same-origin cookie model locally.

## What We're NOT Doing

- No email confirmation, password reset emails, or 2FA UI — `MapIdentityApi` exposes those endpoints, but no email sender is configured (Identity's no-op sender) and the SPA does not use them.
- No OAuth / external logins (parked, roadmap Open Question 2).
- No profile fields (city, etc.) on `AppUser` — S-03 adds them.
- No roles/admin (flat role model per PRD §Access Control).
- No bearer tokens / refresh tokens in the SPA.
- No breached-password checks.
- No Playwright/browser e2e automation (testing strategy is Module 3); browser flow is verified manually.
- No CI test run.
- No rate limiting on `/api/auth/register` or `/api/auth/login` beyond Identity's per-account lockout, and duplicate-email registration returns `DuplicateUserName` (reveals that an account exists) — both accepted MVP risks.

## Implementation Approach

Backend first so the contract exists before the SPA uses it: `AppUser : IdentityUser`, `AppDbContext : IdentityDbContext<AppUser>, IDataProtectionKeyContext`, Identity API endpoints with cookie sessions under `/api/auth`, plus `logout` and `me`. Then the Angular side: Material, HttpClient with a dev proxy, an `AuthService` holding the current user as a signal, standalone `login`, `register` and `home` components, and a functional `authGuard`. Finally a production deploy whose release command applies the additive `AddIdentity` migration.

## Critical Implementation Details

- **Cookie options:** `HttpOnly`, `SameSite=Strict`, `SecurePolicy = Always` outside Development (`SameAsRequest` in Development), and `OnRedirectToLogin` / `OnRedirectToAccessDenied` set 401 / 403 instead of redirecting.
- **Data Protection:** `AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("moviedate")` — the application name must be fixed so every machine and release shares the key ring.
- **Middleware order:** `UseAuthentication()` then `UseAuthorization()` after static files and before endpoint mapping; `MapAuthEndpoints()` before `MapFallbackToFile`.

## Phase 1: Backend — Identity + API

### Overview

Identity storage, cookie auth, Data Protection keys in Postgres, the `/api/auth/*` surface, and integration tests.

### Changes Required:

#### 1. Packages

**File**: `movie-date.csproj`

**Intent**: Add Identity EF Core stores and EF Core Data Protection key storage.

**Contract**: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (10.x), `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` (10.x).

#### 2. User and DbContext

**File**: `Data/AppUser.cs` (new), `Data/AppDbContext.cs`

**Intent**: Empty `AppUser : IdentityUser`; `AppDbContext` becomes `IdentityDbContext<AppUser>` and implements `IDataProtectionKeyContext` (`DbSet<DataProtectionKey> DataProtectionKeys`).

**Contract**: `MovieDate.Data.AppUser`; tables `AspNet*` + `DataProtectionKeys`.

#### 3. Migration

**File**: `Data/Migrations/*_AddIdentity.cs` (generated)

**Intent**: `dotnet ef migrations add AddIdentity --output-dir Data/Migrations`. Additive only (new tables), per `lessons.md`.

**Contract**: Migration name `AddIdentity`.

#### 4. Auth services and endpoints

**File**: `Endpoints/AuthEndpoints.cs` (new), `Contracts/MeResponse.cs` (new), `Program.cs`

**Intent**: `AddAppAuth(IHostEnvironment)` registers authorization, `AddIdentityApiEndpoints<AppUser>()` with EF stores, password options (length 8, no class requirements), cookie options (see Critical Implementation Details), and Data Protection persisted to the DbContext. `MapAuthEndpoints()` maps `MapIdentityApi<AppUser>()` under `/api/auth`, plus `POST /api/auth/logout` (signs out the Identity application cookie, 204) and `GET /api/auth/me` (requires authorization, returns `MeResponse(Email)`). `Program.cs` gains only the `AddAppAuth`, `UseAuthentication`/`UseAuthorization` and `MapAuthEndpoints` calls.

**Contract**: `POST /api/auth/register` `{email,password}` → 200 / 400 ValidationProblem; `POST /api/auth/login?useCookies=true` `{email,password}` → 200 + cookie / 401; `GET /api/auth/me` → 200 `{"email":"…"}` / 401; `POST /api/auth/logout` → 204 (requires auth, else 401).

#### 5. Integration tests

**File**: `tests/MovieDate.Tests/AuthTests.cs` (new)

**Intent**: Against the existing Postgres fixture, with a cookie-handling client: register → login → `me` returns the email; `me` without cookie → 401 (not 302); logout → `me` → 401; register with a 7-character password → 400; wrong password → 401; duplicate email → 400. Use unique emails per test (shared container).

**Contract**: Test class in the existing `PostgresAppCollection`.

### Success Criteria:

#### Automated Verification:

- App builds: `dotnet build movie-date.csproj`
- Migration `AddIdentity` exists and is additive (only `CreateTable`/`CreateIndex` in `Up`)
- Tests pass: `dotnet test tests/MovieDate.Tests`
- Local migrate applies `AddIdentity`: `dotnet run --project movie-date.csproj -- --migrate`
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5069/api/auth/me` → `401` with the app running

#### Manual Verification:

- Register + login via `movie-date.http` (or curl) shows a `Set-Cookie` with `HttpOnly` and `SameSite=Strict`
- `DataProtectionKeys` table in the local DB contains a key after the first login

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — Angular Material + auth screens

### Overview

Material setup, HTTP + dev proxy, auth state, three screens and the route guard.

### Changes Required:

#### 1. Angular Material and HTTP

**File**: `ClientApp/package.json`, `ClientApp/angular.json`, `ClientApp/src/styles.css`, `ClientApp/src/app/app.config.ts`, `ClientApp/proxy.conf.json` (new)

**Intent**: Add Angular Material via `ng add @angular/material` (one prebuilt theme, animations as the schematic sets up); `provideHttpClient()`; dev-server proxy forwarding `/api` to `http://localhost:5069` wired into `angular.json` serve options.

**Contract**: `ng serve` proxies `/api/*`; production build unchanged (same-origin).

#### 2. Auth service

**File**: `ClientApp/src/app/auth/auth.service.ts` (new)

**Intent**: Holds the current user as a signal (`null` = anonymous); `register`, `login` (with `useCookies=true`), `logout`, and `loadMe()` (calls `/api/auth/me` once, caches result, treats 401 as anonymous). `MapIdentityApi`'s `/register` returns 200 without setting a cookie, so `register()` calls `/api/auth/register` and then `login()` with the same credentials before resolving. Maps API errors to user-facing messages (validation problems from register, 401 from login, lockout).

**Contract**: `currentUser: Signal<{ email: string } | null>`; methods return Observables/Promises consumed by components and the guard.

#### 3. Route guard and routes

**File**: `ClientApp/src/app/auth/auth.guard.ts` (new), `ClientApp/src/app/app.routes.ts`

**Intent**: Functional `authGuard` awaits `loadMe()`; anonymous → `UrlTree` to `/login?returnUrl=<requested url>`. Routes: `login`, `register` (public), `''` home (guarded), `**` → redirect to `''`.

**Contract**: Every future product route adds `canActivate: [authGuard]`.

#### 4. Screens

**File**: `ClientApp/src/app/auth/login.component.ts`, `register.component.ts`, `ClientApp/src/app/home/home.component.ts` (new, with templates), `ClientApp/src/app/app.html`, `ClientApp/src/app/app.ts`

**Intent**: Material reactive forms for login and register (email + password, client-side min length 8, inline server errors, submit disabled while pending, link between the two); after register the user is logged in and sent to `/`; login honours `returnUrl`. Home shows "Zalogowany jako <email>" and a logout button (logout → `/login`). Replace the CLI placeholder `app.html` with a Material toolbar + `<router-outlet>`.

**Contract**: UI copy in Polish.

#### 5. Guard test

**File**: `ClientApp/src/app/auth/auth.guard.spec.ts` (new); `ClientApp/src/app/app.spec.ts` (its `'Hello, client-app'` assertion at line 22 targets the CLI placeholder — replace it with a check that the app renders the toolbar and a `router-outlet`)

**Intent**: Vitest: guard returns `true` when the service reports a user; returns a `UrlTree` to `/login` with `returnUrl` when anonymous.

**Contract**: Runs under `ng test`.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `cd ClientApp && npx ng test --watch=false`
- Frontend production build passes: `cd ClientApp && npm run build`
- Docker image builds: `docker build -t moviedate-local .`

#### Manual Verification:

- With `docker compose up -d`, `dotnet run` and `npm start` in `ClientApp`: opening `http://localhost:4200/` while logged out redirects to `/login?returnUrl=%2F`
- Register a new account → lands on home showing the email; reload keeps the session
- Logout → back to `/login`; visiting `/` redirects to login again
- Wrong password shows an error; a 7-character password on register shows an error

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Production

### Overview

Deploy, verify the full flow on production across both machines, and record it.

### Changes Required:

#### 1. Deploy (human)

**Intent**: `fly deploy --remote-only --app moviedate-sk`; the release command applies `AddIdentity` before rollout. This deploy also ships the impl-review F2 fix (Dockerfile `CMD`), so the release log line should read `dotnet movie-date.dll --migrate` without the duplicated prefix.

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the new known-good release, Data Protection keys in Postgres, and the F2 confirmation.

**Contract**: Section "What's still open".

### Success Criteria:

#### Automated Verification:

- Release completes: `fly releases -a moviedate-sk` shows a new `complete` release
- `curl -s -o /dev/null -w "%{http_code}" https://moviedate-sk.fly.dev/api/auth/me` → `401`
- `curl -s https://moviedate-sk.fly.dev/health` → `{"status":"ok"}`

#### Manual Verification:

- Release log shows `Applying migration '…_AddIdentity'` and `Preparing to run: \`dotnet movie-date.dll --migrate\`` (no duplicated prefix)
- On https://moviedate-sk.fly.dev: register, land on home, reload keeps the session, logout, gated `/` redirects to login
- Shared keys across machines: with a session cookie from a production login, `curl -b <cookie> -H "fly-force-instance-id: <machine-id>" https://moviedate-sk.fly.dev/api/auth/me` returns 200 for **each** of the 2 web machines (`fly machines list`)
- Session survives restart: after `fly machine restart` of both machines, the same cookie still gets 200 from `/api/auth/me`
- `deploy-plan.md` updated with the new known-good release

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Angular `authGuard`: allows a known user; redirects anonymous users with `returnUrl`.

### Integration Tests:

- API on Testcontainers Postgres: register/login/me/logout happy path; 401 (not 302) without cookie; short password 400; wrong password 401; duplicate email 400.

### Manual Testing Steps:

1. Local browser flow via `ng serve` + proxy (Phase 2).
2. Production flow across both machines (Phase 3).

## Performance Considerations

`/api/auth/me` hits the DB once per app load (guard caches the result); first call after Neon autosuspend pays the wake-up (~1–4 s observed). Acceptable for MVP.

## Migration Notes

`AddIdentity` only creates tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`, `AspNetUserRoles`, `DataProtectionKeys`) — backward-compatible with the running v5 release, which never touches them. Rollback to v5 leaves the tables unused.

## References

- Roadmap item: `context/foundation/roadmap.md` → S-01
- PRD: `context/foundation/prd.md` → FR-001, US-01, NFR-03, §Access Control
- Decisions: `context/foundation/tech-stack.md` → "Decisions recorded 2026-11-02"
- Lessons: `context/foundation/lessons.md` → "Keep migrations backward-compatible with the previous release"
- Prior change: `context/archive/2026-11-02-prod-postgres-connection/`
- Patterns: `Endpoints/HealthEndpoints.cs` (Add/Map extension pair), `tests/MovieDate.Tests/PostgresAppFixture.cs`

## Review Addenda (impl-review 2026-11-02)

- `options.User.RequireUniqueEmail = true` was added (not planned); duplicate registration returns both `DuplicateUserName` and `DuplicateEmail`. Accepted — the enumeration risk in "What We're NOT Doing" is unchanged.
- Angular 22's `ng add @angular/material` generated a custom M3 `mat.theme()` (`src/material-theme.scss`, azure) instead of a prebuilt CSS theme, and `index.html` loads Roboto + Material Symbols from Google Fonts — an external runtime dependency. Accepted as schematic output.
- Added after review: functional `authInterceptor` (401 from product APIs → clear session, redirect to `/login?returnUrl=…`); logout keeps the session on non-401 errors; login takes the email from `/me`; login form validates `required` only; register-succeeded-but-login-failed → `/login?registered=1`.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — Identity + API

#### Automated

- [x] 1.1 App builds: `dotnet build movie-date.csproj` — 6470d5d
- [x] 1.2 Migration `AddIdentity` exists and is additive — 6470d5d
- [x] 1.3 Tests pass: `dotnet test tests/MovieDate.Tests` — 6470d5d
- [x] 1.4 Local migrate applies `AddIdentity` — 6470d5d
- [x] 1.5 `/api/auth/me` → `401` with the app running — 6470d5d

#### Manual

- [x] 1.6 Login `Set-Cookie` is `HttpOnly` and `SameSite=Strict` — 6470d5d
- [x] 1.7 `DataProtectionKeys` table contains a key after the first login — 6470d5d

### Phase 2: Frontend — Angular Material + auth screens

#### Automated

- [x] 2.1 Frontend tests pass: `npx ng test --watch=false` — 2f21255
- [x] 2.2 Frontend production build passes: `npm run build` — 2f21255
- [x] 2.3 Docker image builds — 2f21255

#### Manual

- [x] 2.4 Logged-out `/` redirects to `/login?returnUrl=%2F` — 2f21255
- [x] 2.5 Register lands on home with the email; reload keeps the session — 2f21255
- [x] 2.6 Logout returns to `/login`; `/` redirects again — 2f21255
- [x] 2.7 Wrong password and 7-character password show errors — 2f21255

### Phase 3: Production

#### Automated

- [x] 3.1 New release completes — 9e0b945
- [x] 3.2 Prod `/api/auth/me` → `401` — 9e0b945
- [x] 3.3 Prod `/health` → `{"status":"ok"}` — 9e0b945

#### Manual

- [x] 3.4 Release log shows `AddIdentity` applied and no duplicated command prefix — 9e0b945 (partially verified: the release machine's `Preparing to run` line had rotated out of the log buffer; AddIdentity output and single-command web machines were observed — confirm on next deploy)
- [x] 3.5 Prod flow: register, home, reload keeps session, logout, gated redirect — 9e0b945
- [x] 3.6 Cookie accepted by each of the 2 machines (`fly-force-instance-id`) — 9e0b945
- [x] 3.7 Session survives restart of both machines — 9e0b945
- [x] 3.8 `deploy-plan.md` updated with the new known-good release — 9e0b945
