import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';

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

const NETWORK_ERROR = 'Nie udało się połączyć z serwerem. Spróbuj ponownie.';
export const INVALID_RATING = `Ocena musi być liczbą od ${MIN_RATING} do ${MAX_RATING}.`;
const SESSION_EXPIRED = 'Sesja wygasła. Zaloguj się ponownie.';
export const DUPLICATE_MESSAGE = 'Ten film jest już na Twojej liście — zmień ocenę poniżej.';

/** Error carrying a user-facing (Polish) message, emitted by MoviesService methods. */
export class MoviesError extends Error {
  /** HTTP status of the failed call (0 = network error or non-HTTP failure). */
  constructor(
    message: string,
    readonly status = 0,
  ) {
    super(message);
  }
}

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

export interface ErrorMessages {
  /** Only operations that send a rating can fail validation; others fall back. */
  badRequest?: string;
  notFound?: string;
  /** Defaults to the watched-list duplicate message. */
  conflict?: string;
  fallback: string;
}

/** Converts HTTP failures into MoviesError with a Polish message (401 redirect is the interceptor's job). */
export function mapErrors<T>(messages: ErrorMessages) {
  return catchError<T, Observable<never>>((err: unknown) =>
    throwError(() => new MoviesError(errorMessage(err, messages), err instanceof HttpErrorResponse ? err.status : 0)),
  );
}

function errorMessage(err: unknown, messages: ErrorMessages): string {
  if (!(err instanceof HttpErrorResponse)) {
    return messages.fallback;
  }
  switch (err.status) {
    case 0:
      return NETWORK_ERROR;
    case 400:
      return messages.badRequest ?? messages.fallback;
    case 401:
      return SESSION_EXPIRED;
    case 404:
      return messages.notFound ?? messages.fallback;
    case 409:
      return messages.conflict ?? DUPLICATE_MESSAGE;
    default:
      return messages.fallback;
  }
}
