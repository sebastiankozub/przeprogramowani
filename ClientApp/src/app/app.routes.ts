import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { LoginComponent } from './auth/login.component';
import { RegisterComponent } from './auth/register.component';
import { HomeComponent } from './home/home.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, title: 'Logowanie' },
  { path: 'register', component: RegisterComponent, title: 'Rejestracja' },
  { path: '', component: HomeComponent, canActivate: [authGuard], pathMatch: 'full' },
  { path: '**', redirectTo: '' },
];
