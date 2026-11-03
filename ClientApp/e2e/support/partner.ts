import { APIRequestContext, expect } from '@playwright/test';
import { E2eUser, registerViaApi, uniqueUser } from './users';

export interface PartnerSpec {
  /** City name exactly as the catalog lists it, e.g. "Kraków". */
  city: string;
  /** Catalog title and year of a movie the partner rates. */
  movieTitle: string;
  movieYear: number;
  rating: number;
}

/**
 * Creates a complete, matchable partner (display name + city + one rated movie) through the public API, so a
 * browser test has a strong candidate regardless of whoever else is in the local database.
 * Follows scripts/demo-accounts.mjs. The request context must be a fresh one: it keeps the partner's cookie.
 */
export async function createPartner(request: APIRequestContext, spec: PartnerSpec): Promise<E2eUser> {
  const partner = uniqueUser('Partner');
  await registerViaApi(request, partner);

  const name = await request.put('/api/profile/display-name', { data: { displayName: partner.displayName } });
  expect(name.status(), 'set display name').toBe(200);

  const cities = await request.get(`/api/cities?q=${encodeURIComponent(spec.city)}`);
  expect(cities.ok(), 'search city').toBeTruthy();
  const city = ((await cities.json()) as { id: number; name: string }[]).find((c) => c.name === spec.city);
  expect(city, `city ${spec.city} in catalog`).toBeTruthy();
  const setCity = await request.put('/api/profile/city', { data: { cityId: city!.id } });
  expect(setCity.status(), 'set city').toBe(200);

  const movies = await request.get(`/api/movies?q=${encodeURIComponent(spec.movieTitle)}`);
  expect(movies.ok(), 'search movie').toBeTruthy();
  const movie = ((await movies.json()) as { id: number; title: string; year: number }[]).find(
    (m) => m.title === spec.movieTitle && m.year === spec.movieYear,
  );
  expect(movie, `movie ${spec.movieTitle} (${spec.movieYear}) in catalog`).toBeTruthy();
  const rate = await request.post('/api/watched', { data: { movieId: movie!.id, rating: spec.rating } });
  expect(rate.status(), 'rate movie').toBe(201);

  return partner;
}
