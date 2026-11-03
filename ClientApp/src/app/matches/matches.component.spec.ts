import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { MatchesComponent } from './matches.component';
import { Match, MatchesService, PROFILE_INCOMPLETE } from './matches.service';

describe('MatchesComponent', () => {
  const match: Match = {
    userId: 'u-2',
    displayName: 'Ania',
    city: { id: 7, name: 'Kraków', voivodeship: 'małopolskie' },
    score: 87,
    sharedFavoritesCount: 1,
    sharedWatchlistCount: 0,
    sharedFavorites: [{ id: 1, title: 'Ojciec chrzestny', originalTitle: 'The Godfather', year: 1972 }],
    sharedWatchlist: [],
    firstMatchedAt: '2026-11-03T10:00:00Z',
  };

  async function render(api: Partial<MatchesService>) {
    TestBed.configureTestingModule({
      imports: [MatchesComponent],
      providers: [provideRouter([]), { provide: MatchesService, useValue: api }],
    });
    const fixture = TestBed.createComponent(MatchesComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('renders the stored list with name, city, score and shared titles', async () => {
    const fixture = await render({ list: () => of([match]) });
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Ania');
    expect(text).toContain('Kraków (małopolskie)');
    expect(text).toContain('87/100');
    expect(text).toContain('Ojciec chrzestny (1972)');
  });

  it('shows the profile message with a link to /profile when recompute returns 409', async () => {
    const fixture = await render({
      list: () => of([]),
      recompute: () => throwError(() => new MoviesError(PROFILE_INCOMPLETE, 409)),
    });
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain(PROFILE_INCOMPLETE);
    expect(alert?.querySelector('a')?.getAttribute('href')).toBe('/profile');
  });
});
