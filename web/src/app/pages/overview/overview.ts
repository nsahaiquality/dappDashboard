import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DashboardHubService } from '../../core/dashboard-hub.service';
import { FilterService } from '../../core/filter.service';
import { formatEur, formatEurFull, formatEurKpi, formatInt, formatPct, humanize } from '../../core/format';
import { AssetClass, PortfolioEventType } from '../../core/models';
import { FilterBar } from '../../shared/filter-bar';
import { Icon } from '../../shared/icon';
import { PageHeader } from '../../shared/page-header';

type EventCategory = 'all' | 'credit' | 'valuation' | 'remarketing';

const EVENT_CATEGORY: Record<PortfolioEventType, Exclude<EventCategory, 'all'>> = {
  PaymentReceived: 'credit',
  PaymentMissed: 'credit',
  Default: 'credit',
  Revaluation: 'valuation',
  MarketShock: 'valuation',
  Repossession: 'remarketing',
  Listed: 'remarketing',
  Sold: 'remarketing',
  RefinancingRequested: 'credit',
  RefinancingApproved: 'credit',
  RefinancingDeclined: 'credit',
  AuctionCompleted: 'remarketing',
  PriceReduced: 'remarketing',
};

/** Status styling: colour always paired with a text label. */
const EVENT_STYLE: Record<PortfolioEventType, { label: string; tone: string }> = {
  Revaluation: { label: 'Revaluation', tone: 'neutral' },
  MarketShock: { label: 'Market move', tone: 'serious' },
  PaymentReceived: { label: 'Payment', tone: 'good' },
  PaymentMissed: { label: 'Missed payment', tone: 'warning' },
  Default: { label: 'Default', tone: 'critical' },
  Repossession: { label: 'Repossession', tone: 'serious' },
  Listed: { label: 'Listed', tone: 'neutral' },
  Sold: { label: 'Sold', tone: 'good' },
  RefinancingRequested: { label: 'Refi request', tone: 'neutral' },
  RefinancingApproved: { label: 'Refi approved', tone: 'good' },
  RefinancingDeclined: { label: 'Refi declined', tone: 'warning' },
  AuctionCompleted: { label: 'Auction', tone: 'good' },
  PriceReduced: { label: 'Price cut', tone: 'neutral' },
};

/** Event types added on the server later still render, with a neutral label. */
const styleFor = (type: PortfolioEventType) => EVENT_STYLE[type] ?? { label: type, tone: 'neutral' };

/** Why a class tends to be underwater; shown on the refinancing watchlist. */
const UNDERWATER_REASON: Partial<Record<AssetClass, string>> = {
  Healthcare: 'Imaging equipment loses 15–17% a year; recovery leans on vendor buy-back.',
  Technology: 'Fast depreciation (about 30% a year); short terms keep the gap small.',
  Transportation: 'Used-truck prices are cyclical; forced sales come in well below market.',
  Construction: 'Heavy equipment values follow construction activity.',
  Agriculture: 'Values follow commodity cycles and seasonal demand.',
  MaterialHandling: 'Fleet assets with high usage wear.',
  CleanTech: 'Thin secondary market; forced-sale haircut is large.',
};

const BUCKET_TONES = ['current', 'b1', 'b2', 'b3', 'b4'];

interface Segment {
  pct: number;
  tone: string;
}

@Component({
  selector: 'app-overview',
  imports: [PageHeader, FilterBar, Icon, RouterLink, DatePipe],
  templateUrl: './overview.html',
})
export class Overview {
  protected readonly hub = inject(DashboardHubService);
  protected readonly filters = inject(FilterService);

  protected readonly eur = formatEur;
  protected readonly eurFull = formatEurFull;
  protected readonly int = formatInt;
  protected readonly pct = formatPct;
  protected readonly humanize = humanize;

  protected readonly s = this.hub.snapshot;

