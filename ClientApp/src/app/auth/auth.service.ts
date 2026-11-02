import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  email: string;
}

interface Credentials {
  email: string;
  password: string;
}

const API = '/api/auth';

const REGISTER_ERRORS: Record<string, string> = {
  PasswordTooShort: 'Hasło musi mieć co najmniej 8 znaków.',
  DuplicateUserName: 'Konto z tym adresem email już istnieje.',
  DuplicateEmail: 'Konto z tym adresem email już istnieje.',
  InvalidEmail: 'Nieprawidłowy adres email.',
  InvalidUserName: 'Nieprawidłowy adres email.',
};

const NETWORK_ERROR = 'Nie udało się połączyć z serwerem. Spróbuj ponownie.';

/** Error carrying a user-facing (Polish) message, thrown by AuthService methods. */
export class AuthError extends Error {}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly user = signal<CurrentUser | null>(null);
  private me: Promise<CurrentUser | null> | null = null;

  /** `null` = anonymous. */
  readonly currentUser = this.user.asReadonly();

  /** Resolves the session once (cached); 401 and network errors resolve to anonymous, never reject. */
  loadMe(): Promise<CurrentUser | null> {
    this.me ??= firstValueFrom(this.http.get<CurrentUser>(`${API}/me`)).then(
      (me) => this.setUser({ email: me.email }),
      (err: unknown) => {
        // Only a definitive 401 is cached; transient failures are retried on the next navigation.
        if (!(err instanceof HttpErrorResponse && err.status === 401)) {
          this.me = null;
        }
        this.user.set(null);
        return null;
      },
    );
    return this.me;
  }

  async login(credentials: Credentials): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/login?useCookies=true`, credentials));
    } catch (err) {
      throw new AuthError(loginErrorMessage(err));
    }
    this.setUser({ email: credentials.email });
  }

  /** The register endpoint does not set a cookie, so a successful registration is followed by login. */
  async register(credentials: Credentials): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/register`, credentials));
    } catch (err) {
      throw new AuthError(registerErrorMessage(err));
    }
    await this.login(credentials);
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/logout`, null));
    } catch {
      // 401 means the session is already gone; either way the client is anonymous now.
    }
    this.setUser(null);
  }

  private setUser(user: CurrentUser | null): CurrentUser | null {
    this.user.set(user);
    this.me = Promise.resolve(user);
    return user;
  }
}

function loginErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse && err.status === 401) {
    return err.error?.detail === 'LockedOut'
      ? 'Konto tymczasowo zablokowane. Spróbuj ponownie za kilka minut.'
      : 'Nieprawidłowy email lub hasło.';
  }
  return NETWORK_ERROR;
}

function registerErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse && err.status === 400) {
    const errors = (err.error?.errors ?? {}) as Record<string, string[]>;
    const messages = Object.entries(errors).map(
      ([code, texts]) => REGISTER_ERRORS[code] ?? texts.join(' '),
    );
    if (messages.length > 0) {
      return [...new Set(messages)].join(' ');
    }
    return err.error?.title ?? 'Rejestracja nie powiodła się.';
  }
  return NETWORK_ERROR;
}
