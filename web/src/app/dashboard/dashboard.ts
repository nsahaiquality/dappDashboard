import { Component, computed, inject } from '@angular/core';
import { ChartConfiguration } from 'chart.js';
import { DashboardHubService } from '../core/dashboard-hub.service';
import { formatEur, formatEurFull, formatInt, formatPct, humanize } from '../core/format';
import { PortfolioSnapshot } from '../core/models';
import { ChartComponent, baseOptions, chartTheme } from '../shared/chart';
import { EventFeed } from './event-feed';

/** Portfolio overview: headline KPIs, breakdown charts, top vendors, recovery stats, live feed. */
@Component({
  selector: 'app-dashboard',
  imports: [ChartComponent, EventFeed],
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly hub = inject(DashboardHubService);

  protected readonly s = this.hub.snapshot;
  protected readonly eur = formatEur;
  protected readonly eurFull = formatEurFull;
  protected readonly pct = formatPct;
  protected readonly int = formatInt;

  protected readonly byClassChart = computed(() => {
    const s = this.s();
    return s ? exposureByClassChart(s) : null;
  });

  protected readonly delinquencyChart = computed(() => {
    const s = this.s();
    if (!s) return null;
    const t = chartTheme();
    const options = baseOptions()!;
    options.plugins!.legend = { display: false };
    options.plugins!.tooltip = {
      ...options.plugins!.tooltip,
      callbacks: {
        label: (ctx) => {
          const b = s.delinquency[ctx.dataIndex];
          return `${formatEurFull(b.exposure)} · ${formatInt(b.contracts)} contracts`;
        },
      },
    };
    (options.scales!['y'] as any).ticks.callback = (v: number) => formatEur(v);
    return {
      type: 'bar',
      data: {
        labels: s.delinquency.map((d) => d.bucket),
        datasets: [{ label: 'Exposure', data: s.delinquency.map((d) => d.exposure), backgroundColor: t.series1, borderRadius: 4, borderSkipped: 'start', maxBarThickness: 48 }],
      },
      options,
    } satisfies ChartConfiguration as ChartConfiguration;
  });

  protected readonly countryChart = computed(() => {
    const s = this.s();
    if (!s) return null;
    const t = chartTheme();
    const options = baseOptions()!;
    (options as any).indexAxis = 'y';
    options.plugins!.legend = { display: false };
    options.plugins!.tooltip = {
      ...options.plugins!.tooltip,
      callbacks: {
        label: (ctx) => {
          const c = s.byCountry[ctx.dataIndex];
          return `${formatEurFull(c.marketValue)} · ${formatInt(c.assets)} assets`;
        },
      },
    };
    (options.scales!['x'] as any).ticks.callback = (v: number) => formatEur(v);
    (options.scales!['y'] as any).grid = { display: false };
    return {
      type: 'bar',
      data: {
        labels: s.byCountry.map((c) => c.country),
        datasets: [{ label: 'Collateral market value', data: s.byCountry.map((c) => c.marketValue), backgroundColor: t.series1, borderRadius: 4, borderSkipped: 'start', maxBarThickness: 22 }],
      },
      options,
    } satisfies ChartConfiguration as ChartConfiguration;
  });

  protected readonly shortfallShare = computed(() => {
    const s = this.s();
    return s && s.totalExposure ? s.forcedSaleShortfall / s.totalExposure : 0;
  });

  protected readonly atRiskShare = computed(() => {
    const s = this.s();
    return s && s.totalExposure ? s.exposureAtRisk / s.totalExposure : 0;
  });

  protected readonly humanize = humanize;
}

/** Exposure and collateral market value side by side per asset class (same unit, one axis). */
function exposureByClassChart(s: PortfolioSnapshot): ChartConfiguration {
  const t = chartTheme();
  const options = baseOptions()!;
  (options as any).indexAxis = 'y';
  options.interaction = { mode: 'index', axis: 'y', intersect: false };
  options.plugins!.legend = { ...options.plugins!.legend, position: 'top', align: 'start' };
  options.plugins!.tooltip = {
    ...options.plugins!.tooltip,
    callbacks: {
      label: (ctx) => `${ctx.dataset.label}: ${formatEurFull(ctx.parsed.x ?? 0)}`,
      footer: (items) => `LTV ${formatPct(s.byAssetClass[items[0].dataIndex].loanToValue)}`,
    },
  };
  (options.scales!['x'] as any).ticks.callback = (v: number) => formatEur(v);
  (options.scales!['y'] as any).grid = { display: false };
  const bar = { borderRadius: 4, borderSkipped: 'start' as const, borderColor: t.surface, borderWidth: { right: 2 } as any, maxBarThickness: 16 };
  return {
    type: 'bar',
    data: {
      labels: s.byAssetClass.map((c) => humanize(c.assetClass)),
      datasets: [
        { label: 'Exposure', data: s.byAssetClass.map((c) => c.exposure), backgroundColor: t.series1, ...bar },
        { label: 'Collateral market value', data: s.byAssetClass.map((c) => c.marketValue), backgroundColor: t.series2, ...bar },
      ],
    },
    options,
  };
}
