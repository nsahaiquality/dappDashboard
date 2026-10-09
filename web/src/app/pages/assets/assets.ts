import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ChartConfiguration } from 'chart.js';
import { ApiService, AssetQuery } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { formatEur, formatEurFull, formatInt, formatPct, humanize } from '../../core/format';
import { ASSET_STATUSES, AssetDetail, AssetListItem, AssetSort, AssetStatus } from '../../core/models';
import { ChartComponent, baseOptions, chartTheme } from '../../shared/chart';
import { FilterBar } from '../../shared/filter-bar';
import { Icon } from '../../shared/icon';
import { PageHeader } from '../../shared/page-header';

const STATUS_TONE: Record<AssetStatus, string> = {
  InUse: 'neutral',
  Repossessed: 'serious',
  InRemarketing: 'warning',
  Sold: 'good',
  Returned: 'neutral',
};

/** Searchable, sortable asset list with a side panel showing one asset's valuation against its exposure. */
@Component({
  selector: 'app-assets',
  imports: [PageHeader, FilterBar, Icon, ChartComponent, DatePipe],
  templateUrl: './assets.html',
})
export class Assets {
  private readonly api = inject(ApiService);
  protected readonly filters = inject(FilterService);

  /** Initial sort from the URL (`/assets?sort=DaysPastDue`), e.g. when coming from the delinquency card. */
  readonly sortParam = input<AssetSort | undefined>(undefined, { alias: 'sort' });

  protected readonly statuses = ASSET_STATUSES;
  protected readonly statusTone = STATUS_TONE;
  protected readonly eur = formatEur;
  protected readonly eurFull = formatEurFull;
  protected readonly int = formatInt;
  protected readonly pct = formatPct;
  protected readonly humanize = humanize;

  protected readonly status = signal<AssetStatus | ''>('');
  protected readonly search = signal('');
  protected readonly sort = signal<AssetSort>('MarketValue');
  protected readonly page = signal(1);
  protected readonly pageSize = 15;

  protected readonly items = signal<AssetListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly selected = signal<AssetDetail | null>(null);
  protected readonly searchDraft = signal('');

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  protected readonly firstRow = computed(() => (this.total() ? (this.page() - 1) * this.pageSize + 1 : 0));
  protected readonly lastRow = computed(() => Math.min(this.total(), this.page() * this.pageSize));

  private readonly query = computed<AssetQuery>(() => ({
    filter: this.filters.filter(),
    status: this.status(),
    search: this.search(),
    sort: this.sort(),
    page: this.page(),
    pageSize: this.pageSize,
  }));

  protected readonly exportUrl = computed(() => this.api.assetsExportUrl(this.query()));

  /** Global filter chips plus this page's own status and search. */
  protected readonly chips = computed(() => {
    const chips = this.filters.chips().map((c) => ({ label: c.label, remove: () => this.filters.patch(c.clear) }));
    if (this.status()) chips.push({ label: `Status: ${humanize(this.status())}`, remove: () => this.setStatus('') });
    if (this.search()) chips.push({ label: `Search: “${this.search()}”`, remove: () => this.applySearch('') });
    return chips;
  });

  private searchTimer?: ReturnType<typeof setTimeout>;
  private requestId = 0;

  constructor() {
    effect(() => {
      const sort = this.sortParam();
      if (sort) untracked(() => this.sort.set(sort));
    });
    // Back to page 1 whenever the global filter changes.
    effect(() => {
      this.filters.filter();
      untracked(() => this.page.set(1));
    });
    effect(() => this.load(this.query()));
  }

  protected load(q: AssetQuery = this.query()) {
    const id = ++this.requestId;
    this.loading.set(true);
    this.api.getAssets(q).subscribe({
      next: (r) => {
        if (id !== this.requestId) return; // a newer request is in flight
        this.items.set(r.items);
        this.total.set(r.total);
        this.loading.set(false);
        if (!this.selected() && r.items.length) this.open(r.items[0]);
      },
      error: () => this.loading.set(false),
    });
  }

  protected setStatus(value: string) {
    this.status.set(value as AssetStatus | '');
    this.page.set(1);
  }

  protected onSearchInput(value: string) {
    this.searchDraft.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.applySearch(value), 300);
  }

  protected applySearch(value: string) {
    clearTimeout(this.searchTimer);
    this.searchDraft.set(value);
    this.search.set(value.trim());
    this.page.set(1);
  }

  protected setSort(sort: AssetSort) {
    this.sort.set(sort);
    this.page.set(1);
  }

  protected open(item: AssetListItem) {
    this.api.getAsset(item.id).subscribe((d) => this.selected.set(d));
  }

  protected showContract(contractNumber: string) {
    this.applySearch(contractNumber);
  }

  protected ariaSort(sort: AssetSort) {
    return this.sort() === sort ? 'descending' : null;
  }

  protected readonly detail = computed(() => {
    const d = this.selected();
    if (!d) return null;
    const cover = d.assetExposure ? d.asset.marketValue / d.assetExposure : null;
    const forcedCover = d.assetExposure ? d.asset.forcedSaleValue / d.assetExposure : null;
    return { ...d, cover, forcedCover };
  });

  protected readonly valuationChart = computed<ChartConfiguration | null>(() => {
    const d = this.selected();
    if (!d || d.valuations.length < 2) return null;
    const t = chartTheme();
    const options = baseOptions()!;
    options.interaction = { mode: 'index', intersect: false };
    options.plugins!.legend = { display: false };
    options.plugins!.tooltip = {
      ...options.plugins!.tooltip,
      callbacks: {
        label: (ctx) => `${ctx.dataset.label}: ${formatEurFull(ctx.parsed.y ?? 0)}`,
        footer: (items) => humanize(d.valuations[items[0].dataIndex].method),
      },
    };
    (options.scales!['y'] as any).ticks.callback = (v: number) => formatEur(v);
    (options.scales!['y'] as any).beginAtZero = true;
    (options.scales!['x'] as any).grid = { display: false };
    const line = { borderWidth: 2, pointRadius: 3, pointHoverRadius: 6, pointBorderColor: t.surface, pointBorderWidth: 2, tension: 0 };
    return {
      type: 'line',
      data: {
        labels: d.valuations.map((v) => new Date(v.valuedAt).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })),
        datasets: [
          { label: 'Market value', data: d.valuations.map((v) => v.marketValue), borderColor: t.series1, backgroundColor: t.series1, ...line },
          { label: 'Forced sale value', data: d.valuations.map((v) => v.forcedSaleValue), borderColor: t.series2, backgroundColor: t.series2, ...line },
          { label: 'Exposure (asset share)', data: d.valuations.map((v) => v.exposureShare), borderColor: t.inkSecondary, backgroundColor: t.inkSecondary,
            ...line, borderDash: [6, 4], pointRadius: 0 },
        ],
      },
      options,
    };
  });
}
