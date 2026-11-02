<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Register and Login

- **Plan**: context/changes/register-and-login/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-02
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated re-run 2026-11-02: `dotnet build` 0 errors; `dotnet test` 9/9; `ng test` 4/4; `npm run build` OK; prod `/api/auth/me` 401; prod `/health` ok.

## Findings

### F1 — Logout swallows every error and still shows the user as logged out

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/auth/auth.service.ts:73-80
- **Detail**: `logout()` catches all errors (network, 5xx), not only 401, then clears the user, and home navigates to /login. If the POST failed, the HttpOnly cookie is still valid (14-day sliding); on a shared device the next person reloading is signed in as the previous user while the UI claimed logout succeeded.
- **Fix**: Treat only 401 as "already logged out"; on any other error keep the user and throw an `AuthError` so home shows a message.
- **Decision**: FIXED

### F2 — Session state cached for the SPA lifetime; no 401 handling after load

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/auth/auth.service.ts:39-52, ClientApp/src/app/app.config.ts
- **Detail**: `loadMe()` caches its promise until reload and no HTTP interceptor exists. Cookie expiry, logout in another tab or a security-stamp invalidation leave `currentUser` showing the old email and guarded routes open; later API 401s will not reset state or redirect. Not exploitable (server enforces auth per endpoint), but the first product slice (S-02) will hit it.
- **Fix A ⭐ Recommended**: Add a functional `authInterceptor` now: on 401 from any `/api/*` call other than `/api/auth/login` and `/api/auth/me`, reset the cached user and navigate to `/login?returnUrl=<current url>`; register via `provideHttpClient(withInterceptors([...]))` with a Vitest spec.
  - Strength: Closes the gap before any product API exists; every later slice gets it for free.
  - Tradeoff: ~30 lines + spec now, for a path no current endpoint exercises.
  - Confidence: HIGH — standard Angular functional-interceptor pattern.
  - Blind spot: Behaviour with concurrent 401s (multiple redirects) not designed.
- **Fix B**: Defer to S-02 and record a lesson "every product API call must handle 401 centrally".
  - Strength: No speculative code; built when there is a real 401-producing call to test against.
  - Tradeoff: Relies on S-02's plan remembering it.
  - Confidence: MEDIUM — the lesson is re-read by /10x-plan, but still easy to under-scope.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A — authInterceptor + spec)

### F3 — Criterion 3.4 ticked with partial evidence

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/register-and-login/plan.md:305
- **Detail**: 3.4 asks for the release machine's `Preparing to run: dotnet movie-date.dll --migrate` line; it had rotated out of the log buffer. The AddIdentity migration output and the web machines' single `dotnet movie-date.dll` were observed, which strongly implies the CMD fix works, but the exact line was not seen.
- **Fix**: Annotate 3.4 in Progress as "partially verified (release line rotated out; web machines confirm CMD fix)" and check it on the next deploy.
- **Decision**: FIXED (3.4 annotated as partially verified)

### F4 — `RequireUniqueEmail = true` not in the plan

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Endpoints/AuthEndpoints.cs:31
- **Detail**: Unplanned but sensible setting; duplicate registration now returns both `DuplicateUserName` and `DuplicateEmail` (the plan's risk note names only the first). SPA already maps both codes.
- **Fix**: Accept; add one line to the plan's "What We're NOT Doing" risk note.
- **Decision**: FIXED (accepted; noted in plan Review Addenda)

### F5 — Login form enforces `minLength(8)`

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/auth/login.component.ts:22
- **Detail**: Duplicates the registration password policy on the login form; blocks no one today, but would block valid logins if the policy changes or accounts are seeded. A short wrong password shows a validation message instead of "Nieprawidłowy email lub hasło".
- **Fix**: Keep only `Validators.required` on the login password field.
- **Decision**: FIXED

### F6 — AuthService state edge cases

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ClientApp/src/app/auth/auth.service.ts:40-50, 60, 64-71
- **Detail**: (a) after login the user is set to the typed email, not the server's normalized one; (b) a slow in-flight `loadMe()` can overwrite newer state set by login/logout; (c) if register succeeds but the follow-up login fails, resubmitting shows "konto już istnieje". All unlikely with current routes.
- **Fix**: After login reset the cache and call `loadMe()` (fixes a, mitigates b); on login failure after register navigate to /login with a hint.
- **Decision**: FIXED (login reloads /me; superseded loadMe ignored; AccountCreatedError → /login?registered=1)

### F7 — Google Fonts dependency and custom M3 theme

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: ClientApp/src/index.html:9-18, ClientApp/src/material-theme.scss
- **Detail**: Plan said "one prebuilt theme"; Angular 22's `ng add` generates a custom `mat.theme()` SCSS and links Roboto + Material Symbols from Google Fonts (external runtime dependency, not mentioned in the plan).
- **Fix**: Accept as schematic output; note in the plan.
- **Decision**: FIXED (accepted; noted in plan Review Addenda)

### F8 — `/me` reads the email claim; future per-user data must key on user id

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: Endpoints/AuthEndpoints.cs:82
- **Detail**: Correct for display, but `ClaimTypes.Email` is a login-time snapshot. S-02+ (ratings, lists, matches) must key per-user data on `ClaimTypes.NameIdentifier` / `UserManager.GetUserId`, not email.
- **Fix**: Record as a lesson for future slices; no code change.
- **Decision**: ACCEPTED-AS-RULE: Key per-user data on the user id, never on the email claim
