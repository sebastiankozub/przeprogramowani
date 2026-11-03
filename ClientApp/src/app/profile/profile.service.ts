import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { mapErrors } from '../movies/movies.service';

/** A city from the catalog — the API exposes no coordinates. */
export interface City {
  id: number;
  name: string;
  voivodeship: string;
}

export interface Profile {
  email: string;
  city: City | null;
}

const PROFILE_API = '/api/profile';
const CITIES_API = '/api/cities';

export const UNKNOWN_CITY = 'Nie znaleziono tego miasta.';

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

  searchCities(q: string): Observable<City[]> {
    return this.http
      .get<City[]>(CITIES_API, { params: { q } })
      .pipe(mapErrors({ fallback: 'Nie udało się wyszukać miast.' }));
  }
}
