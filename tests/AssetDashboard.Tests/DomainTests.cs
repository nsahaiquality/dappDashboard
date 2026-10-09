using AssetDashboard.Domain;

namespace AssetDashboard.Tests;

public class AmortizationTests
{
    [Theory]
    [InlineData(100_000, 5.0, 60, 0)]
    [InlineData(250_000, 7.5, 48, 50_000)]
    [InlineData(40_000, 0.0, 36, 0)]
    public void Balance_after_full_term_equals_residual(decimal principal, decimal rate, int term, decimal residual)
    {
        var installment = Amortization.Installment(principal, rate, term, residual);
        var outstanding = Amortization.OutstandingAfter(principal, rate, installment, term);

        Assert.InRange(outstanding, residual - 5, residual + 5);
    }

    [Fact]
    public void Known_annuity_matches_reference_value()
    {
        // 100k over 60 months at 5% p.a. → 1,887.12 per month (standard annuity formula).
        Assert.Equal(1_887.12m, Amortization.Installment(100_000, 5.0m, 60));
    }

    [Fact]
    public void Balance_decreases_monotonically()
    {
        var installment = Amortization.Installment(80_000, 6m, 48);
        var balances = Enumerable.Range(0, 49).Select(m => Amortization.OutstandingAfter(80_000, 6m, installment, m)).ToList();

        Assert.True(balances.Zip(balances.Skip(1)).All(p => p.First >= p.Second));
    }
}

public class ValuationModelTests
{
    [Theory]
    [InlineData(AssetClass.Agriculture)]
    [InlineData(AssetClass.Technology)]
    [InlineData(AssetClass.Healthcare)]
    public void Value_declines_with_age_and_respects_salvage_floor(AssetClass assetClass)
    {
        var values = Enumerable.Range(0, 30)
            .Select(age => ValuationModel.MarketValue(100_000, assetClass, age, AssetCondition.Good))
            .ToList();

        Assert.Equal(100_000m, values[0]);
        Assert.True(values.Zip(values.Skip(1)).All(p => p.First >= p.Second));
        Assert.Equal((decimal)ValuationModel.Profile(assetClass).SalvageFloor * 100_000, values[^1]);
    }

    [Fact]
    public void Technology_depreciates_faster_than_agriculture()
    {
        var tech = ValuationModel.MarketValue(100_000, AssetClass.Technology, 3, AssetCondition.Good);
        var agri = ValuationModel.MarketValue(100_000, AssetClass.Agriculture, 3, AssetCondition.Good);

        Assert.True(tech < agri);
    }

    [Theory]
    [InlineData(AssetCondition.Excellent, AssetCondition.Good)]
    [InlineData(AssetCondition.Good, AssetCondition.Fair)]
    [InlineData(AssetCondition.Fair, AssetCondition.Poor)]
    public void Better_condition_means_higher_value(AssetCondition better, AssetCondition worse)
    {
        Assert.True(ValuationModel.MarketValue(50_000, AssetClass.Construction, 4, better)
                    > ValuationModel.MarketValue(50_000, AssetClass.Construction, 4, worse));
    }

    [Fact]
    public void Value_never_exceeds_original_cost()
    {
        foreach (var assetClass in Enum.GetValues<AssetClass>())
            Assert.Equal(80_000m, ValuationModel.MarketValue(80_000, assetClass, 0.1, AssetCondition.Excellent, marketIndex: 1.2));
    }

    [Fact]
    public void Forced_sale_value_is_below_market_value()
    {
        foreach (var assetClass in Enum.GetValues<AssetClass>())
            Assert.True(ValuationModel.ForcedSaleValue(10_000, assetClass) < 10_000);
    }
}
