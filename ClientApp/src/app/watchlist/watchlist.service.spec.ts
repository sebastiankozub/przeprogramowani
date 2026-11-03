import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { ALREADY_ON_WATCHLIST, ALREADY_WATCHED, WatchlistService } from './watchlist.service';

describe('WatchlistService', () => {
  let service: WatchlistService;
  let http: HttpTestingController;

  const movie = { id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WatchlistService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('adds a movie by posting movieId', async () => {
    const item = { movie, addedAt: '2026-11-03T10:00:00Z' };
    const call = firstValueFrom(service.add(1));

    const req = http.expectOne('/api/watchlist');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ movieId: 1 });
    req.flush(item, { status: 201, statusText: 'Created' });

    await expect(call).resolves.toEqual(item);
  });

  it('marks a movie as watched by posting the rating', async () => {
    const watched = { movie, rating: 8, updatedAt: '2026-11-03T10:00:00Z' };
    const call = firstValueFrom(service.markWatched(1, 8));

    const req = http.expectOne('/api/watchlist/1/watched');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ rating: 8 });
    req.flush(watched);

    await expect(call).resolves.toEqual(watched);
  });

  it('maps a 409 to distinct messages for add and for mark watched', async () => {
    const add = firstValueFrom(service.add(1)).catch((e: unknown) => e);
    http
      .expectOne('/api/watchlist')
      .flush({ title: 'This movie is already on your watchlist.' }, { status: 409, statusText: 'Conflict' });
    const addErr = await add;
    expect(addErr).toBeInstanceOf(MoviesError);
    expect((addErr as MoviesError).message).toBe(ALREADY_ON_WATCHLIST);
    expect((addErr as MoviesError).status).toBe(409);

    const markWatched = firstValueFrom(service.markWatched(1, 8)).catch((e: unknown) => e);
    http
      .expectOne('/api/watchlist/1/watched')
      .flush({ title: 'This movie was rated concurrently; try again.' }, { status: 409, statusText: 'Conflict' });
    const markErr = await markWatched;
    expect(markErr).toBeInstanceOf(MoviesError);
    expect((markErr as MoviesError).message).toBe(ALREADY_WATCHED);
    expect((markErr as MoviesError).status).toBe(409);
  });
});
