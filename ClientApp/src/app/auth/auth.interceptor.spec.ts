import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let clearSession: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    clearSession = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { clearSession } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  async function respond401(url: string): Promise<void> {
    const call = firstValueFrom(TestBed.inject(HttpClient).get(url));
    TestBed.inject(HttpTestingController).expectOne(url).flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(call).rejects.toBeTruthy();
  }

  it('clears the session and redirects to /login on a 401 from a product API', async () => {
    await respond401('/api/movies');

    expect(clearSession).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledOnce();
    expect(String(navigate.mock.calls[0][0])).toContain('/login?returnUrl=');
  });

  it('leaves auth endpoints to handle their own 401s', async () => {
    await respond401('/api/auth/me');

    expect(clearSession).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
