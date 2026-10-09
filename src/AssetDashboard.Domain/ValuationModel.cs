namespace AssetDashboard.Domain;

/// <summary>
/// Simplified collateral valuation: declining-balance depreciation per asset class,
/// adjusted for condition and a market index, with a salvage floor.
/// Forced sale value applies a class-specific haircut for quick liquidation.
/// </summary>
public static class ValuationModel
{
    /// <param name="InitialDrop">Value lost in the first year ("drive off the lot").</param>
    /// <param name="AnnualDepreciation">Declining-balance rate per year.</param>
    /// <param name="SalvageFloor">Minimum value as a fraction of original cost.</param>
    /// <param name="ForcedSaleFactor">Forced sale value as a fraction of market value.</param>
    public sealed record ClassProfile(double InitialDrop, double AnnualDepreciation, double SalvageFloor, double ForcedSaleFactor);

    public static ClassProfile Profile(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Agriculture => new(0.10, 0.11, 0.15, 0.75),
        AssetClass.Construction => new(0.12, 0.13, 0.12, 0.70),
        AssetClass.Healthcare => new(0.15, 0.17, 0.05, 0.50),
        AssetClass.Technology => new(0.20, 0.30, 0.02, 0.35),
        AssetClass.Transportation => new(0.12, 0.16, 0.08, 0.70),
        AssetClass.MaterialHandling => new(0.10, 0.12, 0.10, 0.65),
        AssetClass.CleanTech => new(0.08, 0.07, 0.10, 0.55),
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass)),
    };

    public static double ConditionFactor(AssetCondition condition) => condition switch
    {
        AssetCondition.Excellent => 1.08,
        AssetCondition.Good => 1.00,
        AssetCondition.Fair => 0.88,
        AssetCondition.Poor => 0.70,
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    public static decimal MarketValue(decimal originalCost, AssetClass assetClass, double ageYears,
        AssetCondition condition, double marketIndex = 1.0)
    {
        var p = Profile(assetClass);
        ageYears = Math.Max(0, ageYears);
        var remaining = (1 - p.InitialDrop * Math.Min(ageYears, 1)) * Math.Pow(1 - p.AnnualDepreciation, ageYears);
        remaining = Math.Max(remaining, p.SalvageFloor);
        return Math.Round(originalCost * (decimal)(remaining * ConditionFactor(condition) * marketIndex), 2);
    }

    public static decimal ForcedSaleValue(decimal marketValue, AssetClass assetClass) =>
        Math.Round(marketValue * (decimal)Profile(assetClass).ForcedSaleFactor, 2);
}
