import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { AssetClass, AssetDetail, AssetListItem, AssetStatus, PagedResult } from './models';

export interface AssetQuery {
  assetClass?: AssetClass | '';
  status?: AssetStatus | '';
  search?: string;
  page: number;
  pageSize: number;
}

/** REST calls for data that is fetched on demand rather than pushed. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getAssets(q: AssetQuery) {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.assetClass) params = params.set('assetClass', q.assetClass);
    if (q.status) params = params.set('status', q.status);
    if (q.search?.trim()) params = params.set('search', q.search.trim());
    return this.http.get<PagedResult<AssetListItem>>('/api/assets', { params });
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
