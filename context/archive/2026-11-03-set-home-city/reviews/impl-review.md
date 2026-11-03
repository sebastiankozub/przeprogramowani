<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Set Home City

- **Plan**: context/changes/set-home-city/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated re-run 2026-11-03: `dotnet build` 0/0; `dotnet test` 73/73; `ng test` 18/18; `npm run build` 502.25 kB initial (warning budget 600 kB); prod `/api/cities` anonymous 401; prod `/health` ok. Drift review: every planned change MATCH (deploy note records S-04 review fixes in v11 rather than v12 — factual, not a defect). Safety review: NFR-04 holds on every traced path (DTO projections only, no entity serialization, no sensitive logging); migration metadata-only; seeder idempotent (coordinates round-trip exactly); search escaping/caps verified; `cities.json` 1,030 rows, no duplicate QIDs or name+voivodeship pairs, all inside Poland's bounding box.

## Findings

### F1 — `ExecuteUpdateAsync` leaves `ConcurrencyStamp` unchanged; a concurrent Identity update can revert the city

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Services/ProfileService.cs:50-52
- **Detail**: Identity's `UserStore.UpdateAsync` (reached via `/api/auth/manage/info` and failed-login lockout counting) writes all columns. If such a request loads the user before a city save and writes after it, it restores the old `CityId`; Identity's optimistic concurrency cannot detect it because the stamp never changed. Single-request window, no security impact.
- **Fix**: Set `ConcurrencyStamp = Guid.NewGuid().ToString()` in the same `ExecuteUpdateAsync`, so the concurrent Identity write fails with ConcurrencyFailure instead of silently overwriting the city; fix the code comment.
- **Decision**: FIXED (ConcurrencyStamp rotated in the same ExecuteUpdate + test Set_city_rotates_concurrency_stamp; break-check: test fails without the rotation)

### F2 — Guardrail not asserted on the `PUT /api/profile/city` response

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/ProfileTests.cs:241-253
- **Detail**: The raw-JSON no-coordinates check covers `GET /api/cities` and `GET /api/profile`, not the PUT body (parsed into a DTO that would ignore extra fields).
- **Fix**: Call `AssertNoCoordinatesOrPopulation` on the PUT response body too.
- **Decision**: FIXED (raw PUT body asserted)

### F3 — `Search_caps_overlong_query` would pass without the cap

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/CitiesTests.cs:145-153
- **Detail**: Same pattern as the movie search test: it only proves a long query does not fail.
- **Fix**: Rename to `Search_with_overlong_query_does_not_fail` (honest name); a real cap test needs a 100+ char name, not worth it.
- **Decision**: FIXED (renamed to Search_with_overlong_query_does_not_fail in cities and movies tests)

### F4 — Test-inserted cities persist in the shared collection database

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/MovieDate.Tests/CitiesTests.cs:124-143
- **Detail**: Two `Zq{guid}` cities stay in the shared container; harmless today, but row-count assertions elsewhere could drift.
- **Fix**: Delete the inserted rows at the end of the test (try/finally).
- **Decision**: FIXED (try/finally cleanup in cities and movies literal-wildcard tests)

### F5 — FK violation on set-city race surfaces as 500

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Services/ProfileService.cs:43-52
- **Detail**: City existence is checked before the update; a city deleted in between would raise 23503 → 500. The seeder never deletes, so currently unreachable.
- **Fix**: Map `PostgresException` 23503 to NotFound (consistent with S-02/S-04 services).
- **Decision**: FIXED (FK violation -> NotFound)

### F6 — Cross-feature coupling of shared helpers

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Endpoints/CitiesEndpoints.cs (uses `MoviesEndpoints.EscapeLikePattern`), ClientApp/src/app/profile/profile.service.ts:4 (imports `MoviesError`/`mapErrors` from movies.service)
- **Detail**: Profile/cities code depends on the movies feature for generic helpers (also watchlist on the SPA side).
- **Fix**: Move `EscapeLikePattern` to `Endpoints/LikePattern.cs` and `MoviesError`/`mapErrors` to `ClientApp/src/app/shared/api-errors.ts` (re-exported or imports updated).
- **Decision**: FIXED (Endpoints/LikePattern.cs; ClientApp/src/app/shared/api-errors.ts — mapErrors 409 fallback now generic, MoviesService passes its duplicate message explicitly)

### F7 — Catalog search queries live in endpoints, not Services/

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: Endpoints/CitiesEndpoints.cs:41-50, Endpoints/MoviesEndpoints.cs
- **Detail**: Second instance of the S-02 F5 observation (skipped then). S-05 will query cities (coordinates) and movies from `MatchScoringService`, so lookups will be needed in Services/.
- **Fix**: Accept now; when S-05 needs catalog/city lookups, add them to services instead of more endpoint-level queries.
- **Decision**: ACCEPTED (move catalog/city lookups into services when S-05 needs them)
