import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService, CurrentUser } from './auth.service';

describe('authGuard', () => {
  function runGuard(user: CurrentUser | null, url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { loadMe: () => Promise.resolve(user) } },
      ],
    });
    const state = { url } as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, state));
  }

  it('allows navigation when a user is signed in', async () => {
    expect(await runGuard({ email: 'a@example.com' }, '/')).toBe(true);
  });

  it('redirects anonymous users to /login with returnUrl', async () => {
    const result = await runGuard(null, '/movies?page=2');

    expect(result).toBeInstanceOf(UrlTree);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(result as UrlTree)).toBe(
      router.serializeUrl(router.createUrlTree(['/login'], { queryParams: { returnUrl: '/movies?page=2' } })),
    );
    expect((result as UrlTree).queryParams['returnUrl']).toBe('/movies?page=2');
  });
});
