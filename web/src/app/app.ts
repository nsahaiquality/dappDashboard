import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { AssetExplorer } from './assets/asset-explorer';
import { ApiService } from './core/api.service';
import { DashboardHubService } from './core/dashboard-hub.service';
import { Dashboard } from './dashboard/dashboard';

@Component({
  selector: 'app-root',
  imports: [Dashboard, AssetExplorer, DatePipe],
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly hub = inject(DashboardHubService);
  private readonly api = inject(ApiService);

  protected readonly tab = signal<'overview' | 'assets'>('overview');
  protected readonly simulatorRunning = signal<boolean | null>(null);

  ngOnInit() {
    this.hub.start();
    this.api.getSimulator().subscribe({ next: (r) => this.simulatorRunning.set(r.running), error: () => {} });
  }

  protected toggleSimulator() {
    this.api.setSimulator(!this.simulatorRunning()).subscribe((r) => this.simulatorRunning.set(r.running));
  }
}
