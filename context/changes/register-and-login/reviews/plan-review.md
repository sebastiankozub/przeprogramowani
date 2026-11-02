<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Register and Login

- **Plan**: context/changes/register-and-login/plan.md
- **Mode**: Deep (inline verification — small codebase; framework behaviours are covered by Phase 1 integration tests)
- **Date**: 2026-11-02
- **Verdict**: SOUND
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding
10/10 paths ✓, 3/3 symbols ✓ (UseStaticFiles Program.cs:28, MapFallbackToFile Program.cs:52, PostgresAppCollection), brief↔plan ✓; lessons.md (expand/contract) satisfied — AddIdentity is additive; contract-surfaces.md absent — skipped.

## Findings

### F1 — Cross-machine cookie check would not reach both machines

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 — Manual verification
- **Detail**: The only production proof of shared Data Protection keys was "reload several times". On the Fly trial both machines are stopped (`fly machines list`); the proxy wakes one and reloads likely stay on it, so the check passes even with unshared keys. Survival across restarts was not checked either.
- **Fix**: Per-machine `curl -b <cookie> -H "fly-force-instance-id: <id>" /api/auth/me` → 200 on both, then `fly machine restart` both and repeat.
  - Strength: Deterministic proof of shared keys and restart survival.
  - Tradeoff: A few extra commands in Phase 3.
  - Confidence: HIGH — documented Fly proxy header.
  - Blind spot: Waking a stopped machine via the header not verified on the trial.
- **Decision**: FIXED — Phase 3 manual criteria + Progress 3.6/3.7 added (deploy-plan row renumbered 3.8 before any execution)

### F2 — Two Phase 2 contract gaps

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 #2, #5
- **Detail**: MapIdentityApi /register sets no cookie, but the plan expects the user to be logged in after registering; app.spec.ts:22 asserts the CLI placeholder 'Hello, client-app' and will fail.
- **Fix**: AuthService.register() chains login; replace the app.spec.ts assertion with toolbar + router-outlet.
- **Decision**: FIXED

### F3 — Account enumeration and registration spam not recorded

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 #4, What We're NOT Doing
- **Detail**: Duplicate email returns DuplicateUserName; no rate limiting beyond per-account lockout.
- **Fix**: Record both as accepted MVP risks.
- **Decision**: FIXED
