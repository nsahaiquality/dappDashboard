import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../core/api.service';
import { DashboardHubService } from '../core/dashboard-hub.service';
import { FilterService } from '../core/filter.service';
import { Icon } from './icon';

/** Page title with breadcrumb, live-feed status and the simulator toggle; page actions are projected. */
@Component({
  selector: 'app-page-header',
  imports: [DatePipe, Icon],
  template: `
    <header class="page-header">
      <div class="page-title">
        <span class="crumb">{{ section() }} · {{ filters.summary() }} · EUR</span>
        <h1>{{ title() }}</h1>
      </div>
      <div class="page-actions">
        <span class="live {{ liveClass() }}" role="status">
          <span class="dot" aria-hidden="true"></span>
          @switch (hub.state()) {
            @case ('connected') {
              @if (simulatorRunning() === false) { Paused } @else { Live }
              @if (hub.snapshot(); as s) { · updated {{ s.generatedAt | date: 'HH:mm:ss' }} }
            }
            @case ('reconnecting') { Reconnecting… }
            @case ('connecting') { Connecting… }
            @default { Offline }
          }
        </span>
        @if (simulatorRunning() !== null) {
          <button type="button" class="btn" (click)="toggleSimulator()">
            <app-icon [name]="simulatorRunning() ? 'pause' : 'play'" [size]="16" />
            {{ simulatorRunning() ? 'Pause feed' : 'Resume feed' }}
          </button>
        }
        <ng-content />
      </div>
    </header>
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly section = input('Portfolio');

  protected readonly hub = inject(DashboardHubService);
  protected readonly filters = inject(FilterService);
  private readonly api = inject(ApiService);

  protected readonly simulatorRunning = signal<boolean | null>(null);
  protected readonly liveClass = computed(() =>
    this.hub.state() !== 'connected' ? this.hub.state() : this.simulatorRunning() === false ? 'paused' : 'connected',
  );

  constructor() {
    this.api.getSimulator().subscribe({ next: (r) => this.simulatorRunning.set(r.running), error: () => {} });
  }

  protected toggleSimulator() {
    this.api.setSimulator(!this.simulatorRunning()).subscribe((r) => this.simulatorRunning.set(r.running));
  }
}
