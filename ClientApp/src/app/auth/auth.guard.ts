import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Add `canActivate: [authGuard]` to every product route. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const user = await inject(AuthService).loadMe();
  return user ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
