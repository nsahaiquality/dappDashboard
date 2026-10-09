import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DashboardHubService } from '../core/dashboard-hub.service';
import { PortfolioEventType } from '../core/models';

/** Status styling: colour is always paired with an icon and a text label. */
const EVENT_STYLE: Record<PortfolioEventType, { label: string; icon: string; status: string }> = {
  Revaluation: { label: 'Revaluation', icon: '↻', status: 'neutral' },
  PaymentReceived: { label: 'Payment', icon: '✓', status: 'good' },
  PaymentMissed: { label: 'Missed payment', icon: '!', status: 'warning' },
  Default: { label: 'Default', icon: '✕', status: 'critical' },
  Repossession: { label: 'Repossession', icon: '⇤', status: 'serious' },
  Listed: { label: 'Listed', icon: '⌂', status: 'neutral' },
  Sold: { label: 'Sold', icon: '€', status: 'good' },
  MarketShock: { label: 'Market move', icon: '≈', status: 'serious' },
};

@Component({
  selector: 'app-event-feed',
  imports: [DatePipe],
  template: `
    <section class="card feed" aria-live="polite" aria-label="Live portfolio events">
      <h2>Live events</h2>
      <ol>
        @for (e of hub.events(); track e) {
          <li>
            <time>{{ e.at | date: 'HH:mm:ss' }}</time>
            <span class="status {{ style[e.type].status }}">{{ style[e.type].icon }} {{ style[e.type].label }}</span>
            <span class="msg">{{ e.message }}</span>
          </li>
        } @empty {
          <li class="muted">No events yet.</li>
        }
      </ol>
    </section>
  `,
})
export class EventFeed {
  protected readonly hub = inject(DashboardHubService);
  protected readonly style = EVENT_STYLE;
}
