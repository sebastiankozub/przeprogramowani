import { Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { MovieSearchComponent } from './movie-search.component';
import { MAX_RATING, MIN_RATING, Movie, MoviesError, MoviesService, WatchedMovie } from './movies.service';

@Component({
  selector: 'app-movies',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MovieSearchComponent],
  templateUrl: './movies.component.html',
})
export class MoviesComponent implements OnInit {
  private readonly movies = inject(MoviesService);
  private readonly search = viewChild.required(MovieSearchComponent);

  protected readonly ratings = Array.from({ length: MAX_RATING - MIN_RATING + 1 }, (_, i) => MIN_RATING + i);

  protected readonly ratingControl = new FormControl<number | null>(null);
  protected readonly form = new FormGroup({ rating: this.ratingControl });

  /** The movie chosen in the shared search, null while the user is typing. */
  protected readonly selectedMovie = signal<Movie | null>(null);
  private readonly ratingValue = toSignal(this.ratingControl.valueChanges, { initialValue: null });

  /** Inline message for the last failed operation. */
  protected readonly error = signal<string | null>(null);
  /** The message currently shown because of a failed search, cleared by the next successful one. */
  private searchError: string | null = null;

  protected readonly watched = signal<WatchedMovie[]>([]);
  protected readonly loaded = signal(false);
  protected readonly adding = signal(false);
  /** Movie ids with a rating change or removal in flight. */
  protected readonly busy = signal<ReadonlySet<number>>(new Set());

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
    return !this.adding() && this.selectedMovie() !== null && this.ratingValue() !== null;
  }

  protected async add(): Promise<void> {
    const movie = this.selectedMovie();
    const rating = this.ratingControl.value;
    if (!movie || rating === null || this.adding()) {
      return;
    }
    this.adding.set(true);
    this.error.set(null);
    try {
      const entry = await firstValueFrom(this.movies.add(movie.id, rating));
      this.watched.update((list) => [entry, ...list.filter((item) => item.movie.id !== entry.movie.id)]);
      this.search().reset();
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

  protected onSearchError(message: string | null): void {
    if (message === null) {
      this.clearSearchError();
    } else {
      this.searchError = message;
      this.error.set(message);
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
