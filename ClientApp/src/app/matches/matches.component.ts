import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Movie } from '../movies/movies.service';
import { cityLabel } from '../profile/profile.service';
import { MoviesError } from '../shared/api-errors';
import { Match, MatchesService, PROFILE_INCOMPLETE } from './matches.service';

/** The signed-in user's ranked match list: shows the stored list and recomputes it on demand. */
@Component({
  selector: 'app-matches',
  imports: [MatButtonModule, MatProgressBarModule, RouterLink],
  templateUrl: './matches.component.html',
})
export class MatchesComponent implements OnInit {
  private readonly matchesApi = inject(MatchesService);

  protected readonly matches = signal<Match[]>([]);
  protected readonly loaded = signal(false);
  protected readonly searching = signal(false);
  /** True once a recompute succeeded in this visit — switches the empty-state wording. */
  protected readonly searched = signal(false);

  /** Inline message for the last failed operation (other than an incomplete profile). */
  protected readonly error = signal<string | null>(null);
  /** The last recompute was refused because the profile lacks a city or a display name. */
  protected readonly profileIncomplete = signal(false);
  protected readonly profileIncompleteMessage = PROFILE_INCOMPLETE;

  protected readonly cityLabel = cityLabel;

  async ngOnInit(): Promise<void> {
    try {
      this.matches.set(await firstValueFrom(this.matchesApi.list()));
    } catch (err) {
      this.showError(err);
    } finally {
      this.loaded.set(true);
    }
  }

  protected async search(): Promise<void> {
    if (this.searching()) {
      return;
    }
    this.searching.set(true);
    this.error.set(null);
    this.profileIncomplete.set(false);
    try {
      this.matches.set(await firstValueFrom(this.matchesApi.recompute()));
      this.searched.set(true);
    } catch (err) {
      this.showError(err);
    } finally {
      this.searching.set(false);
    }
  }

  /** "Tytuł (rok)". */
  protected movieLabel(movie: Movie): string {
    return movie.year ? `${movie.title} (${movie.year})` : movie.title;
  }

  /** How many shared titles the count includes beyond the (at most 5) listed ones; 0 when none are listed. */
  protected moreCount(total: number, listed: readonly Movie[]): number {
    return listed.length === 0 ? 0 : Math.max(0, total - listed.length);
  }

  private showError(err: unknown): void {
    if (err instanceof MoviesError && err.status === 409) {
      this.profileIncomplete.set(true);
    } else {
      this.error.set(err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.');
    }
  }
}
