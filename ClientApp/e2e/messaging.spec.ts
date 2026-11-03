// Protects test-plan risk #4 (context/foundation/test-plan.md) for the second primary journey, and the
// happy path of the messaging guardrail: a user adds a match as a friend from /matches, writes to them in a
// real browser, the friend (through the API) receives the message and replies, and the reply shows up in the
// browser. Strangers being refused is covered by the backend integration tests (ConversationsTests).
// Change: context/changes/first-message-to-friend (Phase 3).
// The user's profile and rating writes trigger a recompute (5 s quiet period in playwright.config.ts), so
// /matches may show the update notice first; the test only waits for the partner row.
import { randomUUID } from 'node:crypto';
import { expect, request as apiRequest, test } from '@playwright/test';
import { createPartner } from './support/partner';
import { completeProfile } from './support/profile';
import { registerViaApi, uniqueUser } from './support/users';

interface FriendsResponse {
  friends: { userId: string; displayName: string }[];
  addedMe: { userId: string; displayName: string }[];
}

interface ConversationResponse {
  with: { userId: string; displayName: string };
  messages: { id: number | string; fromMe: boolean; body: string; sentAt: string }[];
}

test.describe('messaging: a user writes to a match they added as a friend', () => {
  test('a message sent from the browser reaches the friend, and the friend reply shows up after reload', async ({
    page,
    baseURL,
  }) => {
    // Setup: a partner in Kraków who rated Casablanca 9, with its own cookie jar kept for the whole test.
    const partnerApi = await apiRequest.newContext({ baseURL });
    try {
      const partner = await createPartner(partnerApi, {
        city: 'Kraków',
        movieTitle: 'Casablanca',
        movieYear: 1942,
        rating: 9,
      });

      // The browser user is set up through page.request, which shares the browser's cookies: Kraków +
      // Casablanca 8 makes the partner a shared-favorite match.
      const user = uniqueUser('Writer');
      await registerViaApi(page.request, user);
      await completeProfile(page.request, user, {
        city: 'Kraków',
        movieTitle: 'Casablanca',
        movieYear: 1942,
        rating: 8,
      });
      test.info().annotations.push({ type: 'test-data', description: `${user.email} / ${partner.email}` });

      const runId = randomUUID().slice(0, 8);
      const message = `Cześć, idziemy na Casablancę? ${runId}`;
      const reply = `Jasne, w piątek! ${runId}`;

      // Step 1: the partner arrives on /matches once the recompute has run.
      await page.goto('/matches');
      const partnerRow = page.getByRole('listitem').filter({ hasText: partner.displayName });
      await expect(partnerRow).toBeVisible({ timeout: 30_000 });

      // Step 2: add the partner as a friend; the row then offers "Napisz".
      await partnerRow.getByRole('button', { name: 'Dodaj do znajomych' }).click();
      const writeLink = partnerRow.getByRole('link', { name: 'Napisz' });
      await expect(writeLink).toBeVisible();

      // Step 3: open the conversation with the partner.
      await writeLink.click();
      await expect(page).toHaveURL(/\/friends\/[^/?#]+$/);
      await expect(page.getByRole('heading', { name: partner.displayName })).toBeVisible();

      // Step 4: send a unique message; it appears in the conversation.
      await page.getByLabel('Wiadomość').fill(message);
      await page.getByRole('button', { name: 'Wyślij' }).click();
      await expect(page.getByRole('listitem').filter({ hasText: message })).toBeVisible();

      // Step 5 (partner, API): the user shows up under "added me", and the message arrived as not-from-me.
      const friendsRes = await partnerApi.get('/api/friends');
      expect(friendsRes.status(), 'partner lists friends').toBe(200);
      const friends = (await friendsRes.json()) as FriendsResponse;
      const writer = friends.addedMe.find((f) => f.displayName === user.displayName);
      expect(writer, 'browser user is in the partner\'s "addedMe"').toBeTruthy();

      const conversationRes = await partnerApi.get(`/api/conversations/${writer!.userId}`);
      expect(conversationRes.status(), 'partner opens the conversation').toBe(200);
      const conversation = (await conversationRes.json()) as ConversationResponse;
      expect(conversation.with.displayName).toBe(user.displayName);
      expect(conversation.messages).toContainEqual(expect.objectContaining({ body: message, fromMe: false }));

      // Step 6 (partner, API): reply.
      const replyRes = await partnerApi.post(`/api/conversations/${writer!.userId}/messages`, {
        data: { body: reply },
      });
      expect(replyRes.status(), 'partner replies').toBe(201);

      // Step 7: the browser user reloads the conversation and sees the reply after their own message.
      await page.reload();
      await expect(page.getByRole('heading', { name: partner.displayName })).toBeVisible();
      await expect(page.getByRole('listitem').filter({ hasText: reply })).toBeVisible();
      await expect(page.getByRole('listitem').filter({ hasText: message })).toBeVisible();

      // Cleanup: none possible through the API (no delete-account endpoint); both e2e-* accounts are unique
      // per run and never collide with later runs.
    } finally {
      await partnerApi.dispose();
    }
  });
});
