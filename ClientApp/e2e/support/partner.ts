import { APIRequestContext } from '@playwright/test';
import { completeProfile, MatchableProfile } from './profile';
import { E2eUser, registerViaApi, uniqueUser } from './users';

export type PartnerSpec = MatchableProfile;

/**
 * Creates a complete, matchable partner (display name + city + one rated movie) through the public API, so a
 * browser test has a strong candidate regardless of whoever else is in the local database.
 * Follows scripts/demo-accounts.mjs. The request context must be a fresh one: it keeps the partner's cookie.
 */
export async function createPartner(request: APIRequestContext, spec: PartnerSpec): Promise<E2eUser> {
  const partner = uniqueUser('Partner');
  await registerViaApi(request, partner);
  await completeProfile(request, partner, spec);
  return partner;
}
