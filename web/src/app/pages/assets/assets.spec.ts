import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { FakeHubConnection, apiStub, asset, page } from '../../../testing/fakes';
import { ApiService, AssetQuery } from '../../core/api.service';
import { HUB_CONNECTION_FACTORY } from '../../core/dashboard-hub.service';
import { FilterService } from '../../core/filter.service';
import { AssetListItem, PagedResult } from '../../core/models';
import { Assets } from './assets';

describe('Assets page', () => {
  let requests: { query: AssetQuery; reply: Subject<PagedResult<AssetListItem>> }[];

  beforeEach(() => {
    requests = [];
    const api = {
      ...apiStub(),
      getAssets: (query: AssetQuery) => {
        const reply = new Subject<PagedResult<AssetListItem>>();
        requests.push({ query, reply });
        return reply;
      },
      getAsset: () => of(null),
    };
    TestBed.configureTestingModule({
      imports: [Assets],
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: api },
        { provide: HUB_CONNECTION_FACTORY, useValue: () => new FakeHubConnection().asConnection() },
      ],
    });
  });

  function create() {
    const fixture = TestBed.createComponent(Assets);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  it('loads the first page sorted by market value', () => {
    create();
    expect(requests.length).toBe(1);
    expect(requests[0].query).toMatchObject({ sort: 'MarketValue', page: 1, pageSize: 15 });
  });

  it('ignores a slow response that arrives after a newer one', () => {
    const fixture = create();
    TestBed.inject(FilterService).patch({ country: 'NL' });
    TestBed.tick();
    expect(requests.length).toBe(2);

    requests[1].reply.next(page([asset(2, { country: 'NL' })], 1));
    requests[0].reply.next(page([asset(1), asset(3)], 2)); // stale: the unfiltered request
    fixture.detectChanges();

    const serials = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.clickable .mono'),
    ].map((e) => e.textContent!.trim());
    expect(serials).toEqual(['TST-2024-0000002']);
  });

  it('shows global filters, status and search as removable chips', () => {
    const fixture = create();
    TestBed.inject(FilterService).patch({ assetClass: 'Healthcare' });
    const component = fixture.componentInstance as unknown as {
      setStatus(s: string): void;
      applySearch(s: string): void;
    };
    component.setStatus('Sold');
    component.applySearch('MRI');
    TestBed.tick();
    fixture.detectChanges();

    const chips = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('.chip.removable'),
    ].map((c) => c.textContent!.trim());
    expect(chips).toEqual(['Healthcare', 'Status: Sold', 'Search: “MRI”']);
    expect(requests.at(-1)!.query).toMatchObject({ status: 'Sold', search: 'MRI', page: 1 });
  });

  it('changing the sort goes back to page 1', () => {
    const fixture = create();
    const component = fixture.componentInstance as unknown as {
      page: { set(n: number): void };
      setSort(s: string): void;
    };
    component.page.set(3);
    TestBed.tick();
    component.setSort('Ltv');
    TestBed.tick();
    expect(requests.at(-1)!.query).toMatchObject({ sort: 'Ltv', page: 1 });
  });
});
