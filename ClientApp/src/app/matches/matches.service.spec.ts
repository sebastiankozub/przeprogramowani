import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { Match, MatchesService, MatchStatus, PROFILE_INCOMPLETE } from './matches.service';

describe('MatchesService', () => {
  let service: MatchesService;
  let http: HttpTestingController;

  const movie = { id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 };
  const match: Match = {
    userId: 'u-2',
    displayName: 'Ania',
    city: { id: 7, name: 'Kraków', voivodeship: 'małopolskie' },
    score: 87,
    sharedFavoritesCount: 1,
    sharedWatchlistCount: 0,
    sharedFavorites: [movie],
    sharedWatchlist: [],
    firstMatchedAt: '2026-11-03T10:00:00Z',
    isFriend: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MatchesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the stored list', async () => {
    const call = firstValueFrom(service.list());

    const req = http.expectOne('/api/matches');
    expect(req.request.method).toBe('GET');
    req.flush([match]);

    await expect(call).resolves.toEqual([match]);
  });

  it('maps a 409 on the list to the Polish incomplete-profile message', async () => {
    const call = firstValueFrom(service.list()).catch((e: unknown) => e);
    http
      .expectOne('/api/matches')
      .flush({ title: 'profile_incomplete' }, { status: 409, statusText: 'Conflict' });

    const err = await call;
    expect(err).toBeInstanceOf(MoviesError);
    expect((err as MoviesError).message).toBe(PROFILE_INCOMPLETE);
    expect((err as MoviesError).status).toBe(409);
  });

  it('reads the recompute status', async () => {
    const status: MatchStatus = { pending: true, profileComplete: true };
    const call = firstValueFrom(service.status());

    const req = http.expectOne('/api/matches/status');
    expect(req.request.method).toBe('GET');
    req.flush(status);

    await expect(call).resolves.toEqual(status);
  });

  it('maps a failed status call to a Polish message', async () => {
    const call = firstValueFrom(service.status()).catch((e: unknown) => e);
    http.expectOne('/api/matches/status').flush(null, { status: 404, statusText: 'Not Found' });

    const err = await call;
    expect(err).toBeInstanceOf(MoviesError);
    expect((err as MoviesError).message).toBe('Nie udało się sprawdzić stanu dopasowań.');
    expect((err as MoviesError).status).toBe(404);
  });

  it('no longer exposes an on-demand recompute', () => {
    expect((service as unknown as Record<string, unknown>)['recompute']).toBeUndefined();
  });
});
