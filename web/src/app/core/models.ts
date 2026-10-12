// API types come from the contract (contracts/openapi.v1.json), generated into api.generated.ts by
// `npm run api:generate`. Don't edit them here: change the API, update the contract, regenerate.
// This file names them for the app and adds the few client-side types, with compile-time checks
// that keep the hand-written parts in step with the contract.
import type { components } from './api.generated';

type Schemas = components['schemas'];

export type AssetClass = Schemas['AssetClass'];
export type AssetStatus = Schemas['AssetStatus'];
export type AssetCondition = Schemas['AssetCondition'];
export type ProductType = Schemas['ProductType'];
export type ValuationMethod = Schemas['ValuationMethod'];
export type AssetSort = Schemas['AssetSort'];
export type PortfolioEventType = Schemas['PortfolioEventType'];

export type PortfolioSnapshot = Schemas['PortfolioSnapshot'];
export type AssetClassStat = Schemas['AssetClassStat'];
export type DelinquencyBucket = Schemas['DelinquencyBucket'];
export type RemarketingStat = Schemas['RemarketingStat'];
export type VendorStat = Schemas['VendorStat'];
export type CountryStat = Schemas['CountryStat'];
export type PortfolioEvent = Schemas['PortfolioEvent'];
export type AssetListItem = Schemas['AssetListItem'];
export type ValuationPoint = Schemas['ValuationPoint'];
export type AssetDetail = Schemas['AssetDetail'];
export type VendorOption = Schemas['VendorOption'];
export type ReferenceData = Schemas['ReferenceData'];

/** A page of results; checked against every paged schema in the contract below. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// Option lists for dropdowns, in display order.
export const ASSET_CLASSES = [
  'Agriculture',
  'Construction',
  'Healthcare',
  'Technology',
  'Transportation',
  'MaterialHandling',
  'CleanTech',
] as const satisfies readonly AssetClass[];
export const ASSET_STATUSES = [
  'InUse',
  'Repossessed',
  'InRemarketing',
  'Sold',
  'Returned',
] as const satisfies readonly AssetStatus[];
export const PRODUCT_TYPES = [
  'FinanceLease',
  'OperatingLease',
  'Loan',
  'HirePurchase',
] as const satisfies readonly ProductType[];

/**
 * The global filter as the app holds it: every field present, `null` when not set.
 * The API's PortfolioFilter has the same fields, all optional.
 */
export interface PortfolioFilter {
  assetClass: AssetClass | null;
  country: string | null;
  vendorId: number | null;
  productType: ProductType | null;
  underwaterOnly: boolean;
}

export const NO_FILTER: PortfolioFilter = {
  assetClass: null,
  country: null,
  vendorId: null,
  productType: null,
  underwaterOnly: false,
};

/** True when two filters select the same data; accepts the app's filter or the one echoed in a snapshot. */
export const sameFilter = (a: Schemas['PortfolioFilter'], b: Schemas['PortfolioFilter']) =>
  (a.assetClass ?? null) === (b.assetClass ?? null) &&
  (a.country ?? null) === (b.country ?? null) &&
  (a.vendorId ?? null) === (b.vendorId ?? null) &&
  (a.productType ?? null) === (b.productType ?? null) &&
  (a.underwaterOnly ?? false) === (b.underwaterOnly ?? false);

// ---- compile-time contract checks (fail the build when the API changes underneath) -------------

type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
type Check<T extends true> = T;

export type ContractChecks = [
  // Every enum value is offered in its dropdown, and nothing else.
  Check<Same<(typeof ASSET_CLASSES)[number], AssetClass>>,
  Check<Same<(typeof ASSET_STATUSES)[number], AssetStatus>>,
  Check<Same<(typeof PRODUCT_TYPES)[number], ProductType>>,
  // The app's filter can be sent as the API's filter (SetFilter, query parameters).
  Check<PortfolioFilter extends Schemas['PortfolioFilter'] ? true : false>,
  // The generic page matches the contract's paged results.
  Check<Same<PagedResult<AssetListItem>, Schemas['PagedResultOfAssetListItem']>>,
];
