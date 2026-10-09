import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ChartConfiguration } from 'chart.js';
import { ApiService } from '../core/api.service';
import { formatEur, formatEurFull, formatInt, humanize } from '../core/format';
import { ASSET_CLASSES, ASSET_STATUSES, AssetClass, AssetDetail, AssetListItem, AssetStatus } from '../core/models';
import { ChartComponent, baseOptions, chartTheme } from '../shared/chart';

/** Searchable, paged list of assets with a detail panel showing valuation history. */
@Component({
  selector: 'app-asset-explorer',
  imports: [ChartComponent, DatePipe],
  templateUrl: './asset-explorer.html',
})
export class AssetExplorer {
  private readonly api = inject(ApiService);

  protected readonly classes = ASSET_CLASSES;
  protected readonly statuses = ASSET_STATUSES;
  protected readonly eur = formatEur;
  protected readonly eurFull = formatEurFull;
  protected readonly int = formatInt;
  protected readonly humanize = humanize;

  protected readonly assetClass = signal<AssetClass | ''>('');
  protected readonly status = signal<AssetStatus | ''>('');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = 25;

  protected readonly items = signal<AssetListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly selected = signal<AssetDetail | null>(null);

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => this.load());
  }

  protected load() {
    const query = {
      assetClass: this.assetClass(),
      status: this.status(),
      search: this.search(),
      page: this.page(),
      pageSize: this.pageSize,
    };
    this.loading.set(true);
    this.api.getAssets(query).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.total.set(r.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected setClass(value: string) {
    this.assetClass.set(value as AssetClass | '');
    this.page.set(1);
  }

  protected setStatus(value: string) {
    this.status.set(value as AssetStatus | '');
    this.page.set(1);
  }

  protected setSearch(value: string) {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
    }, 300);
  }

  protected open(item: AssetListItem) {
    this.api.getAsset(item.id).subscribe((d) => this.selected.set(d));
  }

  protected readonly valuationChart = computed<ChartConfiguration | null>(() => {
    const d = this.selected();
    if (!d) return null;
    const t = chartTheme();
    const options = baseOptions()!;
    options.interaction = { mode: 'index', intersect: false };
    options.plugins!.legend = { ...options.plugins!.legend, position: 'top', align: 'start' };
    options.plugins!.tooltip = {
      ...options.plugins!.tooltip,
      callbacks: {
        label: (ctx) => `${ctx.dataset.label}: ${formatEurFull(ctx.parsed.y ?? 0)}`,
        footer: (items) => humanize(d.valuations[items[0].dataIndex].method),
      },
    };
    (options.scales!['y'] as any).ticks.callback = (v: number) => formatEur(v);
    (options.scales!['y'] as any).beginAtZero = true;
    const line = { borderWidth: 2, pointRadius: 4, pointHoverRadius: 6, pointBorderColor: t.surface, pointBorderWidth: 2, tension: 0 };
    return {
      type: 'line',
      data: {
        labels: d.valuations.map((v) => new Date(v.valuedAt).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })),
        datasets: [
          { label: 'Market value', data: d.valuations.map((v) => v.marketValue), borderColor: t.series1, backgroundColor: t.series1, ...line },
          { label: 'Forced sale value', data: d.valuations.map((v) => v.forcedSaleValue), borderColor: t.series2, backgroundColor: t.series2, ...line },
        ],
      },
      options,
    };
  });
}
