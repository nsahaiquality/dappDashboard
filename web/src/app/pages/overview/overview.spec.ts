import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FakeHubConnection, apiStub, snapshot } from '../../../testing/fakes';
import { ApiService } from '../../core/api.service';
import { DashboardHubService, HUB_CONNECTION_FACTORY } from '../../core/dashboard-hub.service';
import { FilterService } from '../../core/filter.service';
import { Overview } from './overview';

describe('Overview page', () => {
  let fake: FakeHubConnection;

  async function render() {
    const fixture = TestBed.createComponent(Overview);
    await TestBed.inject(DashboardHubService).start();
    fake.emit('Snapshot', snapshot());
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    fake = new FakeHubConnection();
    TestBed.configureTestingModule({
      imports: [Overview],
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: apiStub() },
        { provide: HUB_CONNECTION_FACTORY, useValue: () => fake.asConnection() },
      ],
    });
  });

  it('shows the six headline figures from the snapshot', async () => {
    const el = await render();
    const values = [...el.querySelectorAll('.kpi-value')].map((v) => v.textContent!.trim());
    expect(values).toEqual(['€3.00B', '€4.00B', '75%', '€240M', '€700M', '60%']);
  });

  it('flags an underwater asset class and lists it on the refinancing watchlist', async () => {
    const el = await render();
    expect(el.querySelectorAll('button.class-row:not(.head)').length).toBe(2);
    expect(el.querySelector('button.class-row .pill.critical')!.textContent).toContain('110%');
    expect(el.querySelector('.watch-item')!.textContent).toContain('Healthcare');
  });

  it('filters the whole page when an asset class is clicked, and clears on a second click', async () => {
    const el = await render();
    const filters = TestBed.inject(FilterService);
    const healthcare = [...el.querySelectorAll<HTMLButtonElement>('button.class-row:not(.head)')].find((b) => b.textContent!.includes('Healthcare'))!;

    healthcare.click();
    expect(filters.filter().assetClass).toBe('Healthcare');
    healthcare.click();
    expect(filters.filter().assetClass).toBeNull();
  });

  it('shows revaluations as signed changes and other events as amounts', async () => {
    const el = await render();
    fake.emit('PortfolioEvent', { at: '2026-10-12T08:00:00Z', type: 'Revaluation', message: 'Tractor revalued', amount: -1234 });
    fake.emit('PortfolioEvent', { at: '2026-10-12T08:00:01Z', type: 'Sold', message: 'Tractor sold', amount: 80_000 });
    fake.emit('PortfolioEvent', { at: '2026-10-12T08:00:02Z', type: 'DataLoadFailed', message: 'Feed failed', amount: null });
    TestBed.tick();

    const rows = [...el.querySelectorAll('ol.events li')].map((li) => li.textContent!.replace(/\s+/g, ' '));
    expect(rows[0]).toContain('Load failed');
    expect(rows[1]).toContain('€80,000');
    expect(rows[2]).toContain('−€1,234');
  });
});
