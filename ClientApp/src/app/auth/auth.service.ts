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

/** Registration succeeded but the follow-up login failed — the account exists, the user should log in. */
export class AccountCreatedError extends AuthError {}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly user = signal<CurrentUser | null>(null);
  private me: Promise<CurrentUser | null> | null = null;

  /** `null` = anonymous. */
  readonly currentUser = this.user.asReadonly();

  /** Resolves the session once (cached); 401 and network errors resolve to anonymous, never reject. */
  loadMe(): Promise<CurrentUser | null> {
    if (this.me) {
      return this.me;
    }
    const request: Promise<CurrentUser | null> = firstValueFrom(this.http.get<CurrentUser>(`${API}/me`)).then(
      // A superseded request (login/logout/clearSession happened meanwhile) must not overwrite newer state.
      (me) => (request === this.me ? this.setUser({ email: me.email }) : this.user()),
      (err: unknown) => {
        if (request !== this.me) {
          return this.user();
        }
        // Only a definitive 401 is cached; transient failures are retried on the next navigation.
        if (!(err instanceof HttpErrorResponse && err.status === 401)) {
          this.me = null;
        }
        this.user.set(null);
        return null;
      },
    );
    this.me = request;
    return request;
  }

  /** Drops the cached session without calling the API — used when any API call answers 401. */
  clearSession(): void {
    this.setUser(null);
  }

  async login(credentials: Credentials): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/login?useCookies=true`, credentials));
    } catch (err) {
      throw new AuthError(loginErrorMessage(err));
    }
    // Take the email as the server stores it, not as typed.
    this.me = null;
    if (!(await this.loadMe())) {
      throw new AuthError(NETWORK_ERROR);
    }
  }

  /** The register endpoint does not set a cookie, so a successful registration is followed by login. */
  async register(credentials: Credentials): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/register`, credentials));
    } catch (err) {
      throw new AuthError(registerErrorMessage(err));
    }
    try {
      await this.login(credentials);
    } catch {
      throw new AccountCreatedError('Konto zostało utworzone. Zaloguj się.');
    }
  }

  /** Clears the client state only when the server confirms the session is gone (204, or 401 = already gone). */
  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/logout`, null));
    } catch (err) {
      if (!(err instanceof HttpErrorResponse && err.status === 401)) {
        throw new AuthError('Nie udało się wylogować. Spróbuj ponownie.');
      }
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
