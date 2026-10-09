import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { humanize } from './format';
import { NO_FILTER, PortfolioFilter, ReferenceData } from './models';

/** The global filter shared by every page, plus the reference data that fills its dropdowns. */
@Injectable({ providedIn: 'root' })
export class FilterService {
  private readonly api = inject(ApiService);

  readonly filter = signal<PortfolioFilter>(NO_FILTER);
  readonly reference = signal<ReferenceData>({ vendors: [], countries: [] });

  readonly active = computed(() => {
    const f = this.filter();
    return !!(f.assetClass || f.country || f.vendorId || f.productType || f.underwaterOnly);
  });

  /** Human-readable chips for the active filter, each with the patch that removes it. */
  readonly chips = computed(() => {
    const f = this.filter();
    const vendors = this.reference().vendors;
    const chips: { label: string; clear: Partial<PortfolioFilter> }[] = [];
    if (f.underwaterOnly) chips.push({ label: 'Underwater contracts', clear: { underwaterOnly: false } });
    if (f.assetClass) chips.push({ label: humanize(f.assetClass), clear: { assetClass: null } });
    if (f.country) chips.push({ label: `Country: ${f.country}`, clear: { country: null } });
    if (f.vendorId) {
      const name = vendors.find((v) => v.id === f.vendorId)?.name ?? `#${f.vendorId}`;
      chips.push({ label: `Vendor: ${name}`, clear: { vendorId: null } });
    }
    if (f.productType) chips.push({ label: humanize(f.productType), clear: { productType: null } });
    return chips;
  });

  /** One-line description for headers, e.g. "Healthcare · NL". */
  readonly summary = computed(() => {
    const chips = this.chips();
    return chips.length ? chips.map((c) => c.label).join(' · ') : 'All contracts';
  });

  constructor() {
    this.api.getReference().subscribe({ next: (r) => this.reference.set(r), error: () => {} });
  }

  patch(change: Partial<PortfolioFilter>) {
    this.filter.update((f) => ({ ...f, ...change }));
  }

  /** Clicking an already-selected value toggles it off (chart drill-down behaviour). */
  toggle<K extends keyof PortfolioFilter>(key: K, value: PortfolioFilter[K]) {
    this.filter.update((f) => ({ ...f, [key]: f[key] === value ? NO_FILTER[key] : value }));
  }

  reset() {
    this.filter.set(NO_FILTER);
  }
}
