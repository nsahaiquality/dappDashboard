// Mirrors the DTOs in AssetDashboard.Infrastructure/Dashboard/DashboardDtos.cs.
// Enums are serialised as strings by the API.

export const ASSET_CLASSES = [
  'Agriculture',
  'Construction',
  'Healthcare',
  'Technology',
  'Transportation',
  'MaterialHandling',
  'CleanTech',
] as const;
export type AssetClass = (typeof ASSET_CLASSES)[number];

export const ASSET_STATUSES = ['InUse', 'Repossessed', 'InRemarketing', 'Sold', 'Returned'] as const;
export type AssetStatus = (typeof ASSET_STATUSES)[number];

export const PRODUCT_TYPES = ['FinanceLease', 'OperatingLease', 'Loan', 'HirePurchase'] as const;
export type ProductType = (typeof PRODUCT_TYPES)[number];

export type AssetSort = 'MarketValue' | 'Ltv' | 'DaysPastDue';

/** Global dashboard filter; mirrors PortfolioFilter on the server. */
export interface PortfolioFilter {
  assetClass: AssetClass | null;
  country: string | null;
  vendorId: number | null;
  productType: ProductType | null;
  underwaterOnly: boolean;
}

export const NO_FILTER: PortfolioFilter = { assetClass: null, country: null, vendorId: null, productType: null, underwaterOnly: false };

export const sameFilter = (a: PortfolioFilter, b: PortfolioFilter) =>
  a.assetClass === b.assetClass &&
  (a.country ?? null) === (b.country ?? null) &&
  a.vendorId === b.vendorId &&
  a.productType === b.productType &&
  a.underwaterOnly === b.underwaterOnly;

export interface VendorOption {
  id: number;
  name: string;
  assetClass: AssetClass;
}

export interface ReferenceData {
  vendors: VendorOption[];
  countries: string[];
}

export type AssetCondition = 'Excellent' | 'Good' | 'Fair' | 'Poor';
export type ValuationMethod = 'Invoice' | 'IndexBased' | 'PhysicalInspection' | 'AuctionComparable';

export interface PortfolioSnapshot {
  generatedAt: string;
  openContracts: number;
  activeAssets: number;
  totalExposure: number;
  totalMarketValue: number;
  totalForcedSaleValue: number;
  loanToValue: number;
  exposureAtRisk: number;
  forcedSaleShortfall: number;
  byAssetClass: AssetClassStat[];
  delinquency: DelinquencyBucket[];
  remarketing: RemarketingStat;
  topVendors: VendorStat[];
  byCountry: CountryStat[];
  filter: PortfolioFilter;
}

export interface AssetClassStat {
  assetClass: AssetClass;
  assets: number;
  marketValue: number;
  exposure: number;
  loanToValue: number;
}

export interface DelinquencyBucket {
  bucket: string;
  contracts: number;
  exposure: number;
}

export interface RemarketingStat {
  repossessed: number;
  listed: number;
  sold: number;
  exposureAtDefault: number;
  saleProceeds: number;
  recoveryCosts: number;
  recoveryRate: number;
  averageDaysToSell: number;
}

export interface VendorStat {
  vendorId: number;
  name: string;
  contracts: number;
  exposure: number;
}

export interface CountryStat {
  country: string;
  assets: number;
  marketValue: number;
}

export type PortfolioEventType =
  | 'Revaluation'
  | 'PaymentReceived'
  | 'PaymentMissed'
  | 'Default'
  | 'Repossession'
  | 'Listed'
  | 'Sold'
  | 'MarketShock'
  | 'RefinancingRequested'
  | 'RefinancingApproved'
  | 'RefinancingDeclined'
  | 'AuctionCompleted'
  | 'PriceReduced'
  | 'DataLoadFailed'
  | 'DataLoadRecovered';

export interface PortfolioEvent {
  at: string;
  type: PortfolioEventType;
  message: string;
  assetClass?: AssetClass;
  contractId?: number;
  assetId?: number;
  amount?: number;
}

export interface AssetListItem {
  id: number;
  serialNumber: string;
  assetClass: AssetClass;
  category: string;
  manufacturer: string;
  model: string;
  yearOfManufacture: number;
  status: AssetStatus;
  condition: AssetCondition;
  country: string;
  city: string;
  marketValue: number;
  forcedSaleValue: number;
  lastValuedAt: string;
  contractNumber: string;
  daysPastDue: number;
  /** Contract exposure ÷ market value of its remaining assets; null once sold. */
  contractLtv: number | null;
}

export interface ValuationPoint {
  valuedAt: string;
  marketValue: number;
  forcedSaleValue: number;
  method: ValuationMethod;
  /** This asset's share of the contract balance at that date. */
  exposureShare: number;
}

export interface AssetDetail {
  asset: AssetListItem;
  originalCost: number;
  contractExposure: number;
  assetExposure: number;
  productType: ProductType;
  termMonths: number;
  startDate: string;
  customerName: string;
  customerRiskGrade: number;
  vendorName: string;
  valuations: ValuationPoint[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
