# Test stack

## E2E

<!-- Written by /10x-e2e-setup. Re-run it to change this section; other skills only read it. -->

- runner: Playwright Test, @playwright/test 1.64.1
- config: ClientApp/playwright.config.ts (run every command from ClientApp/)
- single-spec command: npx playwright test e2e/<name>.spec.ts
- full-suite command: npm run e2e
- base URL: http://localhost:4200
- port: 4200 (detected default of `ng serve`, not overridden in ClientApp/angular.json; override with E2E_PORT); the API it proxies to is fixed at 5069 (Properties/launchSettings.json profile "http", ClientApp/proxy.conf.json)
- web server command: app entry `npm start -- --port $E2E_PORT` (ng serve dev server with the /api proxy; there is no preview build locally because wwwroot is only filled in the Docker image); reuseExistingServer outside CI. A second entry starts the API: `dotnet run --launch-profile http -- --migrate && dotnet run --no-build --launch-profile http` with MatchRecompute__QuietPeriod=00:00:05 and MatchRecompute__MaxDelay=00:00:15, never reused (stop a local `dotnet run` first). Requires `docker compose up -d` (Postgres on 5432).
- auth setup project: none — each spec creates its own throwaway account through the public API (ClientApp/e2e/support/users.ts, e2e-<id>@moviedate.test, local stack only), so there are no shared E2E_USERNAME / E2E_PASSWORD credentials
- storageState: none
- seed: ClientApp/e2e/auth-guard.spec.ts — protects test-plan risk #4 at guard level: a signed-out visitor of /matches is sent to login and returns to /matches after signing in
- browser CLI: playwright-cli 0.1.23, command skill at .claude/skills/playwright-cli/SKILL.md (use `-s=przeprogramowani`)
- updated: 2026-11-03
