import { Component, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { catchError, debounceTime, distinctUntilChanged, firstValueFrom, map, of, startWith, switchMap, tap } from 'rxjs';
import { MoviesError } from '../movies/movies.service';
import { City, Profile, ProfileService, cityLabel } from './profile.service';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_QUERY_LENGTH = 2;

/** Shows the signed-in user's email and home city and lets them pick a city (there is no way to clear it). */
@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './profile.component.html',
})
export class ProfileComponent implements OnInit {
  private readonly profiles = inject(ProfileService);

  protected readonly profile = signal<Profile | null>(null);
  protected readonly loaded = signal(false);
  protected readonly saving = signal(false);

  /** Inline message for the last failed operation. */
  protected readonly error = signal<string | null>(null);
  /** The message currently shown because of a failed search, cleared by the next successful one. */
  private searchError: string | null = null;

  /** Typed text while searching, the chosen City once an option is selected. */
  protected readonly control = new FormControl<string | City | null>(null);

  /** The city chosen in the autocomplete, null while the user is typing. */
  protected readonly selectedCity = toSignal(
    this.control.valueChanges.pipe(map((value) => (value && typeof value === 'object' ? value : null))),
    { initialValue: null },
  );

  protected readonly options = toSignal(
    this.control.valueChanges.pipe(
      startWith(this.control.value),
      map((value) => (typeof value === 'string' ? value.trim() : '')),
      debounceTime(SEARCH_DEBOUNCE_MS),
      distinctUntilChanged(),
      switchMap((q) =>
        q.length < MIN_QUERY_LENGTH
          ? of<City[]>([])
          : this.profiles.searchCities(q).pipe(
              tap(() => this.onSearchError(null)),
              catchError((err: unknown) => {
                this.onSearchError(errorMessage(err));
                return of<City[]>([]);
              }),
            ),
      ),
    ),
    { initialValue: [] },
  );

  protected readonly cityLabel = cityLabel;
  protected readonly displayCity = (value: string | City | null): string =>
    value && typeof value === 'object' ? cityLabel(value) : (value ?? '');

  async ngOnInit(): Promise<void> {
    try {
      this.profile.set(await firstValueFrom(this.profiles.get()));
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loaded.set(true);
    }
  }

  protected canSave(): boolean {
    return !this.saving() && this.selectedCity() !== null;
  }

  protected async save(): Promise<void> {
    const city = this.selectedCity();
    if (!city || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      this.profile.set(await firstValueFrom(this.profiles.setCity(city.id)));
      this.control.reset();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }

  private onSearchError(message: string | null): void {
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
}

function errorMessage(err: unknown): string {
  return err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.';
}
