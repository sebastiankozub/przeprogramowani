import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { MoviesError } from '../shared/api-errors';
import { ConversationComponent } from './conversation.component';
import { CONVERSATION_NOT_ALLOWED, Conversation, FriendsService, Message } from './friends.service';

describe('ConversationComponent', () => {
  const conversation: Conversation = {
    with: { userId: 'u-2', displayName: 'Ania', city: { id: 7, name: 'Kraków', voivodeship: 'małopolskie' } },
    messages: [
      { id: 1, fromMe: false, body: 'Hej, oglądamy coś?', sentAt: '2026-11-03T10:00:00Z' },
      { id: 2, fromMe: true, body: 'Jasne!', sentAt: '2026-11-03T10:05:00Z' },
    ],
  };

  async function render(api: Partial<FriendsService>) {
    TestBed.configureTestingModule({
      imports: [ConversationComponent],
      providers: [provideRouter([]), { provide: FriendsService, useValue: api }],
    });
    const fixture = TestBed.createComponent(ConversationComponent);
    fixture.componentRef.setInput('userId', 'u-2');
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: ComponentFixture<ConversationComponent>) {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function el(fixture: ComponentFixture<ConversationComponent>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function sendButton(fixture: ComponentFixture<ConversationComponent>): HTMLButtonElement | undefined {
    return Array.from(el(fixture).querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Wyślij');
  }

  function textarea(fixture: ComponentFixture<ConversationComponent>): HTMLTextAreaElement {
    return el(fixture).querySelector('textarea')!;
  }

  async function type(fixture: ComponentFixture<ConversationComponent>, value: string) {
    textarea(fixture).value = value;
    textarea(fixture).dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  it('shows the other person as heading and labels each message "Ty" or with their name', async () => {
    const conversationCall = vi.fn(() => of(conversation));
    const fixture = await render({ conversation: conversationCall });

    expect(conversationCall).toHaveBeenCalledWith('u-2');
    expect(el(fixture).querySelector('h1')?.textContent).toContain('Ania');
    const items = Array.from(el(fixture).querySelectorAll('ol > li'));
    expect(items).toHaveLength(2);
    expect(items[0].querySelector('strong')?.textContent?.trim()).toBe('Ania');
    expect(items[0].textContent).toContain('Hej, oglądamy coś?');
    expect(items[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-11-03T10:00:00Z');
    expect(items[1].querySelector('strong')?.textContent?.trim()).toBe('Ty');
  });

  it('has a "Wiadomość" textarea limited to 2000 characters', async () => {
    const fixture = await render({ conversation: () => of(conversation) });

    const label = el(fixture).querySelector('mat-label');
    expect(label?.textContent?.trim()).toBe('Wiadomość');
    expect(textarea(fixture).getAttribute('maxlength')).toBe('2000');
  });

  it('disables "Wyślij" while the text is blank', async () => {
    const fixture = await render({ conversation: () => of(conversation) });

    expect(sendButton(fixture)?.disabled).toBe(true);
    await type(fixture, '   ');
    expect(sendButton(fixture)?.disabled).toBe(true);
    await type(fixture, 'Cześć');
    expect(sendButton(fixture)?.disabled).toBe(false);
  });

  it('sends the trimmed text, disables while sending, appends the message and clears the field', async () => {
    const result = new Subject<Message>();
    const send = vi.fn(() => result.asObservable());
    const fixture = await render({ conversation: () => of(conversation), send });

    await type(fixture, '  Do zobaczenia!  ');
    sendButton(fixture)!.click();
    await settle(fixture);

    expect(send).toHaveBeenCalledWith('u-2', 'Do zobaczenia!');
    expect(sendButton(fixture)?.disabled).toBe(true);

    result.next({ id: 3, fromMe: true, body: 'Do zobaczenia!', sentAt: '2026-11-03T10:10:00Z' });
    result.complete();
    await settle(fixture);

    const items = Array.from(el(fixture).querySelectorAll('ol > li'));
    expect(items).toHaveLength(3);
    expect(items[2].textContent).toContain('Do zobaczenia!');
    expect(items[2].querySelector('strong')?.textContent?.trim()).toBe('Ty');
    expect(textarea(fixture).value).toBe('');
    expect(sendButton(fixture)?.disabled).toBe(true);
  });

  it('shows "Nie możesz pisać z tą osobą" instead of the form on a 404', async () => {
    const fixture = await render({
      conversation: () => throwError(() => new MoviesError(CONVERSATION_NOT_ALLOWED, 404)),
    });

    const alert = el(fixture).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Nie możesz pisać z tą osobą');
    expect(el(fixture).querySelector('form')).toBeNull();
    expect(el(fixture).querySelector('textarea')).toBeNull();
  });
});
