import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { FriendsComponent } from './friends.component';
import { Friend, FriendsList, FriendsService } from './friends.service';

describe('FriendsComponent', () => {
  const city = { id: 7, name: 'Kraków', voivodeship: 'małopolskie' };
  const ania: Friend = { userId: 'u-2', displayName: 'Ania', city };
  const bartek: Friend = { userId: 'u-3', displayName: 'Bartek', city };

  async function render(lists: FriendsList) {
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [provideRouter([]), { provide: FriendsService, useValue: { list: () => of(lists) } }],
    });
    const fixture = TestBed.createComponent(FriendsComponent);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function text(el: HTMLElement): string {
    return (el.textContent ?? '').replace(/\s+/g, ' ');
  }

  function writeLinks(el: HTMLElement): string[] {
    return Array.from(el.querySelectorAll('a'))
      .filter((a) => a.textContent?.trim() === 'Napisz')
      .map((a) => a.getAttribute('href') ?? '');
  }

  it('shows friends and people who added me, each with name, city and a "Napisz" link', async () => {
    const el = await render({ friends: [ania], addedMe: [bartek] });

    expect(el.querySelector('h1')?.textContent).toContain('Znajomi');
    expect(el.querySelector('h2')?.textContent).toContain('Dodali Cię do znajomych');
    const [friendsList, addedMeList] = Array.from(el.querySelectorAll('ul'));
    expect(text(friendsList)).toContain('Ania');
    expect(text(friendsList)).toContain('Kraków (małopolskie)');
    expect(text(addedMeList)).toContain('Bartek');
    expect(writeLinks(el)).toEqual(['/friends/u-2', '/friends/u-3']);
  });

  it('hides the "Dodali Cię do znajomych" section when nobody added me', async () => {
    const el = await render({ friends: [ania], addedMe: [] });

    expect(text(el)).not.toContain('Dodali Cię do znajomych');
    expect(writeLinks(el)).toEqual(['/friends/u-2']);
  });

  it('shows the empty state pointing to /matches when both lists are empty', async () => {
    const el = await render({ friends: [], addedMe: [] });

    expect(text(el)).toContain('Nie masz jeszcze znajomych');
    expect(el.querySelector('a[href="/matches"]')).not.toBeNull();
    expect(el.querySelector('ul')).toBeNull();
  });
});
