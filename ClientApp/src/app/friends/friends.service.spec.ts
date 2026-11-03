import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, firstValueFrom } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import {
  ALREADY_FRIEND,
  CONVERSATION_NOT_ALLOWED,
  Conversation,
  Friend,
  FriendsList,
  FriendsService,
  INVALID_MESSAGE,
  Message,
  NOT_ON_MATCH_LIST,
} from './friends.service';

describe('FriendsService', () => {
  let service: FriendsService;
  let http: HttpTestingController;

  const friend: Friend = { userId: 'u-2', displayName: 'Ania', city: { id: 7, name: 'Kraków', voivodeship: 'małopolskie' } };
  const message: Message = { id: 1, fromMe: true, body: 'Cześć!', sentAt: '2026-11-03T10:00:00Z' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FriendsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function failWith(call: Observable<unknown>, url: string, status: number): Promise<MoviesError> {
    const result = firstValueFrom(call).catch((e: unknown) => e);
    http.expectOne(url).flush({ title: 'x' }, { status, statusText: 'Error' });
    const err = await result;
    expect(err).toBeInstanceOf(MoviesError);
    expect((err as MoviesError).status).toBe(status);
    return err as MoviesError;
  }

  it('loads both lists', async () => {
    const lists: FriendsList = { friends: [friend], addedMe: [] };
    const call = firstValueFrom(service.list());

    const req = http.expectOne('/api/friends');
    expect(req.request.method).toBe('GET');
    req.flush(lists);

    await expect(call).resolves.toEqual(lists);
  });

  it('adds a friend by user id', async () => {
    const call = firstValueFrom(service.add('u-2'));

    const req = http.expectOne('/api/friends');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userId: 'u-2' });
    req.flush(friend, { status: 201, statusText: 'Created' });

    await expect(call).resolves.toEqual(friend);
  });

  it('maps add failures: 404 to "not on your match list", 409 to "already a friend"', async () => {
    expect((await failWith(service.add('u-2'), '/api/friends', 404)).message).toBe(NOT_ON_MATCH_LIST);
    expect((await failWith(service.add('u-2'), '/api/friends', 409)).message).toBe(ALREADY_FRIEND);
  });

  it('loads a conversation', async () => {
    const conversation: Conversation = { with: friend, messages: [message] };
    const call = firstValueFrom(service.conversation('u-2'));

    const req = http.expectOne('/api/conversations/u-2');
    expect(req.request.method).toBe('GET');
    req.flush(conversation);

    await expect(call).resolves.toEqual(conversation);
  });

  it('maps a 404 on a conversation to "not allowed"', async () => {
    const err = await failWith(service.conversation('u-9'), '/api/conversations/u-9', 404);
    expect(err.message).toBe(CONVERSATION_NOT_ALLOWED);
  });

  it('sends a message', async () => {
    const call = firstValueFrom(service.send('u-2', 'Cześć!'));

    const req = http.expectOne('/api/conversations/u-2/messages');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Cześć!' });
    req.flush(message, { status: 201, statusText: 'Created' });

    await expect(call).resolves.toEqual(message);
  });

  it('maps send failures: 400 to the length rule, 404 to "not allowed"', async () => {
    const url = '/api/conversations/u-2/messages';
    expect((await failWith(service.send('u-2', ' '), url, 400)).message).toBe(INVALID_MESSAGE);
    expect((await failWith(service.send('u-2', 'x'), url, 404)).message).toBe(CONVERSATION_NOT_ALLOWED);
  });
});
