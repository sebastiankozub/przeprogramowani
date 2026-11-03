-- Removes every manual-test account created by scripts/seed-test-users.mjs (emails mdtest-*@moviedate.test)
-- together with everything hanging off them. All per-user tables cascade from "AspNetUsers":
-- WatchedMovies, WatchlistItems, Identity claims/logins/tokens/roles, and Matches on BOTH sides
-- (rows where the test user is the owner AND rows on real users' lists where the test user is the candidate).
-- Movies and Cities are catalog data and are never touched.
--
-- Run:  psql "<neon direct connection string>" -f scripts/cleanup-test-users.sql
--   or paste into the Neon console SQL Editor (project moviedate-sk, branch main, database neondb).

BEGIN;

-- Preview: what is about to go.
SELECT
  (SELECT count(*) FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST') AS test_users,
  (SELECT count(*) FROM "Matches" m
     WHERE m."UserId"      IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST')
        OR m."CandidateId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST')) AS matches,
  (SELECT count(*) FROM "WatchedMovies" w
     WHERE w."UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST')) AS watched,
  (SELECT count(*) FROM "WatchlistItems" l
     WHERE l."UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST')) AS watchlist;

DELETE FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST';

-- Verify: must be all zeros.
SELECT
  (SELECT count(*) FROM "AspNetUsers" WHERE "NormalizedEmail" LIKE 'MDTEST-%@MOVIEDATE.TEST') AS test_users_left,
  (SELECT count(*) FROM "Matches" m
     LEFT JOIN "AspNetUsers" a ON a."Id" = m."UserId"
     LEFT JOIN "AspNetUsers" b ON b."Id" = m."CandidateId"
     WHERE a."Id" IS NULL OR b."Id" IS NULL) AS orphan_matches;

COMMIT;
