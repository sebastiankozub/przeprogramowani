import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { City } from '../profile/profile.service';
import { mapErrors } from '../shared/api-errors';

/** A friend (or someone who friended me) — only public, city-level data. */
export interface Friend {
  userId: string;
  displayName: string;
  city: City;
}

export interface FriendsList {
  /** People I added, by name. */
  friends: Friend[];
  /** People who added me and whom I have not added, by name. */
  addedMe: Friend[];
}

export interface Message {
  id: number;
  /** Sent by the signed-in user. */
  fromMe: boolean;
  body: string;
  /** ISO timestamp. */
  sentAt: string;
}

export interface Conversation {
  with: Friend;
  /** The latest 100 messages, oldest first. */
  messages: Message[];
}

const FRIENDS_API = '/api/friends';
const CONVERSATIONS_API = '/api/conversations';

/** Mirrors the backend limit, applied after trimming. */
export const MESSAGE_MAX_LENGTH = 2000;
export const NOT_ON_MATCH_LIST = 'Tej osoby nie ma już na Twojej liście dopasowań.';
export const ALREADY_FRIEND = 'Ta osoba jest już na Twojej liście znajomych.';
/** The server answers 404 for anyone you are not allowed to talk to (no friendship in either direction). */
export const CONVERSATION_NOT_ALLOWED = 'Nie możesz pisać z tą osobą';
export const INVALID_MESSAGE = `Wiadomość musi mieć od 1 do ${MESSAGE_MAX_LENGTH} znaków.`;

/** Thin wrapper over friends and conversations; failures surface as MoviesError with a Polish message. */
@Injectable({ providedIn: 'root' })
export class FriendsService {
  private readonly http = inject(HttpClient);

  list(): Observable<FriendsList> {
    return this.http
      .get<FriendsList>(FRIENDS_API)
      .pipe(mapErrors({ fallback: 'Nie udało się wczytać listy znajomych.' }));
  }

  /** Only a user from the caller's match list can be added. */
  add(userId: string): Observable<Friend> {
    return this.http.post<Friend>(FRIENDS_API, { userId }).pipe(
      mapErrors({
        notFound: NOT_ON_MATCH_LIST,
        conflict: ALREADY_FRIEND,
        fallback: 'Nie udało się dodać do znajomych.',
      }),
    );
  }

  conversation(userId: string): Observable<Conversation> {
    return this.http.get<Conversation>(`${CONVERSATIONS_API}/${encodeURIComponent(userId)}`).pipe(
      mapErrors({
        notFound: CONVERSATION_NOT_ALLOWED,
        fallback: 'Nie udało się wczytać rozmowy.',
      }),
    );
  }

  /** The server trims the body and requires 1–2000 characters after trimming. */
  send(userId: string, body: string): Observable<Message> {
    return this.http.post<Message>(`${CONVERSATIONS_API}/${encodeURIComponent(userId)}/messages`, { body }).pipe(
      mapErrors({
        badRequest: INVALID_MESSAGE,
        notFound: CONVERSATION_NOT_ALLOWED,
        fallback: 'Nie udało się wysłać wiadomości.',
      }),
    );
  }
}
