import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { FriendsService } from '../friends/friends.service';
import { Movie } from '../movies/movies.service';
import { cityLabel } from '../profile/profile.service';
import { MoviesError } from '../shared/api-errors';
import { Match, MatchesService, PROFILE_INCOMPLETE } from './matches.service';

/** How often the status is re-checked while a background recompute is pending. */
export const STATUS_POLL_INTERVAL_MS = 2000;
/** Polling gives up after this long (15 checks) — the list then waits for a page refresh. */
export const STATUS_POLL_TIMEOUT_MS = 30000;

/**
 * The signed-in user's ranked match list. The server recomputes it in the background after every relevant
 * change; while that is pending the page shows an indicator, polls the status and reloads the list when done.
 */
@Component({
  selector: 'app-matches',
  imports: [MatButtonModule, MatProgressBarModule, RouterLink],
  templateUrl: './matches.component.html',
})
export class MatchesComponent implements OnInit, OnDestroy {
  private readonly matchesApi = inject(MatchesService);
  private readonly friendsApi = inject(FriendsService);

  protected readonly matches = signal<Match[]>([]);
  protected readonly loaded = signal(false);
  /** A background recompute is on its way; the list reloads by itself when it finishes. */
  protected readonly pending = signal(false);
  /** Polling gave up while the recompute was still pending. */
  protected readonly slow = signal(false);

  /** Inline message for the last failed operation (other than an incomplete profile). */
  protected readonly error = signal<string | null>(null);
  /** The profile lacks a city or a display name, so there is nothing to match on yet. */
  protected readonly profileIncomplete = signal(false);
  protected readonly profileIncompleteMessage = PROFILE_INCOMPLETE;

  protected readonly cityLabel = cityLabel;

  /** User ids with an add-friend call in flight. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollsLeft = 0;
  private destroyed = false;

  async ngOnInit(): Promise<void> {
    const [status, list] = await Promise.allSettled([
      firstValueFrom(this.matchesApi.status()),
      firstValueFrom(this.matchesApi.list()),
    ]);
    if (this.destroyed) {
      return;
    }
    if (list.status === 'fulfilled') {
      this.matches.set(list.value);
    } else {
      this.showError(list.reason);
    }
    if (status.status === 'fulfilled') {
      if (!status.value.profileComplete) {
        this.profileIncomplete.set(true);
      } else if (status.value.pending) {
        this.startPolling();
      }
    } else if (list.status === 'fulfilled') {
      this.showError(status.reason);
    }
    this.loaded.set(true);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopPolling();
  }

  /** "Tytuł (rok)". */
  protected movieLabel(movie: Movie): string {
    return movie.year ? `${movie.title} (${movie.year})` : movie.title;
  }

  /** How many shared titles the count includes beyond the (at most 5) listed ones; 0 when none are listed. */
  protected moreCount(total: number, listed: readonly Movie[]): number {
    return listed.length === 0 ? 0 : Math.max(0, total - listed.length);
  }

  protected isBusy(userId: string): boolean {
    return this.busy().has(userId);
  }

  /** Adds the candidate to friends; the row then offers the conversation instead. */
  protected async addFriend(match: Match): Promise<void> {
    const userId = match.userId;
    if (match.isFriend || this.isBusy(userId)) {
      return;
    }
    this.setBusy(userId, true);
    this.error.set(null);
    try {
      await firstValueFrom(this.friendsApi.add(userId));
      this.markFriend(userId);
    } catch (err) {
      if (err instanceof MoviesError && err.status === 409) {
        // Already a friend (added in another tab) — show the row as such, with the message.
        this.markFriend(userId);
        this.error.set(err.message);
      } else {
        this.showError(err);
      }
    } finally {
      this.setBusy(userId, false);
    }
  }

  private markFriend(userId: string): void {
    this.matches.update((list) => list.map((m) => (m.userId === userId ? { ...m, isFriend: true } : m)));
  }

  private setBusy(userId: string, busy: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (busy) {
        next.add(userId);
      } else {
        next.delete(userId);
      }
      return next;
    });
  }

  private startPolling(): void {
    this.pending.set(true);
    this.slow.set(false);
    this.pollsLeft = Math.ceil(STATUS_POLL_TIMEOUT_MS / STATUS_POLL_INTERVAL_MS);
    this.scheduleNextPoll();
  }

  private scheduleNextPoll(): void {
    this.pollTimer = setTimeout(() => void this.poll(), STATUS_POLL_INTERVAL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private async poll(): Promise<void> {
    this.pollTimer = null;
    this.pollsLeft--;
    try {
      const status = await firstValueFrom(this.matchesApi.status());
      if (this.destroyed) {
        return;
      }
      if (!status.profileComplete) {
        this.pending.set(false);
        this.profileIncomplete.set(true);
      } else if (!status.pending) {
        this.matches.set(await firstValueFrom(this.matchesApi.list()));
        this.pending.set(false);
      } else if (this.pollsLeft > 0) {
        this.scheduleNextPoll();
      } else {
        this.pending.set(false);
        this.slow.set(true);
      }
    } catch (err) {
      if (!this.destroyed) {
        this.pending.set(false);
        this.showError(err);
      }
    }
  }

  private showError(err: unknown): void {
    if (err instanceof MoviesError && err.status === 409) {
      this.profileIncomplete.set(true);
    } else {
      this.error.set(err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.');
    }
  }
}
