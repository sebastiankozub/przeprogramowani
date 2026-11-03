import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { mapErrors } from '../shared/api-errors';

export interface Movie {
  id: number;
  title: string;
  originalTitle: string | null;
  year: number | null;
}

export interface WatchedMovie {
  movie: Movie;
  rating: number;
  updatedAt: string;
}

export const MIN_RATING = 1;
export const MAX_RATING = 10;

const MOVIES_API = '/api/movies';
const WATCHED_API = '/api/watched';

export const INVALID_RATING = `Ocena musi być liczbą od ${MIN_RATING} do ${MAX_RATING}.`;
export const DUPLICATE_MESSAGE = 'Ten film jest już na Twojej liście — zmień ocenę poniżej.';

/** Thin wrapper over the movie catalog and the signed-in user's watched list. */
@Injectable({ providedIn: 'root' })
export class MoviesService {
  private readonly http = inject(HttpClient);

  search(q: string): Observable<Movie[]> {
    return this.http
      .get<Movie[]>(MOVIES_API, { params: { q } })
      .pipe(mapErrors({ fallback: 'Nie udało się wyszukać filmów.' }));
  }

  listWatched(): Observable<WatchedMovie[]> {
    return this.http
      .get<WatchedMovie[]>(WATCHED_API)
      .pipe(mapErrors({ fallback: 'Nie udało się wczytać listy obejrzanych filmów.' }));
  }

  add(movieId: number, rating: number): Observable<WatchedMovie> {
    return this.http.post<WatchedMovie>(WATCHED_API, { movieId, rating }).pipe(
      mapErrors({
        badRequest: INVALID_RATING,
        notFound: 'Nie znaleziono tego filmu w katalogu.',
        conflict: DUPLICATE_MESSAGE,
        fallback: 'Nie udało się dodać filmu.',
      }),
    );
  }

  updateRating(movieId: number, rating: number): Observable<WatchedMovie> {
    return this.http.put<WatchedMovie>(`${WATCHED_API}/${movieId}`, { rating }).pipe(
      mapErrors({
        badRequest: INVALID_RATING,
        notFound: 'Tego filmu nie ma już na Twojej liście.',
        fallback: 'Nie udało się zmienić oceny.',
      }),
    );
  }

  remove(movieId: number): Observable<void> {
    return this.http.delete(`${WATCHED_API}/${movieId}`).pipe(
      map(() => undefined),
      mapErrors({
        notFound: 'Tego filmu nie ma już na Twojej liście.',
        fallback: 'Nie udało się usunąć filmu.',
      }),
    );
  }
}
