using AssetDashboard.Domain;

namespace AssetDashboard.Infrastructure.Dashboard;

/// <summary>Everything the dashboard needs in one message; pushed to clients over SignalR.</summary>
public sealed record PortfolioSnapshot(
    DateTime GeneratedAt,
    int OpenContracts,
    int ActiveAssets,
    decimal TotalExposure,
    decimal TotalMarketValue,
    decimal TotalForcedSaleValue,
    // Exposure / market value of the collateral.
    decimal LoanToValue,
    // Exposure on contracts 30+ days past due.
    decimal ExposureAtRisk,
    // Sum over open contracts of max(0, exposure − forced sale value): the expected loss if all were liquidated now.
    decimal ForcedSaleShortfall,
    IReadOnlyList<AssetClassStat> ByAssetClass,
    IReadOnlyList<DelinquencyBucket> Delinquency,
    RemarketingStat Remarketing,
    IReadOnlyList<VendorStat> TopVendors,
    IReadOnlyList<CountryStat> ByCountry,
    // The filter this snapshot was computed for.
    PortfolioFilter Filter);

public sealed record AssetClassStat(AssetClass AssetClass, int Assets, decimal MarketValue, decimal Exposure, decimal LoanToValue);

public sealed record DelinquencyBucket(string Bucket, int Contracts, decimal Exposure);

public sealed record RemarketingStat(
    int Repossessed,
    int Listed,
    int Sold,
    decimal ExposureAtDefault,
    decimal SaleProceeds,
    decimal RecoveryCosts,
    // (proceeds − costs) / exposure at default, for sold assets.
    decimal RecoveryRate,
    double AverageDaysToSell);

public sealed record VendorStat(int VendorId, string Name, int Contracts, decimal Exposure);

public sealed record CountryStat(string Country, int Assets, decimal MarketValue);

public enum PortfolioEventType
{
    Revaluation,
    PaymentReceived,
    PaymentMissed,
    Default,
    Repossession,
    Listed,
    Sold,
    MarketShock,
    RefinancingRequested,
    RefinancingApproved,
    RefinancingDeclined,
}

/// <summary>A single change in the portfolio, streamed to the live event feed.</summary>
public sealed record PortfolioEvent(
    DateTime At,
    PortfolioEventType Type,
    string Message,
    AssetClass? AssetClass = null,
    long? ContractId = null,
    long? AssetId = null,
    decimal? Amount = null);

public sealed record AssetListItem(
    long Id, string SerialNumber, AssetClass AssetClass, string Category, string Manufacturer, string Model,
    int YearOfManufacture, AssetStatus Status, AssetCondition Condition, string Country, string City,
    decimal MarketValue, decimal ForcedSaleValue, DateTime LastValuedAt, string ContractNumber, int DaysPastDue,
    // Contract exposure ÷ market value of the contract's remaining assets; null once sold.
    decimal? ContractLtv);

// ExposureShare: this asset's share of the contract balance at that date (from the amortisation schedule).
public sealed record ValuationPoint(DateTime ValuedAt, decimal MarketValue, decimal ForcedSaleValue, ValuationMethod Method,
    decimal ExposureShare);

public sealed record AssetDetail(AssetListItem Asset, decimal OriginalCost, decimal ContractExposure, decimal AssetExposure,
    ProductType ProductType, int TermMonths, DateOnly StartDate,
    string CustomerName, int CustomerRiskGrade, string VendorName, IReadOnlyList<ValuationPoint> Valuations);

public sealed record VendorOption(int Id, string Name, AssetClass AssetClass);

public sealed record ReferenceData(IReadOnlyList<VendorOption> Vendors, IReadOnlyList<string> Countries);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
