import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DashboardHubService } from './core/dashboard-hub.service';
import { Icon } from './shared/icon';

interface NavItem {
  label: string;
  icon: string;
  link?: string;
}

/** Application shell: sidebar navigation (a top bar on narrow screens) around the routed page. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly hub = inject(DashboardHubService);

  protected readonly monitor: NavItem[] = [
    { label: 'Overview', icon: 'overview', link: '/' },
    { label: 'Assets', icon: 'assets', link: '/assets' },
    { label: 'Remarketing', icon: 'remarketing' },
    { label: 'Vendors', icon: 'vendors' },
    { label: 'Refinancing', icon: 'refinancing' },
  ];
  protected readonly data: NavItem[] = [
    { label: 'Sources', icon: 'sources' },
    { label: 'Reports', icon: 'reports' },
  ];

  ngOnInit() {
    this.hub.start();
  }
}
