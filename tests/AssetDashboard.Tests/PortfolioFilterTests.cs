using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;

namespace AssetDashboard.Tests;

public class PortfolioFilterTests
{
    [Fact]
    public void Equivalent_filters_share_a_group_key()
    {
        var a = new PortfolioFilter(AssetClass.Agriculture, " nl ").Normalize();
        var b = new PortfolioFilter(AssetClass.Agriculture, "NL").Normalize();

        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void Different_filters_get_different_keys()
    {
        var keys = new[]
        {
            PortfolioFilter.None,
            new PortfolioFilter(AssetClass: AssetClass.Healthcare),
            new PortfolioFilter(Country: "DE"),
            new PortfolioFilter(VendorId: 7),
            new PortfolioFilter(ProductType: ProductType.Loan),
            new PortfolioFilter(UnderwaterOnly: true),
        }.Select(f => f.Normalize().Key);

        Assert.Equal(6, keys.Distinct().Count());
    }

    [Fact]
    public void Blank_country_means_no_country_filter()
    {
        Assert.Equal(PortfolioFilter.None.Key, new PortfolioFilter(Country: "  ").Normalize().Key);
    }

    [Fact]
    public void Filter_narrows_contracts_in_memory()
    {
        var contracts = new PortfolioGeneratorFixture().Contracts.AsQueryable();
        var filter = new PortfolioFilter(AssetClass.Construction);

        Assert.All(filter.Apply(contracts), c => Assert.Equal(AssetClass.Construction, c.AssetClass));
    }

    private sealed class PortfolioGeneratorFixture
    {
        public List<Contract> Contracts { get; } =
            new AssetDashboard.Infrastructure.Seeding.PortfolioGenerator(3).Generate(300, new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)).Contracts;
    }
}
