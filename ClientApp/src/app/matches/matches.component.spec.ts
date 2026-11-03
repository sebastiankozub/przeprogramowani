import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import {
  MatchesComponent,
  STATUS_POLL_INTERVAL_MS,
  STATUS_POLL_TIMEOUT_MS,
} from './matches.component';
import { Match, MatchesService, MatchStatus, PROFILE_INCOMPLETE } from './matches.service';

describe('MatchesComponent', () => {
  const match: Match = {
    userId: 'u-2',
    displayName: 'Ania',
    city: { id: 7, name: 'Kraków', voivodeship: 'małopolskie' },
    score: 87,
    sharedFavoritesCount: 1,
    sharedWatchlistCount: 0,
    sharedFavorites: [
      { id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 },
    ],
    sharedWatchlist: [],
    firstMatchedAt: '2026-11-03T10:00:00Z',
  };
  const upToDate: MatchStatus = { pending: false, profileComplete: true };
  const pending: MatchStatus = { pending: true, profileComplete: true };

  const PENDING_TEXT = 'Trwa aktualizacja dopasowań…';
  const SLOW_TEXT = 'Aktualizacja trwa dłużej niż zwykle — odśwież stronę za chwilę.';
  const EMPTY_TEXT =
    'Nie masz jeszcze dopasowań. Oceń filmy lub dodaj je do listy do obejrzenia — lista zaktualizuje się automatycznie.';

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  async function render(api: Partial<MatchesService>) {
    TestBed.configureTestingModule({
      imports: [MatchesComponent],
      providers: [provideRouter([]), { provide: MatchesService, useValue: api }],
    });
    const fixture = TestBed.createComponent(MatchesComponent);
    fixture.detectChanges();
    await settle(fixture);
    return fixture;
  }

  /**
   * Advances the fake clock by `ms`, lets pending promises resolve and re-renders. (whenStable is not used:
   * Angular's scheduler relies on timers, so it would never resolve under fake timers.)
   */
  async function settle(fixture: ComponentFixture<MatchesComponent>, ms = 0) {
    await vi.advanceTimersByTimeAsync(ms);
    fixture.detectChanges();
  }

  function text(fixture: ComponentFixture<MatchesComponent>): string {
    return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  }

  it('renders the stored list with name, city, score and shared titles', async () => {
    const fixture = await render({ status: () => of(upToDate), list: () => of([match]) });
    const content = text(fixture);

    expect(content).toContain('Ania');
    expect(content).toContain('Kraków (małopolskie)');
    expect(content).toContain('87/100');
    expect(content).toContain('Ojciec chrzestny (1972)');
    expect(content).not.toContain(PENDING_TEXT);
  });

  it('has no search button', async () => {
    const fixture = await render({ status: () => of(upToDate), list: () => of([match]) });

    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();
    expect(text(fixture)).not.toContain('Szukaj dopasowań');
  });

  it('shows the automatic-update empty state when the list is empty', async () => {
    const fixture = await render({ status: () => of(upToDate), list: () => of([]) });

    expect(text(fixture)).toContain(EMPTY_TEXT);
  });

  it('shows the profile message with a link to /profile and does not poll when the profile is incomplete', async () => {
    const status = vi.fn(() => of<MatchStatus>({ pending: false, profileComplete: false }));
    const fixture = await render({ status, list: () => of([]) });
    const el = fixture.nativeElement as HTMLElement;

    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain(PROFILE_INCOMPLETE);
    expect(alert?.querySelector('a')?.getAttribute('href')).toBe('/profile');
    expect(text(fixture)).not.toContain(PENDING_TEXT);
    expect(text(fixture)).not.toContain(EMPTY_TEXT);

    await settle(fixture, STATUS_POLL_TIMEOUT_MS);
    expect(status).toHaveBeenCalledTimes(1);
  });

  it('shows the indicator while pending, polls the status and reloads the list when it clears', async () => {
    const status = vi
      .fn()
      .mockReturnValueOnce(of(pending))
      .mockReturnValueOnce(of(pending))
      .mockReturnValue(of(upToDate));
    const list = vi
      .fn()
      .mockReturnValueOnce(of([]))
      .mockReturnValue(of([match]));
    const fixture = await render({ status, list });
    const el = fixture.nativeElement as HTMLElement;

    expect(text(fixture)).toContain(PENDING_TEXT);
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    expect(text(fixture)).not.toContain(EMPTY_TEXT);

    await settle(fixture, STATUS_POLL_INTERVAL_MS);
    expect(status).toHaveBeenCalledTimes(2);
    expect(list).toHaveBeenCalledTimes(1);
    expect(text(fixture)).toContain(PENDING_TEXT);

    await settle(fixture, STATUS_POLL_INTERVAL_MS);
    expect(status).toHaveBeenCalledTimes(3);
    expect(list).toHaveBeenCalledTimes(2);
    expect(text(fixture)).not.toContain(PENDING_TEXT);
    expect(el.querySelector('mat-progress-bar')).toBeNull();
    expect(text(fixture)).toContain('Ania');

    await settle(fixture, STATUS_POLL_TIMEOUT_MS);
    expect(status).toHaveBeenCalledTimes(3);
  });

  it('gives up polling after the timeout with a refresh hint', async () => {
    const status = vi.fn(() => of(pending));
    const fixture = await render({ status, list: () => of([]) });

    await settle(fixture, STATUS_POLL_TIMEOUT_MS);

    expect(text(fixture)).not.toContain(PENDING_TEXT);
    expect(text(fixture)).toContain(SLOW_TEXT);
    const calls = status.mock.calls.length;
    expect(calls).toBe(1 + STATUS_POLL_TIMEOUT_MS / STATUS_POLL_INTERVAL_MS);

    await settle(fixture, STATUS_POLL_TIMEOUT_MS);
    expect(status).toHaveBeenCalledTimes(calls);
  });

  it('stops polling when the component is destroyed', async () => {
    const status = vi.fn(() => of(pending));
    const fixture = await render({ status, list: () => of([]) });
    expect(status).toHaveBeenCalledTimes(1);

    fixture.destroy();
    await vi.advanceTimersByTimeAsync(STATUS_POLL_TIMEOUT_MS);

    expect(status).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('shows an error and stops the indicator when a status check fails', async () => {
    const status = vi
      .fn()
      .mockReturnValueOnce(of(pending))
      .mockReturnValue(
        throwError(() => new MoviesError('Nie udało się sprawdzić stanu dopasowań.', 500)),
      );
    const fixture = await render({ status, list: () => of([]) });

    await settle(fixture, STATUS_POLL_INTERVAL_MS);

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Nie udało się sprawdzić stanu dopasowań.');
    expect(text(fixture)).not.toContain(PENDING_TEXT);
  });
});
