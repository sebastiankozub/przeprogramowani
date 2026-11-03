import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { INVALID_RATING, Movie, WatchedMovie } from '../movies/movies.service';
import { mapErrors } from '../shared/api-errors';

export interface WatchlistItem {
  movie: Movie;
  addedAt: string;
}

const WATCHLIST_API = '/api/watchlist';

export const ALREADY_ON_WATCHLIST = 'Ten film jest już na liście do obejrzenia.';
/** Only a race with a concurrent rating of the same movie — a re-watch otherwise overwrites the rating. */
export const ALREADY_WATCHED = 'Ten film został właśnie oceniony w innym miejscu — spróbuj ponownie.';
const NOT_ON_WATCHLIST = 'Tego filmu nie ma już na Twojej liście do obejrzenia.';

/** Thin wrapper over the signed-in user's watchlist; failures surface as MoviesError with a Polish message. */
@Injectable({ providedIn: 'root' })
export class WatchlistService {
  private readonly http = inject(HttpClient);

  /** Newest first. */
  list(): Observable<WatchlistItem[]> {
    return this.http
      .get<WatchlistItem[]>(WATCHLIST_API)
      .pipe(mapErrors({ fallback: 'Nie udało się wczytać listy do obejrzenia.' }));
  }

  add(movieId: number): Observable<WatchlistItem> {
    return this.http.post<WatchlistItem>(WATCHLIST_API, { movieId }).pipe(
      mapErrors({
        notFound: 'Nie znaleziono tego filmu w katalogu.',
        conflict: ALREADY_ON_WATCHLIST,
        fallback: 'Nie udało się dodać filmu do listy.',
      }),
    );
  }

  remove(movieId: number): Observable<void> {
    return this.http.delete(`${WATCHLIST_API}/${movieId}`).pipe(
      map(() => undefined),
      mapErrors({
        notFound: NOT_ON_WATCHLIST,
        fallback: 'Nie udało się usunąć filmu z listy.',
      }),
    );
  }

  /** Moves the entry to the watched list with a rating. */
  markWatched(movieId: number, rating: number): Observable<WatchedMovie> {
    return this.http.post<WatchedMovie>(`${WATCHLIST_API}/${movieId}/watched`, { rating }).pipe(
      mapErrors({
        badRequest: INVALID_RATING,
        notFound: NOT_ON_WATCHLIST,
        conflict: ALREADY_WATCHED,
        fallback: 'Nie udało się oznaczyć filmu jako obejrzanego.',
      }),
    );
  }
}
