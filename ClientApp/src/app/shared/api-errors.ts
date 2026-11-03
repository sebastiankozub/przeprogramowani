import { HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, throwError } from 'rxjs';

const NETWORK_ERROR = 'Nie udało się połączyć z serwerem. Spróbuj ponownie.';
const SESSION_EXPIRED = 'Sesja wygasła. Zaloguj się ponownie.';

/** Error carrying a user-facing (Polish) message, emitted by the API services. */
export class MoviesError extends Error {
  /** HTTP status of the failed call (0 = network error or non-HTTP failure). */
  constructor(
    message: string,
    readonly status = 0,
  ) {
    super(message);
  }
}

export interface ErrorMessages {
  /** Only operations that send validated input can fail validation; others fall back. */
  badRequest?: string;
  notFound?: string;
  /** Only operations that can hit a duplicate set this; others fall back. */
  conflict?: string;
  fallback: string;
}

/** Converts HTTP failures into MoviesError with a Polish message (401 redirect is the interceptor's job). */
export function mapErrors<T>(messages: ErrorMessages) {
  return catchError<T, Observable<never>>((err: unknown) =>
    throwError(() => new MoviesError(errorMessage(err, messages), err instanceof HttpErrorResponse ? err.status : 0)),
  );
}

function errorMessage(err: unknown, messages: ErrorMessages): string {
  if (!(err instanceof HttpErrorResponse)) {
    return messages.fallback;
  }
  switch (err.status) {
    case 0:
      return NETWORK_ERROR;
    case 400:
      return messages.badRequest ?? messages.fallback;
    case 401:
      return SESSION_EXPIRED;
    case 404:
      return messages.notFound ?? messages.fallback;
    case 409:
      return messages.conflict ?? messages.fallback;
    default:
      return messages.fallback;
  }
}
