import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { of } from 'rxjs';
import {
  AssetListItem,
  NO_FILTER,
  PagedResult,
  PortfolioFilter,
  PortfolioSnapshot,
  ReferenceData,
} from '../app/core/models';

/** Stands in for a SignalR HubConnection: records invocations and lets a test push server messages. */
export class FakeHubConnection {
  state = HubConnectionState.Disconnected;
  readonly invoked: { method: string; args: unknown[] }[] = [];
  private readonly handlers = new Map<string, (arg: unknown) => void>();
  private reconnected?: () => void;

  on(name: string, handler: (arg: unknown) => void) {
    this.handlers.set(name, handler);
  }
  onreconnecting() {}
  onreconnected(cb: () => void) {
    this.reconnected = cb;
  }
  onclose() {}
  async start() {
    this.state = HubConnectionState.Connected;
  }
  invoke(method: string, ...args: unknown[]) {
    this.invoked.push({ method, args });
    return Promise.resolve();
  }

  /** Simulates a server → client message. */
  emit(name: string, arg: unknown) {
    this.handlers.get(name)!(arg);
  }
  simulateReconnect() {
    this.reconnected?.();
  }
  asConnection() {
    return this as unknown as HubConnection;
  }
}

export const reference: ReferenceData = {
  vendors: [{ id: 7, name: 'Example Agri Dealers B.V.', assetClass: 'Agriculture' }],
  countries: ['DE', 'NL'],
};

/** An ApiService double; override single methods per test. */
export const apiStub = () => ({
  getReference: () => of(reference),
  getSimulator: () => of({ running: true }),
  setSimulator: (running: boolean) => of({ running }),
  getAssets: () => of(page([])),
  getAsset: () => of(null),
  assetsExportUrl: () => '/api/assets/export',
});

export const page = (items: AssetListItem[], total = items.length): PagedResult<AssetListItem> => ({
  items,
  total,
  page: 1,
  pageSize: 15,
});

export function asset(id: number, extra: Partial<AssetListItem> = {}): AssetListItem {
  return {
    id,
    serialNumber: `TST-2024-${String(id).padStart(7, '0')}`,
    assetClass: 'Agriculture',
    category: 'Tractor',
    manufacturer: 'Harvestar',
    model: 'T 300',
    yearOfManufacture: 2024,
    status: 'InUse',
    condition: 'Good',
    country: 'NL',
    city: 'Utrecht',
    marketValue: 100_000,
    forcedSaleValue: 75_000,
    lastValuedAt: '2026-10-01T00:00:00Z',
    contractNumber: `VF-2024-${String(id).padStart(7, '0')}`,
    daysPastDue: 0,
    contractLtv: 0.8,
    ...extra,
  };
}

/** A small but complete snapshot: two asset classes, one of them underwater. */
export function snapshot(filter: PortfolioFilter = NO_FILTER): PortfolioSnapshot {
  return {
    generatedAt: '2026-10-12T08:00:00Z',
    openContracts: 1_200,
    activeAssets: 1_900,
    totalExposure: 3_000_000_000,
    totalMarketValue: 4_000_000_000,
    totalForcedSaleValue: 2_600_000_000,
    loanToValue: 0.75,
    exposureAtRisk: 240_000_000,
    forcedSaleShortfall: 700_000_000,
    byAssetClass: [
      {
        assetClass: 'Agriculture',
        assets: 1_000,
        marketValue: 3_000_000_000,
        exposure: 2_000_000_000,
        loanToValue: 0.6667,
      },
      {
        assetClass: 'Healthcare',
        assets: 900,
        marketValue: 1_000_000_000,
        exposure: 1_000_000_000 * 1.1,
        loanToValue: 1.1,
      },
    ],
    delinquency: [
      { bucket: 'Current', contracts: 1_000, exposure: 2_600_000_000 },
      { bucket: '1-29 dpd', contracts: 100, exposure: 160_000_000 },
      { bucket: '30-59 dpd', contracts: 50, exposure: 100_000_000 },
      { bucket: '60-89 dpd', contracts: 25, exposure: 60_000_000 },
      { bucket: '90+ dpd', contracts: 25, exposure: 80_000_000 },
    ],
    remarketing: {
      repossessed: 10,
      listed: 20,
      sold: 30,
      exposureAtDefault: 10_000_000,
      saleProceeds: 7_000_000,
      recoveryCosts: 1_000_000,
      recoveryRate: 0.6,
      averageDaysToSell: 45,
    },
    topVendors: [
      { vendorId: 7, name: 'Example Agri Dealers B.V.', contracts: 300, exposure: 500_000_000 },
    ],
    byCountry: [{ country: 'NL', assets: 1_900, marketValue: 4_000_000_000 }],
    filter,
  };
}
