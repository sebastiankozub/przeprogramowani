// Protects test-plan risks #1 and #4 (context/foundation/test-plan.md, rollout Phase 1):
//   #1 — after a rating write, /matches announces that an update is on the way and then shows the new
//        candidate by itself, without any click;
//   #4 — a fresh user completes register -> name + city -> rate a movie -> /matches in a real browser.
// Modeled on the seed e2e/auth-guard.spec.ts. Change: context/changes/testing-critical-path-e2e.
// The e2e API runs with a 5 s quiet period (playwright.config.ts), so the pending state is still on when
// /matches opens right after the rating.
import { expect, request as apiRequest, test } from '@playwright/test';
import { createPartner } from './support/partner';
import { E2E_PASSWORD, uniqueUser } from './support/users';

test.describe('critical path: a new user finds a movie-taste match', () => {
  test('rating a movie a partner loves makes the partner appear on /matches by itself', async ({
    page,
    baseURL,
  }) => {
    // Setup: a partner in Kraków who rated Casablanca 9 (created through the API, own cookie jar).
    const partnerApi = await apiRequest.newContext({ baseURL });
    const partner = await createPartner(partnerApi, {
      city: 'Kraków',
      movieTitle: 'Casablanca',
      movieYear: 1942,
      rating: 9,
    });
    await partnerApi.dispose();
    const user = uniqueUser('User');
    test.info().annotations.push({ type: 'test-data', description: `${user.email} / ${partner.email}` });

    // Step 1: register through the form; registration signs the user in and lands on the start page.
    await page.goto('/register');
    await expect(async () => {
      await page.getByLabel('Email').fill('');
      await page.getByLabel('Email').fill(user.email);
      await page.getByLabel('Hasło').fill('');
      await page.getByLabel('Hasło').fill(E2E_PASSWORD);
      await page.getByRole('button', { name: 'Zarejestruj się' }).click();
      await page.waitForURL((url) => url.pathname === '/', { timeout: 5_000 });
    }).toPass();

    // Step 2: set the display name on the profile.
    await page.getByRole('link', { name: 'Profil' }).click();
    await page.getByLabel('Twoja nazwa').fill(user.displayName);
    await page.getByRole('button', { name: 'Zapisz nazwę' }).click();
    await expect(page.getByText(`Twoja nazwa: ${user.displayName}`)).toBeVisible();

    // Step 3: pick the city from the autocomplete (an option must be chosen) and save it.
    await page.getByLabel('Wybierz miasto').fill('Kraków');
    await page.getByRole('option', { name: 'Kraków (małopolskie)' }).click();
    await page.getByRole('button', { name: 'Zapisz', exact: true }).click();
    await expect(page.getByText('Twoje miasto: Kraków (małopolskie)')).toBeVisible();

    // Step 4: rate Casablanca 8 — a favorite shared with the partner.
    await page.getByRole('link', { name: 'Obejrzane filmy' }).click();
    await page.getByLabel('Szukaj filmu').fill('Casablanca');
    await page.getByRole('option', { name: /^Casablanca.*1942/ }).click();
    await page.getByRole('combobox', { name: 'Ocena' }).first().click();
    await page.getByRole('option', { name: '8', exact: true }).click();
    await page.getByRole('button', { name: 'Dodaj' }).click();
    await expect(page.getByRole('listitem').filter({ hasText: 'Casablanca' })).toBeVisible();

    // Step 5: open /matches right after the write. Nothing on this page is clicked from here on.
    await page.getByRole('link', { name: 'Dopasowania' }).click();
    const pending = page.getByRole('status').filter({ hasText: 'Trwa aktualizacja dopasowań' });

    // The page tells the user an update is on the way...
    await expect(pending).toBeVisible();
    // ...and then the partner arrives by itself, as a shared-favorite match...
    const partnerRow = page.getByRole('listitem').filter({ hasText: partner.displayName });
    await expect(partnerRow).toBeVisible({ timeout: 30_000 });
    await expect(partnerRow).toContainText('Wspólne ulubione');
    await expect(partnerRow).toContainText('Casablanca');
    // ...and the update notice is gone.
    await expect(pending).toBeHidden();

    // Cleanup: none possible through the API (no delete-account endpoint, match lists are append-only by
    // design); both e2e-* accounts are unique per run and never collide with later runs.
  });
});
