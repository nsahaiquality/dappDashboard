import { TestBed } from '@angular/core/testing';
import { apiStub } from '../../testing/fakes';
import { ApiService } from './api.service';
import { FilterService } from './filter.service';
import { NO_FILTER } from './models';

describe('FilterService', () => {
  let filters: FilterService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: apiStub() }] });
    filters = TestBed.inject(FilterService);
  });

  it('starts unfiltered and loads reference data', () => {
    expect(filters.filter()).toEqual(NO_FILTER);
    expect(filters.active()).toBe(false);
    expect(filters.summary()).toBe('All contracts');
    expect(filters.reference().countries).toEqual(['DE', 'NL']);
  });

  it('patch changes only the given fields', () => {
    filters.patch({ country: 'NL' });
    filters.patch({ underwaterOnly: true });
    expect(filters.filter()).toEqual({ ...NO_FILTER, country: 'NL', underwaterOnly: true });
    expect(filters.active()).toBe(true);
  });

  it('toggle sets a value, and clears it when clicked again (drill-down)', () => {
    filters.toggle('assetClass', 'Healthcare');
    expect(filters.filter().assetClass).toBe('Healthcare');
    filters.toggle('assetClass', 'Healthcare');
    expect(filters.filter().assetClass).toBeNull();
  });

  it('describes the active filter as chips with vendor names, and resets', () => {
    filters.patch({ assetClass: 'MaterialHandling', vendorId: 7, underwaterOnly: true });
    expect(filters.chips().map((c) => c.label)).toEqual(['Underwater contracts', 'Material handling', 'Vendor: Example Agri Dealers B.V.']);
    expect(filters.summary()).toBe('Underwater contracts · Material handling · Vendor: Example Agri Dealers B.V.');

    filters.patch(filters.chips()[1].clear);
    expect(filters.filter().assetClass).toBeNull();

    filters.reset();
    expect(filters.filter()).toEqual(NO_FILTER);
  });
});
