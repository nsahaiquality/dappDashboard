using AssetDashboard.Domain;

namespace AssetDashboard.Infrastructure.Dashboard;

/// <summary>Global dashboard filter. Every field is optional; an empty filter means the whole portfolio.</summary>
public sealed record PortfolioFilter(
    AssetClass? AssetClass = null,
    string? Country = null,
    int? VendorId = null,
    ProductType? ProductType = null,
    // Only contracts whose exposure exceeds the market value of their remaining collateral.
    bool UnderwaterOnly = false)
{
    public static readonly PortfolioFilter None = new();

    /// <summary>Stable identity, used as the SignalR group name for connections sharing this filter.</summary>
    public string Key => $"f:{AssetClass}|{Country?.ToUpperInvariant()}|{VendorId}|{ProductType}|{(UnderwaterOnly ? 1 : 0)}";

    public PortfolioFilter Normalize() => this with
    {
        Country = string.IsNullOrWhiteSpace(Country) ? null : Country.Trim().ToUpperInvariant(),
    };

    public IQueryable<Contract> Apply(IQueryable<Contract> contracts)
    {
        if (AssetClass is { } assetClass) contracts = contracts.Where(c => c.AssetClass == assetClass);
        if (Country is { } country) contracts = contracts.Where(c => c.Customer.Country == country);
        if (VendorId is { } vendorId) contracts = contracts.Where(c => c.VendorId == vendorId);
        if (ProductType is { } productType) contracts = contracts.Where(c => c.ProductType == productType);
        if (UnderwaterOnly)
            contracts = contracts.Where(c => c.OutstandingPrincipal >
                                             c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.MarketValue));
        return contracts;
    }

    public IQueryable<Asset> Apply(IQueryable<Asset> assets)
    {
        if (AssetClass is { } assetClass) assets = assets.Where(a => a.AssetClass == assetClass);
        if (Country is { } country) assets = assets.Where(a => a.Country == country);
        if (VendorId is { } vendorId) assets = assets.Where(a => a.Contract.VendorId == vendorId);
        if (ProductType is { } productType) assets = assets.Where(a => a.Contract.ProductType == productType);
        if (UnderwaterOnly)
            assets = assets.Where(a => a.Contract.OutstandingPrincipal >
                                       a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue));
        return assets;
    }

    /// <summary>
    /// Remarketing cases follow the dimensional filters. "Underwater only" does not apply:
    /// sold cases belong to closed contracts, whose exposure is zero.
    /// </summary>
    public IQueryable<RemarketingCase> Apply(IQueryable<RemarketingCase> cases)
    {
        if (AssetClass is { } assetClass) cases = cases.Where(c => c.Asset.AssetClass == assetClass);
        if (Country is { } country) cases = cases.Where(c => c.Asset.Country == country);
        if (VendorId is { } vendorId) cases = cases.Where(c => c.Asset.Contract.VendorId == vendorId);
        if (ProductType is { } productType) cases = cases.Where(c => c.Asset.Contract.ProductType == productType);
        return cases;
    }
}
