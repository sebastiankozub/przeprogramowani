import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';
import { AuthError, AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-home',
  imports: [MatButtonModule, RouterLink],
  templateUrl: './home.component.html',
})
export class HomeComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly user = this.auth.currentUser;
  protected readonly error = signal<string | null>(null);

  protected async logout(): Promise<void> {
    this.error.set(null);
    try {
      await this.auth.logout();
    } catch (err) {
      this.error.set(err instanceof AuthError ? err.message : 'Wystąpił nieoczekiwany błąd.');
      return;
    }
    await this.router.navigateByUrl('/login');
  }
}
