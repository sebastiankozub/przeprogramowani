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
  /** The signed-in user has added this candidate to their friends. */
  isFriend: boolean;
}

/** The server recomputes the list in the background after every change that can affect it. */
export interface MatchStatus {
  /** A recompute was requested and has not finished yet. */
  pending: boolean;
  /** The profile has both a city and a display name — without them there is nothing to match on. */
  profileComplete: boolean;
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

  /** Whether a background recompute of the list is still on its way, and whether the profile allows matching. */
  status(): Observable<MatchStatus> {
    return this.http.get<MatchStatus>(`${MATCHES_API}/status`).pipe(
      mapErrors({
        fallback: 'Nie udało się sprawdzić stanu dopasowań.',
      }),
    );
  }
}
