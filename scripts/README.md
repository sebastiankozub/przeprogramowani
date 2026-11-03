# scripts/

Helper scripts that talk to the app only through its **public API** (or, for cleanup, plain SQL). Node 22+, no dependencies. Passwords come from environment variables and are never printed or committed.

| Script | What it does | Run |
| --- | --- | --- |
| `demo-accounts.mjs` | Permanent demo accounts `demo-01..10@moviedate.test` with name, city, ratings and watchlist, so a new user gets more than one ranked match. Idempotent. | `DEMO_PASSWORD='…' node scripts/demo-accounts.mjs` (local) · add `BASE_URL=https://moviedate-sk.fly.dev` for production |
| `seed-test-users.mjs` | 20 throwaway manual-test accounts `mdtest-*@moviedate.test` shaped around one reference account. Idempotent. | `TEST_PASSWORD='…' node scripts/seed-test-users.mjs` (defaults to production; `BASE_URL` overrides) |
| `cleanup-test-users.sql` | Deletes every `mdtest-*` account and everything hanging off it (ratings, watchlist, matches on both sides). Does **not** touch `demo-*`. | `psql "<neon direct connection string>" -f scripts/cleanup-test-users.sql` or the Neon SQL editor |

On PowerShell set the variable first: `$env:DEMO_PASSWORD='…'; node scripts/demo-accounts.mjs`.
