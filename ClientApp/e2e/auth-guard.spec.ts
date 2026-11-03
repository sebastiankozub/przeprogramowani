// Seed test — protects test-plan risk #4 (primary path breaks in a real browser while API tests stay
// green), at the auth-guard level: a protected route sends a signed-out visitor to login and back.
import { expect, test } from '@playwright/test';
import { E2E_PASSWORD, registerViaApi, uniqueUser } from './support/users';

test('signed-out visitor of /matches is sent to login and returns to /matches after signing in', async ({
  page,
  request,
}) => {
  // Setup: an account exists (created through the public API, not through the UI under test).
  const user = uniqueUser('Guard');
  await registerViaApi(request, user);

  // Action: a fresh browser context has no session cookie.
  await page.goto('/matches');

  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fmatches$/);
  await expect(page.getByRole('button', { name: 'Zaloguj się' })).toBeVisible();

  // The SPA is client-rendered: retry the idempotent fill + submit until it leaves /login.
  await expect(async () => {
    await page.getByLabel('Email').fill('');
    await page.getByLabel('Email').fill(user.email);
    await page.getByLabel('Hasło').fill('');
    await page.getByLabel('Hasło').fill(E2E_PASSWORD);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await page.waitForURL((url) => url.pathname === '/matches', { timeout: 5_000 });
  }).toPass();

  // Assertion: back on the page that was asked for, rendered for a signed-in user.
  await expect(page.getByRole('heading', { name: 'Dopasowania' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Profil' })).toBeVisible();

  // Cleanup: none possible through the API (no delete-account endpoint); the unique e2e-* account stays in
  // the local DB and never collides with later runs.
});
