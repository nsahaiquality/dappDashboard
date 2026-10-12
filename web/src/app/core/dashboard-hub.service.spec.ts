import { TestBed } from '@angular/core/testing';
import { FakeHubConnection, apiStub, snapshot } from '../../testing/fakes';
import { ApiService } from './api.service';
import { DashboardHubService, HUB_CONNECTION_FACTORY } from './dashboard-hub.service';
import { FilterService } from './filter.service';
import { NO_FILTER, PortfolioEvent } from './models';

describe('DashboardHubService', () => {
  let fake: FakeHubConnection;
  let hub: DashboardHubService;
  let filters: FilterService;

  beforeEach(async () => {
    fake = new FakeHubConnection();
    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: apiStub() },
        { provide: HUB_CONNECTION_FACTORY, useValue: () => fake.asConnection() },
      ],
    });
    hub = TestBed.inject(DashboardHubService);
    filters = TestBed.inject(FilterService);
    await hub.start();
  });

  const setFilterCalls = () =>
    fake.invoked.filter((c) => c.method === 'SetFilter').map((c) => c.args[0]);

  it('connects and sends the current filter', () => {
    expect(hub.state()).toBe('connected');
    expect(setFilterCalls().at(-1)).toEqual(NO_FILTER);
  });

  it('sends the filter again whenever it changes', () => {
    filters.patch({ assetClass: 'Healthcare' });
    TestBed.tick();
    expect(setFilterCalls().at(-1)).toEqual({ ...NO_FILTER, assetClass: 'Healthcare' });
  });

  it('resends the filter after a reconnect (a new connection starts unfiltered on the server)', () => {
    filters.patch({ country: 'NL' });
    TestBed.tick();
    const before = setFilterCalls().length;
    fake.simulateReconnect();
    expect(setFilterCalls().length).toBe(before + 1);
    expect(setFilterCalls().at(-1)).toEqual({ ...NO_FILTER, country: 'NL' });
  });

  it('accepts a snapshot for the current filter and is no longer pending', () => {
    expect(hub.pending()).toBe(true);
    fake.emit('Snapshot', snapshot());
    expect(hub.snapshot()?.openContracts).toBe(1_200);
    expect(hub.pending()).toBe(false);
  });

  it('ignores a late snapshot for a filter the user has left', () => {
    filters.patch({ assetClass: 'Healthcare' });
    fake.emit('Snapshot', snapshot(NO_FILTER));
    expect(hub.snapshot()).toBeNull();
    expect(hub.pending()).toBe(true);

    fake.emit('Snapshot', snapshot({ ...NO_FILTER, assetClass: 'Healthcare' }));
    expect(hub.snapshot()?.filter.assetClass).toBe('Healthcare');
  });

  it('keeps the newest 100 events, newest first', () => {
    for (let i = 0; i < 120; i++) {
      const e: PortfolioEvent = {
        at: `2026-10-12T08:00:${String(i % 60).padStart(2, '0')}Z`,
        type: 'Revaluation',
        message: `event ${i}`,
      };
      fake.emit('PortfolioEvent', e);
    }
    expect(hub.events().length).toBe(100);
    expect(hub.events()[0].message).toBe('event 119');
    expect(hub.events().at(-1)!.message).toBe('event 20');
  });
});
