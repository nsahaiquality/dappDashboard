import { Component, inject } from '@angular/core';
import { DashboardHubService } from '../core/dashboard-hub.service';
import { FilterService } from '../core/filter.service';
import { formatInt, humanize } from '../core/format';
import { ASSET_CLASSES, AssetClass, PRODUCT_TYPES, ProductType } from '../core/models';

/** Global filters; every widget on every page follows them. */
@Component({
  selector: 'app-filter-bar',
  template: `
    <div class="filter-bar" role="group" aria-label="Portfolio filters" [class.pending]="hub.pending()">
      <div class="segmented" role="group" aria-label="Contracts">
        <button type="button" [attr.aria-pressed]="!f().underwaterOnly" (click)="filters.patch({ underwaterOnly: false })">All contracts</button>
        <button type="button" [attr.aria-pressed]="f().underwaterOnly" (click)="filters.patch({ underwaterOnly: true })">Underwater only</button>
      </div>
      <label class="field">Asset class
        <select [value]="f().assetClass ?? ''" (change)="filters.patch({ assetClass: $any(sel($event)) || null })">
          <option value="">All</option>
          @for (c of classes; track c) { <option [value]="c" [selected]="f().assetClass === c">{{ humanize(c) }}</option> }
        </select>
      </label>
      <label class="field">Country
        <select (change)="filters.patch({ country: sel($event) || null })">
          <option value="">All</option>
          @for (c of filters.reference().countries; track c) { <option [value]="c" [selected]="f().country === c">{{ c }}</option> }
        </select>
      </label>
      <label class="field">Vendor
        <select (change)="filters.patch({ vendorId: sel($event) ? +sel($event) : null })">
          <option value="">All</option>
          @for (v of filters.reference().vendors; track v.id) { <option [value]="v.id" [selected]="f().vendorId === v.id">{{ v.name }}</option> }
        </select>
      </label>
      <label class="field">Product
        <select (change)="filters.patch({ productType: $any(sel($event)) || null })">
          <option value="">All</option>
          @for (p of products; track p) { <option [value]="p" [selected]="f().productType === p">{{ humanize(p) }}</option> }
        </select>
      </label>
      @if (filters.active()) {
        <button type="button" class="link-btn" (click)="filters.reset()">Clear filters</button>
      }
      <span class="filter-count">
        @if (hub.snapshot(); as s) { {{ int(s.openContracts) }} open contracts · {{ int(s.activeAssets) }} assets }
      </span>
    </div>
  `,
})
export class FilterBar {
  protected readonly filters = inject(FilterService);
  protected readonly hub = inject(DashboardHubService);
  protected readonly f = this.filters.filter;
  protected readonly classes: readonly AssetClass[] = ASSET_CLASSES;
  protected readonly products: readonly ProductType[] = PRODUCT_TYPES;
  protected readonly humanize = humanize;
  protected readonly int = formatInt;

  protected sel(e: Event) {
    return (e.target as HTMLSelectElement).value;
  }
}
