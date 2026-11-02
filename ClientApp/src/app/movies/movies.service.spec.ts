import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { DUPLICATE_MESSAGE, MoviesError, MoviesService } from './movies.service';

describe('MoviesService', () => {
  let service: MoviesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MoviesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('searches the catalog with the query parameter', async () => {
    const movies = [{ id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 }];
    const call = firstValueFrom(service.search('ojciec'));

    const req = http.expectOne((r) => r.url === '/api/movies');
    expect(req.request.method).toBe('GET');
    expect(req.request.urlWithParams).toBe('/api/movies?q=ojciec');
    req.flush(movies);

    await expect(call).resolves.toEqual(movies);
  });

  it('adds a watched movie by posting movieId and rating', async () => {
    const entry = {
      movie: { id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 },
      rating: 9,
      updatedAt: '2026-11-02T10:00:00Z',
    };
    const call = firstValueFrom(service.add(1, 9));

    const req = http.expectOne('/api/watched');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ movieId: 1, rating: 9 });
    req.flush(entry, { status: 201, statusText: 'Created' });

    await expect(call).resolves.toEqual(entry);
  });

  it('surfaces the Polish duplicate message when the movie is already on the list', async () => {
    const call = firstValueFrom(service.add(1, 9));

    http
      .expectOne('/api/watched')
      .flush({ title: 'This movie is already on your watched list.' }, { status: 409, statusText: 'Conflict' });

    const err = await call.catch((e: unknown) => e);
    expect(err).toBeInstanceOf(MoviesError);
    expect((err as MoviesError).message).toBe(DUPLICATE_MESSAGE);
  });
});
