import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { CONVERSATION_NOT_ALLOWED, Friend, FriendsService, MESSAGE_MAX_LENGTH, Message } from './friends.service';

/** The stored conversation with one friend (latest 100 messages) and a form to send the next one. */
@Component({
  selector: 'app-conversation',
  imports: [DatePipe, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, RouterLink],
  templateUrl: './conversation.component.html',
})
export class ConversationComponent {
  private readonly friendsApi = inject(FriendsService);

  /** Route param, bound through withComponentInputBinding(). */
  readonly userId = input.required<string>();

  protected readonly other = signal<Friend | null>(null);
  protected readonly messages = signal<Message[]>([]);
  protected readonly loaded = signal(false);
  protected readonly sending = signal(false);
  /** The server refused the conversation (404) — no friendship in either direction. */
  protected readonly notAllowed = signal(false);
  protected readonly notAllowedMessage = CONVERSATION_NOT_ALLOWED;
  /** Inline message for the last failed operation (other than "not allowed"). */
  protected readonly error = signal<string | null>(null);

  protected readonly maxLength = MESSAGE_MAX_LENGTH;
  protected readonly bodyControl = new FormControl('', { nonNullable: true });
  private readonly bodyValue = toSignal(this.bodyControl.valueChanges, { initialValue: '' });

  constructor() {
    // Reload whenever the route param changes: the router reuses this component for /friends/A -> /friends/B.
    effect(() => {
      const userId = this.userId();
      untracked(() => void this.load(userId));
    });
  }

  private async load(userId: string): Promise<void> {
    this.other.set(null);
    this.messages.set([]);
    this.notAllowed.set(false);
    this.error.set(null);
    this.loaded.set(false);
    try {
      const conversation = await firstValueFrom(this.friendsApi.conversation(userId));
      if (userId !== this.userId()) {
        return; // a newer navigation owns the page now
      }
      this.other.set(conversation.with);
      this.messages.set(conversation.messages);
    } catch (err) {
      if (userId === this.userId()) {
        this.handleError(err);
      }
    } finally {
      if (userId === this.userId()) {
        this.loaded.set(true);
      }
    }
  }

  protected canSend(): boolean {
    const body = this.bodyValue().trim();
    return !this.sending() && body.length > 0 && body.length <= MESSAGE_MAX_LENGTH;
  }

  protected async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    try {
      const message = await firstValueFrom(this.friendsApi.send(this.userId(), this.bodyValue().trim()));
      this.messages.update((list) => [...list, message]);
      this.bodyControl.setValue('');
    } catch (err) {
      this.handleError(err);
    } finally {
      this.sending.set(false);
    }
  }

  private handleError(err: unknown): void {
    if (err instanceof MoviesError && err.status === 404) {
      this.notAllowed.set(true);
    } else {
      this.error.set(err instanceof MoviesError ? err.message : 'Wystąpił nieoczekiwany błąd.');
    }
  }
}
