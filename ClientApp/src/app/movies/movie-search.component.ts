import { Component, inject, input, output } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { catchError, debounceTime, distinctUntilChanged, map, of, startWith, switchMap, tap } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { Movie, MoviesService } from './movies.service';

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

/**
 * Catalog autocomplete shared by the movie screens. Emits the chosen movie (null while the user is typing);
 * search failures are reported to the parent, which shows them in its own inline error area.
 */
@Component({
  selector: 'app-movie-search',
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatFormFieldModule, MatInputModule],
  templateUrl: './movie-search.component.html',
  host: { class: 'movies-search' },
})
export class MovieSearchComponent {
  private readonly movies = inject(MoviesService);

  readonly label = input('Szukaj filmu');
  /** The selected movie, or null when the field holds typed text or is empty. */
  readonly movieSelected = output<Movie | null>();
  /** A message when a search fails, null after the next successful search. */
  readonly searchError = output<string | null>();

  /** Typed text while searching, the chosen Movie once an option is selected. */
  protected readonly control = new FormControl<string | Movie | null>(null);

  protected readonly options = toSignal(
    this.control.valueChanges.pipe(
      startWith(this.control.value),
      map((value) => (typeof value === 'string' ? value.trim() : '')),
      debounceTime(SEARCH_DEBOUNCE_MS),
      distinctUntilChanged(),
      switchMap((q) =>
        q.length < MIN_QUERY_LENGTH
          ? of<Movie[]>([])
          : this.movies.search(q).pipe(
              tap(() => this.searchError.emit(null)),
              catchError((err: unknown) => {
                this.searchError.emit(err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.');
                return of<Movie[]>([]);
              }),
            ),
      ),
    ),
    { initialValue: [] },
  );

  protected readonly movieLabel = movieLabel;
  protected readonly displayMovie = (value: string | Movie | null): string =>
    value && typeof value === 'object' ? movieLabel(value) : (value ?? '');

  constructor() {
    this.control.valueChanges
      .pipe(
        map((value) => (value && typeof value === 'object' ? value : null)),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((movie) => this.movieSelected.emit(movie));
  }

  /** Clears the field (the parent calls this after a successful add). */
  reset(): void {
    this.control.reset();
  }
}
