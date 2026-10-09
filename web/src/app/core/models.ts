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
  | 'MarketShock';

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
}

export interface ValuationPoint {
  valuedAt: string;
  marketValue: number;
  forcedSaleValue: number;
  method: ValuationMethod;
}

export interface AssetDetail {
  asset: AssetListItem;
  originalCost: number;
  contractExposure: number;
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
