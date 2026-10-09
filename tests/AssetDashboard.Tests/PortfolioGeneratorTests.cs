using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Seeding;

namespace AssetDashboard.Tests;

public class PortfolioGeneratorTests
{
    private static readonly DateTime AsOf = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly PortfolioGenerator.Portfolio Portfolio = new PortfolioGenerator(seed: 7).Generate(5_000, AsOf);

    [Fact]
    public void Same_seed_gives_same_portfolio()
    {
        var a = new PortfolioGenerator(seed: 1).Generate(200, AsOf);
        var b = new PortfolioGenerator(seed: 1).Generate(200, AsOf);

        Assert.Equal(a.Contracts.Select(c => c.OutstandingPrincipal), b.Contracts.Select(c => c.OutstandingPrincipal));
        Assert.Equal(a.Contracts.SelectMany(c => c.Assets).Select(x => x.SerialNumber),
                     b.Contracts.SelectMany(c => c.Assets).Select(x => x.SerialNumber));
    }

    [Fact]
    public void Every_contract_has_assets_of_its_own_class()
    {
        Assert.All(Portfolio.Contracts, c =>
        {
            Assert.NotEmpty(c.Assets);
            Assert.All(c.Assets, a => Assert.Equal(c.AssetClass, a.AssetClass));
        });
    }

    [Fact]
    public void Serial_numbers_are_unique()
    {
        var serials = Portfolio.Contracts.SelectMany(c => c.Assets).Select(a => a.SerialNumber).ToList();
        Assert.Equal(serials.Count, serials.Distinct().Count());
    }

    [Fact]
    public void Default_rate_is_realistic()
    {
        var defaulted = Portfolio.Contracts.Count(c => c.Status is ContractStatus.Defaulted
                                                      || c.Assets.Any(a => a.RemarketingCase is not null));
        var rate = (double)defaulted / Portfolio.Contracts.Count;

        Assert.InRange(rate, 0.01, 0.08);
    }

    [Fact]
    public void Portfolio_is_on_average_overcollateralised()
    {
        var open = Portfolio.Contracts.Where(c => c.Status != ContractStatus.Closed).ToList();
        var exposure = open.Sum(c => c.OutstandingPrincipal);
        var collateral = open.SelectMany(c => c.Assets).Where(a => a.Status != AssetStatus.Sold).Sum(a => a.MarketValue);

        Assert.InRange(exposure / collateral, 0.4m, 1.0m);
    }

    [Fact]
    public void Remarketing_state_is_consistent()
    {
        var assets = Portfolio.Contracts.SelectMany(c => c.Assets).ToList();

        Assert.All(assets.Where(a => a.Status == AssetStatus.Sold), a =>
        {
            Assert.Equal(RemarketingStatus.Sold, a.RemarketingCase!.Status);
            Assert.NotNull(a.RemarketingCase.SalePrice);
            Assert.True(a.RemarketingCase.SoldOn >= a.RemarketingCase.RepossessedOn);
        });
        Assert.All(assets.Where(a => a.Status == AssetStatus.InUse), a => Assert.Null(a.RemarketingCase));
        Assert.All(Portfolio.Contracts.Where(c => c.Status == ContractStatus.Closed),
            c => Assert.Equal(0m, c.OutstandingPrincipal));
    }

    [Fact]
    public void Latest_valuation_matches_asset_value()
    {
        Assert.All(Portfolio.Contracts.SelectMany(c => c.Assets).Take(500), a =>
        {
            var latest = a.Valuations.MaxBy(v => v.ValuedAt)!;
            Assert.Equal(latest.MarketValue, a.MarketValue);
            Assert.True(a.ForcedSaleValue <= a.MarketValue);
        });
    }
}
