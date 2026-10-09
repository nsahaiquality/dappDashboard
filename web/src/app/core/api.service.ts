import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { AssetDetail, AssetListItem, AssetSort, AssetStatus, PagedResult, PortfolioFilter, ReferenceData } from './models';

export interface AssetQuery {
  filter: PortfolioFilter;
  status?: AssetStatus | '';
  search?: string;
  sort: AssetSort;
  page: number;
  pageSize: number;
}

/** REST calls for data that is fetched on demand rather than pushed. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getReference() {
    return this.http.get<ReferenceData>('/api/reference');
  }

  getAssets(q: AssetQuery) {
    const params = assetParams(q).set('page', q.page).set('pageSize', q.pageSize);
    return this.http.get<PagedResult<AssetListItem>>('/api/assets', { params });
  }

  /** URL of the CSV export for the same query (opened as a download). */
  assetsExportUrl(q: AssetQuery) {
    return `/api/assets/export?${assetParams(q).toString()}`;
  }

  getAsset(id: number) {
    return this.http.get<AssetDetail>(`/api/assets/${id}`);
  }

  getSimulator() {
    return this.http.get<{ running: boolean }>('/api/simulator');
  }

  setSimulator(running: boolean) {
    return this.http.post<{ running: boolean }>(`/api/simulator/${running ? 'resume' : 'pause'}`, {});
  }
}

export function filterParams(f: PortfolioFilter, params = new HttpParams()) {
  if (f.assetClass) params = params.set('assetClass', f.assetClass);
  if (f.country) params = params.set('country', f.country);
  if (f.vendorId) params = params.set('vendorId', f.vendorId);
  if (f.productType) params = params.set('productType', f.productType);
  if (f.underwaterOnly) params = params.set('underwaterOnly', true);
  return params;
}

function assetParams(q: AssetQuery) {
  let params = filterParams(q.filter).set('sort', q.sort);
  if (q.status) params = params.set('status', q.status);
  if (q.search?.trim()) params = params.set('search', q.search.trim());
  return params;
}
