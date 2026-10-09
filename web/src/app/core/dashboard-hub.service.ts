import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { PortfolioEvent, PortfolioSnapshot } from './models';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

const MAX_EVENTS = 100;
const RETRY_MS = 5_000;

/**
 * Single SignalR connection to the backend DashboardHub.
 * The server pushes `Snapshot` (throttled aggregate) and `PortfolioEvent` (each change);
 * both are exposed as signals so components re-render automatically.
 */
@Injectable({ providedIn: 'root' })
export class DashboardHubService {
  readonly snapshot = signal<PortfolioSnapshot | null>(null);
  readonly events = signal<PortfolioEvent[]>([]);
  readonly state = signal<ConnectionState>('disconnected');

  private readonly connection: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/dashboard')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  constructor() {
    this.connection.on('Snapshot', (s: PortfolioSnapshot) => this.snapshot.set(s));
    this.connection.on('PortfolioEvent', (e: PortfolioEvent) =>
      this.events.update((list) => [e, ...list].slice(0, MAX_EVENTS)),
    );
    this.connection.onreconnecting(() => this.state.set('reconnecting'));
    this.connection.onreconnected(() => this.state.set('connected'));
    this.connection.onclose(() => {
      this.state.set('disconnected');
      setTimeout(() => this.start(), RETRY_MS);
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
    } catch {
      this.state.set('disconnected');
      setTimeout(() => this.start(), RETRY_MS);
    }
  }
}
