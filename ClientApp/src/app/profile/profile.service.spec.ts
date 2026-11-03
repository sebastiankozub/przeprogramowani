import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { ProfileService, UNKNOWN_CITY, cityLabel } from './profile.service';

describe('ProfileService', () => {
  let service: ProfileService;
  let http: HttpTestingController;

  const krakow = { id: 7, name: 'Kraków', voivodeship: 'małopolskie' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('searches cities with the query parameter', async () => {
    const call = firstValueFrom(service.searchCities('kra'));

    const req = http.expectOne((r) => r.url === '/api/cities');
    expect(req.request.method).toBe('GET');
    expect(req.request.urlWithParams).toBe('/api/cities?q=kra');
    req.flush([krakow]);

    await expect(call).resolves.toEqual([krakow]);
  });

  it('loads the profile', async () => {
    const profile = { email: 'a@example.com', city: null };
    const call = firstValueFrom(service.get());

    const req = http.expectOne('/api/profile');
    expect(req.request.method).toBe('GET');
    req.flush(profile);

    await expect(call).resolves.toEqual(profile);
  });

  it('sets the city by putting cityId', async () => {
    const profile = { email: 'a@example.com', city: krakow };
    const call = firstValueFrom(service.setCity(7));

    const req = http.expectOne('/api/profile/city');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ cityId: 7 });
    req.flush(profile);

    await expect(call).resolves.toEqual(profile);
  });

  it('maps a 404 on set to the Polish unknown-city message', async () => {
    const call = firstValueFrom(service.setCity(999)).catch((e: unknown) => e);
    http.expectOne('/api/profile/city').flush(null, { status: 404, statusText: 'Not Found' });

    const err = await call;
    expect(err).toBeInstanceOf(MoviesError);
    expect((err as MoviesError).message).toBe(UNKNOWN_CITY);
    expect((err as MoviesError).status).toBe(404);
  });

  it('labels a city with its voivodeship', () => {
    expect(cityLabel(krakow)).toBe('Kraków (małopolskie)');
  });
});
