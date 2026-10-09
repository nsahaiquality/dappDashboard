import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { FilterService } from './filter.service';
import { PortfolioEvent, PortfolioSnapshot, sameFilter } from './models';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

const MAX_EVENTS = 100;
const RETRY_MS = 5_000;

/**
 * Single SignalR connection to the backend DashboardHub.
 * The server pushes `Snapshot` (throttled aggregate for this connection's filter) and
 * `PortfolioEvent` (every change). The current global filter is sent with `SetFilter`
 * whenever it changes and after every (re)connect.
 */
@Injectable({ providedIn: 'root' })
export class DashboardHubService {
  private readonly filters = inject(FilterService);

  readonly snapshot = signal<PortfolioSnapshot | null>(null);
  readonly events = signal<PortfolioEvent[]>([]);
  readonly state = signal<ConnectionState>('disconnected');

  /** True while the filter changed and its snapshot has not arrived yet. */
  readonly pending = computed(() => {
    const s = this.snapshot();
    return !s || !sameFilter(s.filter, this.filters.filter());
  });

  private readonly connection: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/dashboard')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  constructor() {
    this.connection.on('Snapshot', (s: PortfolioSnapshot) => {
      // Ignore snapshots for a filter this client has already moved away from.
      if (sameFilter(s.filter, this.filters.filter())) this.snapshot.set(s);
    });
    this.connection.on('PortfolioEvent', (e: PortfolioEvent) => {
      this.events.update((list) => [e, ...list].slice(0, MAX_EVENTS));
    });
    this.connection.onreconnecting(() => this.state.set('reconnecting'));
    this.connection.onreconnected(() => {
      this.state.set('connected');
      this.sendFilter();
    });
    this.connection.onclose(() => {
      this.state.set('disconnected');
      setTimeout(() => this.start(), RETRY_MS);
    });

    effect(() => {
      this.filters.filter();
      this.sendFilter();
    });
  }

  async start(): Promise<void> {
    if (this.state() === 'connected' || this.state() === 'connecting') return;
    this.state.set('connecting');
    try {
      // The hub replays recent events on connect; start from a clean list.
      this.events.set([]);
      await this.connection.start();
      this.state.set('connected');
      this.sendFilter();
    } catch {
      this.state.set('disconnected');
      setTimeout(() => this.start(), RETRY_MS);
    }
  }

  private sendFilter() {
    if (this.connection.state !== HubConnectionState.Connected) return;
    this.connection.invoke('SetFilter', this.filters.filter()).catch(() => {});
  }
}
