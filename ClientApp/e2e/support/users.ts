import { randomUUID } from 'node:crypto';
import { APIRequestContext, expect } from '@playwright/test';

/** Password for every throwaway e2e account (local stack only; meets the 8-character policy). */
export const E2E_PASSWORD = 'e2e-password-1';

export interface E2eUser {
  email: string;
  displayName: string;
}

/** A unique, recognisable identity per test run, so re-runs never collide in the shared local DB. */
export function uniqueUser(label: string): E2eUser {
  const id = randomUUID().slice(0, 8);
  return { email: `e2e-${id}@moviedate.test`, displayName: `E2E ${label} ${id}` };
}

/**
 * Registers a user through the public API and signs the given request context in with a cookie session
 * (the same sequence as scripts/demo-accounts.mjs). Fails the test on any unexpected status.
 */
export async function registerViaApi(request: APIRequestContext, user: E2eUser): Promise<void> {
  const register = await request.post('/api/auth/register', {
    data: { email: user.email, password: E2E_PASSWORD },
  });
  expect(register.status(), 'register').toBe(200);
  const login = await request.post('/api/auth/login?useCookies=true', {
    data: { email: user.email, password: E2E_PASSWORD },
  });
  expect(login.status(), 'login').toBe(200);
}