  protected readonly kpis = computed(() => {
    const s = this.s();
    if (!s) return [];
    const share = (v: number) => (s.totalExposure ? v / s.totalExposure : 0);
    const current = s.delinquency[0]?.exposure ?? 0;
    const bar = (...segments: Segment[]) => segments.map((x) => ({ ...x, pct: Math.max(0, Math.min(100, x.pct * 100)) }));
    return [
      { label: 'Total exposure', value: formatEurKpi(s.totalExposure), sub: `${formatPct(share(s.totalExposure - current))} past due`,
        bar: bar({ pct: share(current), tone: 'exposure' }, { pct: share(s.totalExposure - current), tone: 'warning' }) },
      { label: 'Collateral market value', value: formatEurKpi(s.totalMarketValue), sub: `Forced sale ${formatEurKpi(s.totalForcedSaleValue)}`,
        bar: bar({ pct: s.totalMarketValue ? s.totalForcedSaleValue / s.totalMarketValue : 0, tone: 'collateral' }) },
      { label: 'Loan-to-value', value: formatPct(s.loanToValue), sub: 'Exposure ÷ collateral value',
        bar: bar({ pct: s.loanToValue, tone: s.loanToValue > 1 ? 'critical' : 'accent' }) },
      { label: '30+ days past due', value: formatEurKpi(s.exposureAtRisk), sub: `${formatPct(share(s.exposureAtRisk))} of exposure`,
        bar: bar({ pct: share(s.exposureAtRisk), tone: 'warning' }) },
      { label: 'Shortfall at forced sale', value: formatEurKpi(s.forcedSaleShortfall), sub: `${formatPct(share(s.forcedSaleShortfall))} of exposure not covered`,
        bar: bar({ pct: share(s.forcedSaleShortfall), tone: 'critical' }) },
      { label: 'Recovery rate', value: formatPct(s.remarketing.recoveryRate), sub: 'Net of costs, sold assets',
        bar: bar({ pct: s.remarketing.recoveryRate, tone: 'good' }) },
    ];
  });

  protected readonly classRows = computed(() => {
    const s = this.s();
    if (!s) return [];
    const max = Math.max(1, ...s.byAssetClass.flatMap((c) => [c.exposure, c.marketValue]));
    return s.byAssetClass.map((c) => ({
      ...c,
      name: humanize(c.assetClass),
      expPct: (c.exposure / max) * 100,
      mktPct: (c.marketValue / max) * 100,
      underwater: c.loanToValue > 1,
      selected: this.filters.filter().assetClass === c.assetClass,
    }));
  });

  protected readonly pastDue = computed(() => {
    const s = this.s();
    if (!s) return { total: 0, contracts: 0, share: 0, segments: [] as Segment[] };
    const rows = s.delinquency.slice(1);
    const total = rows.reduce((a, b) => a + b.exposure, 0);
    return {
      total,
      contracts: rows.reduce((a, b) => a + b.contracts, 0),
      share: s.totalExposure ? total / s.totalExposure : 0,
      segments: rows.map((b, i) => ({ pct: total ? (b.exposure / total) * 100 : 0, tone: BUCKET_TONES[i + 1] })),
    };
  });

  protected readonly buckets = computed(() =>
    (this.s()?.delinquency ?? []).map((b, i) => ({ ...b, tone: BUCKET_TONES[i], name: bucketName(b.bucket) })),
  );

  protected readonly recovery = computed(() => {
    const r = this.s()?.remarketing;
    if (!r) return [];
    const base = Math.max(1, r.exposureAtDefault);
    const net = r.saleProceeds - r.recoveryCosts;
    return [
      { label: 'Exposure at default', value: formatEur(r.exposureAtDefault), pct: 100, tone: 'exposure' },
      { label: 'Sale proceeds', value: formatEur(r.saleProceeds), pct: (r.saleProceeds / base) * 100, tone: 'collateral' },
      { label: 'Recovery costs', value: '−' + formatEur(r.recoveryCosts), pct: (r.recoveryCosts / base) * 100, tone: 'muted' },
      { label: 'Net recovery', value: formatEur(net), pct: (net / base) * 100, tone: 'good' },
    ];
  });

