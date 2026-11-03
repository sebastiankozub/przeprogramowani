import { Component } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

/**
 * Shown while a page waits for its first data. A cold production start (Fly machine + Neon wake-up) can take
 * several seconds, and an empty page reads as a bug — this says "still loading" instead.
 */
@Component({
  selector: 'app-loading',
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="page-loading" role="status">
      <mat-progress-spinner mode="indeterminate" diameter="28" aria-hidden="true" />
      <span>Ładowanie…</span>
    </div>
  `,
})
export class LoadingComponent {}
