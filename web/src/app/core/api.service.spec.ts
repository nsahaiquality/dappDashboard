import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService, AssetQuery } from './api.service';
import { NO_FILTER } from './models';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;
  const query: AssetQuery = { filter: NO_FILTER, sort: 'MarketValue', page: 1, pageSize: 15 };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filter fields that are set, with the names the API expects', () => {
    api
      .getAssets({
        ...query,
        filter: { ...NO_FILTER, assetClass: 'Healthcare', vendorId: 7, underwaterOnly: true },
      })
      .subscribe();
    const req = http.expectOne((r) => r.url === '/api/assets');
    expect(req.request.params.keys().sort()).toEqual([
      'assetClass',
      'page',
      'pageSize',
      'sort',
      'underwaterOnly',
      'vendorId',
    ]);
    expect(req.request.params.get('assetClass')).toBe('Healthcare');
    expect(req.request.params.get('underwaterOnly')).toBe('true');
    req.flush({ items: [], total: 0, page: 1, pageSize: 15 });
  });

  it('adds status and trimmed search, and builds the matching CSV export URL', () => {
    api.getAssets({ ...query, status: 'Sold', search: '  MRI ' }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/assets');
    expect(req.request.params.get('status')).toBe('Sold');
    expect(req.request.params.get('search')).toBe('MRI');
    req.flush({ items: [], total: 0, page: 1, pageSize: 15 });

    expect(api.assetsExportUrl({ ...query, filter: { ...NO_FILTER, country: 'NL' } })).toBe(
      '/api/assets/export?country=NL&sort=MarketValue',
    );
  });

  it('calls the simulator and detail endpoints', () => {
    api.setSimulator(false).subscribe();
    http.expectOne({ method: 'POST', url: '/api/simulator/pause' }).flush({ running: false });
    api.getAsset(42).subscribe();
    http.expectOne('/api/assets/42').flush(null);
  });
});
