import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthError, AuthService } from '../auth/auth.service';
import { City, ProfileService, cityLabel } from '../profile/profile.service';

@Component({
  selector: 'app-home',
  imports: [MatButtonModule, RouterLink],
  templateUrl: './home.component.html',
})
export class HomeComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly profiles = inject(ProfileService);
  private readonly router = inject(Router);

  protected readonly user = this.auth.currentUser;
  protected readonly error = signal<string | null>(null);
  /** The home city; undefined until the profile loads (and when it fails — then home shows neither city nor nudge). */
  protected readonly city = signal<City | null | undefined>(undefined);
  /** The display name; undefined until the profile loads (and when it fails), like the city. */
  protected readonly displayName = signal<string | null | undefined>(undefined);
  protected readonly cityLabel = cityLabel;

  async ngOnInit(): Promise<void> {
    try {
      const profile = await firstValueFrom(this.profiles.get());
      this.displayName.set(profile.displayName);
      this.city.set(profile.city);
    } catch {
      // The name and city lines are optional on home; a failed load just leaves them out.
    }
  }

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
