import { defineConfig, devices } from '@playwright/test';

// The SPA is served by `ng serve` (default port 4200, not overridden in angular.json); its proxy
// (proxy.conf.json) forwards /api to the API on :5069 (Properties/launchSettings.json, profile "http").
// Locally wwwroot is empty — only the Dockerfile copies the built SPA into it — so e2e runs both servers.
// E2E_PORT overrides the SPA port when 4200 is taken on this machine.
const PORT = Number(process.env.E2E_PORT ?? 4200);
const baseURL = `http://localhost:${PORT}`;
const API_URL = 'http://localhost:5069';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // One API process with one in-memory recompute queue and one shared database: keep specs sequential.
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // API: apply migrations + seed the movie/city catalogs, then serve. A slower debounce (5 s quiet,
      // 15 s max) makes the "update in progress" state on /matches observable right after a write.
      // Never reuse a developer's `dotnet run`: it has the default 2 s debounce.
      command:
        'dotnet run --launch-profile http -- --migrate && dotnet run --no-build --launch-profile http',
      cwd: '..',
      url: `${API_URL}/health`,
      env: {
        MatchRecompute__QuietPeriod: '00:00:05',
        MatchRecompute__MaxDelay: '00:00:15',
      },
      reuseExistingServer: false,
      timeout: 180_000,
    },
    {
      // App entry: the SPA dev server with the /api proxy.
      command: `npm start -- --port ${PORT}`,
      url: baseURL,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
    },
  ],
});
