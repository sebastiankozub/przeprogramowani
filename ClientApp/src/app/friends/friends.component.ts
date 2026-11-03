import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { cityLabel } from '../profile/profile.service';
import { MoviesError } from '../shared/api-errors';
import { FriendsList, FriendsService } from './friends.service';

/** The signed-in user's friends, plus the people who added them, each with a link to the conversation. */
@Component({
  selector: 'app-friends',
  imports: [MatButtonModule, RouterLink],
  templateUrl: './friends.component.html',
})
export class FriendsComponent implements OnInit {
  private readonly friendsApi = inject(FriendsService);

  protected readonly lists = signal<FriendsList>({ friends: [], addedMe: [] });
  protected readonly loaded = signal(false);
  /** Inline message for the failed load. */
  protected readonly error = signal<string | null>(null);

  protected readonly cityLabel = cityLabel;

  async ngOnInit(): Promise<void> {
    try {
      this.lists.set(await firstValueFrom(this.friendsApi.list()));
    } catch (err) {
      this.error.set(err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected isEmpty(): boolean {
    const { friends, addedMe } = this.lists();
    return friends.length === 0 && addedMe.length === 0;
  }
}
