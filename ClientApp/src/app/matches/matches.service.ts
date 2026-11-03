import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Movie } from '../movies/movies.service';
import { City } from '../profile/profile.service';
import { mapErrors } from '../shared/api-errors';

/** One entry of the match list — only public, city-level data (no email, coordinates or distance). */
export interface Match {
  userId: string;
  displayName: string;
  city: City;
  /** 0–100. */
  score: number;
  sharedFavoritesCount: number;
  sharedWatchlistCount: number;
  /** At most 5. */
  sharedFavorites: Movie[];
  /** At most 5. */
  sharedWatchlist: Movie[];
  firstMatchedAt: string;
}

const MATCHES_API = '/api/matches';

export const PROFILE_INCOMPLETE = 'Uzupełnij miasto i nazwę w profilu, aby szukać dopasowań.';

/** Thin wrapper over the signed-in user's match list; failures surface as MoviesError with a Polish message. */
@Injectable({ providedIn: 'root' })
export class MatchesService {
  private readonly http = inject(HttpClient);

  /** The stored list, best first (possibly empty). */
  list(): Observable<Match[]> {
    return this.http.get<Match[]>(MATCHES_API).pipe(
      mapErrors({
        conflict: PROFILE_INCOMPLETE,
        fallback: 'Nie udało się wczytać dopasowań.',
      }),
    );
  }

  /** Recomputes the list and returns it, best first; 409 when the profile lacks a city or a display name. */
  recompute(): Observable<Match[]> {
    return this.http.post<Match[]>(`${MATCHES_API}/recompute`, null).pipe(
      mapErrors({
        conflict: PROFILE_INCOMPLETE,
        fallback: 'Nie udało się wyszukać dopasowań.',
      }),
    );
  }
}