  protected readonly vendors = computed(() => {
    const s = this.s();
    if (!s) return [];
    const max = Math.max(1, ...s.topVendors.map((v) => v.exposure));
    return s.topVendors.slice(0, 6).map((v) => ({ ...v, pct: (v.exposure / max) * 100, selected: this.filters.filter().vendorId === v.vendorId }));
  });

  protected readonly watchlist = computed(() =>
    (this.s()?.byAssetClass ?? [])
      .filter((c) => c.loanToValue > 1)
      .sort((a, b) => b.loanToValue - a.loanToValue)
      .map((c) => ({ ...c, name: humanize(c.assetClass), gap: c.exposure - c.marketValue, why: UNDERWATER_REASON[c.assetClass] ?? '' })),
  );

  protected readonly category = signal<EventCategory>('all');
  protected readonly categories: { id: EventCategory; label: string }[] = [
    { id: 'all', label: 'All' },
    { id: 'credit', label: 'Credit' },
    { id: 'valuation', label: 'Valuations' },
    { id: 'remarketing', label: 'Remarketing' },
  ];

  protected readonly events = computed(() => {
    const cat = this.category();
    const cls = this.filters.filter().assetClass;
    return this.hub
      .events()
      .filter((e) => (cat === 'all' || EVENT_CATEGORY[e.type] === cat) && (!cls || !e.assetClass || e.assetClass === cls))
      .slice(0, 14)
      .map((e) => ({ ...e, style: styleFor(e.type), amountText: eventAmount(e) }));
  });

  protected exportCsv() {
    const s = this.s();
    if (!s) return;
    const rows: (string | number)[][] = [
      ['Filter', this.filters.summary()],
      ['Generated (UTC)', s.generatedAt],
      [],
      ['Metric', 'Value'],
      ['Open contracts', s.openContracts],
      ['Active assets', s.activeAssets],
      ['Total exposure (EUR)', s.totalExposure],
      ['Collateral market value (EUR)', s.totalMarketValue],
      ['Forced sale value (EUR)', s.totalForcedSaleValue],
      ['Loan-to-value', s.loanToValue],
      ['Exposure 30+ dpd (EUR)', s.exposureAtRisk],
      ['Shortfall at forced sale (EUR)', s.forcedSaleShortfall],
      ['Recovery rate', s.remarketing.recoveryRate],
      [],
      ['Asset class', 'Assets', 'Exposure (EUR)', 'Market value (EUR)', 'LTV'],
      ...s.byAssetClass.map((c) => [c.assetClass, c.assets, c.exposure, c.marketValue, c.loanToValue]),
      [],
      ['Delinquency bucket', 'Contracts', 'Exposure (EUR)'],
      ...s.delinquency.map((d) => [d.bucket, d.contracts, d.exposure]),
    ];
    download(`portfolio-overview-${s.generatedAt.slice(0, 19).replace(/[:T]/g, '')}.csv`, toCsv(rows));
  }

  protected toggleClass(c: AssetClass) {
    this.filters.toggle('assetClass', c);
  }
}

/** Revaluations carry a change in value, so they get an explicit sign. */
function eventAmount(e: { type: PortfolioEventType; amount?: number }) {
  if (e.amount == null) return '';
  if (e.type === 'Revaluation' || e.type === 'PriceReduced') return (e.amount >= 0 ? '+' : '−') + formatEurFull(Math.abs(e.amount));
  return formatEurFull(e.amount);
}

function bucketName(bucket: string) {
  return bucket === 'Current' ? 'Current' : bucket.replace(' dpd', ' days').replace('90+ days', '90+ days (default)').replace('-', '–');
}

function toCsv(rows: (string | number)[][]) {
  const cell = (v: string | number) => (typeof v === 'string' && /[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : String(v));
  return rows.map((r) => r.map(cell).join(',')).join('\n');
}

function download(name: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
  const a = Object.assign(document.createElement('a'), { href: url, download: name });
  a.click();
  URL.revokeObjectURL(url);
}

