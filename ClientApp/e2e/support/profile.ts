import { APIRequestContext, expect } from '@playwright/test';
import { E2eUser } from './users';

export interface MatchableProfile {
  /** City name exactly as the catalog lists it, e.g. "Kraków". */
  city: string;
  /** Catalog title and year of a movie the user rates. */
  movieTitle: string;
  movieYear: number;
  rating: number;
}

/**
 * Makes a signed-in user matchable through the public API: display name + city + one rated movie.
 * The request context must already carry the user's session cookie (see registerViaApi). Works with a
 * separate `request.newContext` as well as with `page.request`, which shares the browser's cookies.
 */
export async function completeProfile(
  request: APIRequestContext,
  user: E2eUser,
  spec: MatchableProfile,
): Promise<void> {
  const name = await request.put('/api/profile/display-name', { data: { displayName: user.displayName } });
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
}
