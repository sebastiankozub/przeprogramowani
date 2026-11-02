import { Component, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { catchError, debounceTime, distinctUntilChanged, firstValueFrom, map, of, startWith, switchMap, tap } from 'rxjs';
import { MAX_RATING, MIN_RATING, Movie, MoviesError, MoviesService, WatchedMovie } from './movies.service';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_QUERY_LENGTH = 2;

/** "Tytuł (Oryginalny, rok)" — parts that are missing or equal to the title are skipped. */
export function movieLabel(movie: Movie): string {
  const details = [
    movie.originalTitle && movie.originalTitle !== movie.title ? movie.originalTitle : null,
    movie.year,
  ].filter((part) => part !== null && part !== '');
  return details.length > 0 ? `${movie.title} (${details.join(', ')})` : movie.title;
}

@Component({
  selector: 'app-movies',
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './movies.component.html',
})
export class MoviesComponent implements OnInit {
  private readonly movies = inject(MoviesService);

  protected readonly ratings = Array.from({ length: MAX_RATING - MIN_RATING + 1 }, (_, i) => MIN_RATING + i);

  /** Typed text while searching, the chosen Movie once an option is selected. */
  protected readonly movieControl = new FormControl<string | Movie | null>(null);
  protected readonly ratingControl = new FormControl<number | null>(null);
  protected readonly form = new FormGroup({ movie: this.movieControl, rating: this.ratingControl });

  private readonly movieValue = toSignal(this.movieControl.valueChanges, { initialValue: null });
  private readonly ratingValue = toSignal(this.ratingControl.valueChanges, { initialValue: null });

  /** Inline message for the last failed operation. */
  protected readonly error = signal<string | null>(null);
  /** The message currently shown because of a failed search, cleared by the next successful one. */
  private searchError: string | null = null;

  protected readonly options = toSignal(
    this.movieControl.valueChanges.pipe(
      startWith(this.movieControl.value),
      map((value) => (typeof value === 'string' ? value.trim() : '')),
      debounceTime(SEARCH_DEBOUNCE_MS),
      distinctUntilChanged(),
      switchMap((q) =>
        q.length < MIN_QUERY_LENGTH
          ? of<Movie[]>([])
          : this.movies.search(q).pipe(
              tap(() => this.clearSearchError()),
              catchError((err: unknown) => {
                this.searchError = errorMessage(err);
                this.error.set(this.searchError);
                return of<Movie[]>([]);
              }),
            ),
      ),
    ),
    { initialValue: [] },
  );

  protected readonly watched = signal<WatchedMovie[]>([]);
  protected readonly loaded = signal(false);
  protected readonly adding = signal(false);
  /** Movie ids with a rating change or removal in flight. */
  protected readonly busy = signal<ReadonlySet<number>>(new Set());

  protected readonly movieLabel = movieLabel;
  protected readonly displayMovie = (value: string | Movie | null): string =>
    value && typeof value === 'object' ? movieLabel(value) : (value ?? '');

  async ngOnInit(): Promise<void> {
    try {
      this.watched.set(await firstValueFrom(this.movies.listWatched()));
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loaded.set(true);
    }
  }

  protected canAdd(): boolean {
    const movie = this.movieValue();
    return !this.adding() && movie !== null && typeof movie === 'object' && this.ratingValue() !== null;
  }

  protected async add(): Promise<void> {
    const movie = this.movieControl.value;
    const rating = this.ratingControl.value;
    if (!movie || typeof movie !== 'object' || rating === null || this.adding()) {
      return;
    }
    this.adding.set(true);
    this.error.set(null);
    try {
      const entry = await firstValueFrom(this.movies.add(movie.id, rating));
      this.watched.update((list) => [entry, ...list.filter((item) => item.movie.id !== entry.movie.id)]);
      this.form.reset();
    } catch (err) {
      this.error.set(errorMessage(err));
      // 409: the entry exists but may be missing locally (added in another tab) — show the server's list.
      await this.reloadIfStale(err, 409);
    } finally {
      this.adding.set(false);
    }
  }

  protected async changeRating(item: WatchedMovie, select: MatSelect): Promise<void> {
    const rating = select.value as number;
    const movieId = item.movie.id;
    this.setBusy(movieId, true);
    this.error.set(null);
    try {
      const updated = await firstValueFrom(this.movies.updateRating(movieId, rating));
      // The server lists most recently updated first — mirror that so a reload does not reorder.
      this.watched.update((list) => [updated, ...list.filter((entry) => entry.movie.id !== movieId)]);
    } catch (err) {
      // Show the rating that is actually stored.
      select.value = item.rating;
      this.error.set(errorMessage(err));
      await this.reloadIfStale(err, 404);
    } finally {
      this.setBusy(movieId, false);
    }
  }

  protected async remove(item: WatchedMovie): Promise<void> {
    const movieId = item.movie.id;
    this.setBusy(movieId, true);
    this.error.set(null);
    try {
      await firstValueFrom(this.movies.remove(movieId));
      this.watched.update((list) => list.filter((entry) => entry.movie.id !== movieId));
    } catch (err) {
      this.error.set(errorMessage(err));
      await this.reloadIfStale(err, 404);
    } finally {
      this.setBusy(movieId, false);
    }
  }

  /** After a status that means "local list is out of date", replace it with the server's list (error stays shown). */
  private async reloadIfStale(err: unknown, staleStatus: number): Promise<void> {
    if (!(err instanceof MoviesError) || err.status !== staleStatus) {
      return;
    }
    try {
      this.watched.set(await firstValueFrom(this.movies.listWatched()));
    } catch {
      // Keep the current list; the original error message is already shown.
    }
  }

  private clearSearchError(): void {
    if (this.searchError !== null && this.error() === this.searchError) {
      this.error.set(null);
    }
    this.searchError = null;
  }

  protected isBusy(movieId: number): boolean {
    return this.busy().has(movieId);
  }

  private setBusy(movieId: number, busy: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (busy) {
        next.add(movieId);
      } else {
        next.delete(movieId);
      }
      return next;
    });
  }
}

function errorMessage(err: unknown): string {
  return err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.';
}
