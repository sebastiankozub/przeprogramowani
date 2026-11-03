import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { mapErrors } from '../shared/api-errors';

/** A city from the catalog — the API exposes no coordinates. */
export interface City {
  id: number;
  name: string;
  voivodeship: string;
}

export interface Profile {
  email: string;
  /** Public name shown to other users in matches; null until the user sets one. */
  displayName: string | null;
  city: City | null;
}

const PROFILE_API = '/api/profile';
const CITIES_API = '/api/cities';

export const UNKNOWN_CITY = 'Nie znaleziono tego miasta.';
/** Mirrors the backend limit, applied after trimming. */
export const DISPLAY_NAME_MAX_LENGTH = 40;
export const INVALID_DISPLAY_NAME = `Nazwa musi mieć od 1 do ${DISPLAY_NAME_MAX_LENGTH} znaków.`;

/** "Nazwa (województwo)". */
export function cityLabel(city: City): string {
  return `${city.name} (${city.voivodeship})`;
}

/** Thin wrapper over the signed-in user's profile and the city catalog; failures surface as MoviesError. */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);

  get(): Observable<Profile> {
    return this.http.get<Profile>(PROFILE_API).pipe(mapErrors({ fallback: 'Nie udało się wczytać profilu.' }));
  }

  setCity(cityId: number): Observable<Profile> {
    return this.http.put<Profile>(`${PROFILE_API}/city`, { cityId }).pipe(
      mapErrors({
        notFound: UNKNOWN_CITY,
        fallback: 'Nie udało się zapisać miasta.',
      }),
    );
  }

  /** The server trims the name and requires 1–40 characters after trimming. */
  setDisplayName(displayName: string): Observable<Profile> {
    return this.http.put<Profile>(`${PROFILE_API}/display-name`, { displayName }).pipe(
      mapErrors({
        badRequest: INVALID_DISPLAY_NAME,
        fallback: 'Nie udało się zapisać nazwy.',
      }),
    );
  }

  searchCities(q: string): Observable<City[]> {
    return this.http
      .get<City[]>(CITIES_API, { params: { q } })
      .pipe(mapErrors({ fallback: 'Nie udało się wyszukać miast.' }));
  }
}
