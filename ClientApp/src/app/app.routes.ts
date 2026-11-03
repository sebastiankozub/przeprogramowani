import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { LoginComponent } from './auth/login.component';
import { RegisterComponent } from './auth/register.component';
import { HomeComponent } from './home/home.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, title: 'Logowanie' },
  { path: 'register', component: RegisterComponent, title: 'Rejestracja' },
  { path: '', component: HomeComponent, canActivate: [authGuard], pathMatch: 'full' },
  {
    path: 'movies',
    loadComponent: () => import('./movies/movies.component').then((m) => m.MoviesComponent),
    canActivate: [authGuard],
    title: 'Obejrzane filmy',
  },
  {
    path: 'watchlist',
    loadComponent: () => import('./watchlist/watchlist.component').then((m) => m.WatchlistComponent),
    canActivate: [authGuard],
    title: 'Do obejrzenia',
  },
  {
    path: 'profile',
    loadComponent: () => import('./profile/profile.component').then((m) => m.ProfileComponent),
    canActivate: [authGuard],
    title: 'Profil',
  },
  { path: '**', redirectTo: '' },
];
