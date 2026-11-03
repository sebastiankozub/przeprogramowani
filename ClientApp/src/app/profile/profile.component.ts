import { Component, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { catchError, debounceTime, distinctUntilChanged, firstValueFrom, map, of, startWith, switchMap, tap } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { City, DISPLAY_NAME_MAX_LENGTH, Profile, ProfileService, cityLabel } from './profile.service';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_QUERY_LENGTH = 2;

/**
 * Shows the signed-in user's email, display name and home city; lets them set the name and pick a city
 * (neither can be cleared).
 */
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
  protected readonly savingName = signal(false);

  protected readonly displayNameMaxLength = DISPLAY_NAME_MAX_LENGTH;
  /** The display name being edited; prefilled with the stored one. */
  protected readonly nameControl = new FormControl('', { nonNullable: true });
  private readonly nameValue = toSignal(this.nameControl.valueChanges, { initialValue: '' });

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
      const profile = await firstValueFrom(this.profiles.get());
      this.profile.set(profile);
      this.nameControl.setValue(profile.displayName ?? '');
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

  /** 1–40 characters after trimming and different from the stored name. */
  protected canSaveName(): boolean {
    const name = this.nameValue().trim();
    return (
      !this.savingName() &&
      name.length > 0 &&
      name.length <= DISPLAY_NAME_MAX_LENGTH &&
      name !== (this.profile()?.displayName ?? '')
    );
  }

  protected async saveName(): Promise<void> {
    if (!this.canSaveName()) {
      return;
    }
    this.savingName.set(true);
    this.error.set(null);
    try {
      const profile = await firstValueFrom(this.profiles.setDisplayName(this.nameValue().trim()));
      this.profile.set(profile);
      this.nameControl.setValue(profile.displayName ?? '');
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.savingName.set(false);
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
