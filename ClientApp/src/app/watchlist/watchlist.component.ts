import { Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { MovieSearchComponent } from '../movies/movie-search.component';
import { MAX_RATING, MIN_RATING, Movie, MoviesError } from '../movies/movies.service';
import { WatchlistItem, WatchlistService } from './watchlist.service';

@Component({
  selector: 'app-watchlist',
  imports: [MatButtonModule, MatFormFieldModule, MatSelectModule, MovieSearchComponent],
  templateUrl: './watchlist.component.html',
})
export class WatchlistComponent implements OnInit {
  private readonly watchlist = inject(WatchlistService);
  private readonly search = viewChild.required(MovieSearchComponent);

  protected readonly ratings = Array.from({ length: MAX_RATING - MIN_RATING + 1 }, (_, i) => MIN_RATING + i);

  /** The movie chosen in the shared search, null while the user is typing. */
  protected readonly selectedMovie = signal<Movie | null>(null);

  /** Inline message for the last failed operation. */
  protected readonly error = signal<string | null>(null);
  /** The message currently shown because of a failed search, cleared by the next successful one. */
  private searchError: string | null = null;

  protected readonly items = signal<WatchlistItem[]>([]);
  protected readonly loaded = signal(false);
  protected readonly adding = signal(false);
  /** Movie ids with a removal or "watched" call in flight. */
  protected readonly busy = signal<ReadonlySet<number>>(new Set());
  /** Rating picked per entry, waiting for "Obejrzałem". */
  protected readonly pickedRatings = signal<ReadonlyMap<number, number>>(new Map());

  async ngOnInit(): Promise<void> {
    try {
      this.items.set(await firstValueFrom(this.watchlist.list()));
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loaded.set(true);
    }
  }

  protected canAdd(): boolean {
    return !this.adding() && this.selectedMovie() !== null;
  }

  protected async add(): Promise<void> {
    const movie = this.selectedMovie();
    if (!movie || this.adding()) {
      return;
    }
    this.adding.set(true);
    this.error.set(null);
    try {
      const item = await firstValueFrom(this.watchlist.add(movie.id));
      this.items.update((list) => [item, ...list.filter((entry) => entry.movie.id !== item.movie.id)]);
      this.search().reset();
    } catch (err) {
      this.error.set(errorMessage(err));
      // 409: the entry exists but may be missing locally (added in another tab) — show the server's list.
      await this.reloadIfStale(err, [409]);
    } finally {
      this.adding.set(false);
    }
  }

  protected async remove(item: WatchlistItem): Promise<void> {
    const movieId = item.movie.id;
    this.setBusy(movieId, true);
    this.error.set(null);
    try {
      await firstValueFrom(this.watchlist.remove(movieId));
      this.dropEntry(movieId);
    } catch (err) {
      this.error.set(errorMessage(err));
      await this.reloadIfStale(err, [404]);
    } finally {
      this.setBusy(movieId, false);
    }
  }

  protected async markWatched(item: WatchlistItem): Promise<void> {
    const movieId = item.movie.id;
    const rating = this.pickedRatings().get(movieId);
    if (rating === undefined || this.isBusy(movieId)) {
      return;
    }
    this.setBusy(movieId, true);
    this.error.set(null);
    try {
      await firstValueFrom(this.watchlist.markWatched(movieId, rating));
      this.dropEntry(movieId);
    } catch (err) {
      this.error.set(errorMessage(err));
      // 404: already gone from the watchlist; 409: rated concurrently elsewhere — show the server's list.
      await this.reloadIfStale(err, [404, 409]);
    } finally {
      this.setBusy(movieId, false);
    }
  }

  protected pickRating(movieId: number, rating: number): void {
    this.pickedRatings.update((ratings) => new Map(ratings).set(movieId, rating));
  }

  protected pickedRating(movieId: number): number | null {
    return this.pickedRatings().get(movieId) ?? null;
  }

  protected onSearchError(message: string | null): void {
    if (message === null) {
      if (this.searchError !== null && this.error() === this.searchError) {
        this.error.set(null);
      }
      this.searchError = null;
    } else {
      this.searchError = message;
      this.error.set(message);
    }
  }

  protected isBusy(movieId: number): boolean {
    return this.busy().has(movieId);
  }

  private dropEntry(movieId: number): void {
    this.items.update((list) => list.filter((entry) => entry.movie.id !== movieId));
    this.pickedRatings.update((ratings) => {
      const next = new Map(ratings);
      next.delete(movieId);
      return next;
    });
  }

  /** After a status that means "local list is out of date", replace it with the server's list (error stays shown). */
  private async reloadIfStale(err: unknown, staleStatuses: readonly number[]): Promise<void> {
    if (!(err instanceof MoviesError) || !staleStatuses.includes(err.status)) {
      return;
    }
    try {
      this.items.set(await firstValueFrom(this.watchlist.list()));
    } catch {
      // Keep the current list; the original error message is already shown.
    }
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
