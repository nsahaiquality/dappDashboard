using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AssetDashboard.IntegrationTests;

/// <summary>Own database and a fixed simulator seed, so the long run is repeatable and isolated.</summary>
public sealed class LongRunFixture : IAsyncLifetime
{
    public ApiFixture Api { get; } = new(new Dictionary<string, string> { ["Simulator:RandomSeed"] = "11" });
    public Task InitializeAsync() => Api.InitializeAsync();
    public Task DisposeAsync() => Api.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class LongRunCollection : ICollectionFixture<LongRunFixture>
{
    public const string Name = "long-run";
}

/// <summary>
/// The live simulator must keep the portfolio realistic however long it runs (SCRUM-76). 6,000 steps on
/// 1,500 contracts are about as many changes per contract as 5-6 hours of live running on 20,000.
/// </summary>
[Collection(LongRunCollection.Name)]
public class LongRunSimulationTests(LongRunFixture fixture, ITestOutputHelper output)
{
    private const int Steps = 6_000;
    private readonly Sql sql = new(fixture.Api.ConnectionString);

    private sealed record Health(decimal Exposure, decimal Ltv, decimal ValueToListPrice, decimal DefaultedShare,
        decimal DelinquentShare, decimal RecoveryRate, long ArrearsOlderThanContract, long ClosedWithArrears)
    {
        public override string ToString() =>
            $"exposure {Exposure:N0} | LTV {Ltv:P1} | value/list {ValueToListPrice:P1} | defaulted {DefaultedShare:P1} | " +
            $"delinquent {DelinquentShare:P1} | recovery {RecoveryRate:P1} | arrears>age {ArrearsOlderThanContract} | closed with arrears {ClosedWithArrears}";
    }

    private async Task<Health> MeasureAsync()
    {
        var r = (await sql.RowsAsync("""
            SELECT
              (SELECT SUM("OutstandingPrincipal") FROM "Contracts" WHERE "Status" <> 'Closed') AS exposure,
              (SELECT SUM("MarketValue") FROM "Assets" WHERE "Status" <> 'Sold') AS market,
              (SELECT AVG("MarketValue" / "OriginalCost") FROM "Assets" WHERE "Status" <> 'Sold') AS value_to_list,
              (SELECT COUNT(*) FILTER (WHERE "Status" = 'Defaulted')::numeric / COUNT(*) FROM "Contracts" WHERE "Status" <> 'Closed') AS defaulted,
              (SELECT COUNT(*) FILTER (WHERE "DaysPastDue" BETWEEN 30 AND 89)::numeric / COUNT(*) FROM "Contracts" WHERE "Status" <> 'Closed') AS delinquent,
              (SELECT (SUM("SalePrice") - SUM("RecoveryCosts")) / NULLIF(SUM("ExposureAtDefault"), 0) FROM "RemarketingCases" WHERE "Status" = 'Sold') AS recovery,
              (SELECT COUNT(*) FROM "Contracts" WHERE "DaysPastDue" > CURRENT_DATE - "StartDate") AS arrears_older,
              (SELECT COUNT(*) FROM "Contracts" WHERE "Status" = 'Closed' AND "DaysPastDue" > 0) AS closed_with_arrears
            """)).Single();
        var exposure = (decimal)r["exposure"]!;
        return new Health(exposure, exposure / (decimal)r["market"]!, (decimal)r["value_to_list"]!, (decimal)r["defaulted"]!,
            (decimal)r["delinquent"]!, (decimal)r["recovery"]!, Convert.ToInt64(r["arrears_older"]), Convert.ToInt64(r["closed_with_arrears"]));
    }

    [Fact]
    public async Task Portfolio_stays_realistic_over_a_long_simulated_run()
    {
        var simulator = fixture.Api.Factory.Services.GetRequiredService<MarketSimulator>();
        var before = await MeasureAsync();
        output.WriteLine($"before: {before}");

        for (var i = 1; i <= Steps; i++)
        {
            await using var scope = fixture.Api.Factory.Services.CreateAsyncScope();
            await simulator.NextEventAsync(scope.ServiceProvider.GetRequiredService<AssetDbContext>());
            if (i % 1_500 == 0) output.WriteLine($"after {i,5}: {await MeasureAsync()}");
        }
        var after = await MeasureAsync();

        Assert.InRange(after.Ltv, 0.50m, 1.00m);
        Assert.InRange(after.ValueToListPrice, before.ValueToListPrice * 0.85m, before.ValueToListPrice * 1.15m);
        Assert.InRange(after.Exposure, before.Exposure * 0.85m, before.Exposure * 1.15m);
        Assert.InRange(after.DefaultedShare, 0m, 0.06m);
        Assert.InRange(after.DelinquentShare, 0m, 0.08m);
        Assert.InRange(after.RecoveryRate, 0.40m, 0.85m);
        Assert.Equal(0, after.ArrearsOlderThanContract);
        Assert.Equal(0, after.ClosedWithArrears);
    }

    /// <summary>
    /// Market shocks hit a whole class at once and happen per unit of time, not per contract, so they need their own
    /// long run: 600 shocks (about 8 hours of live running) with revaluations in between must not move values off-model.
    /// </summary>
    [Fact]
    public async Task Market_values_do_not_trend_over_hundreds_of_market_shocks()
    {
        var simulator = fixture.Api.Factory.Services.GetRequiredService<MarketSimulator>();
        var before = await MeasureAsync();

        for (var i = 1; i <= 600; i++)
        {
            await using var scope = fixture.Api.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
            await simulator.StepAsync(db, SimulationStep.MarketShock);
            for (var r = 0; r < 4; r++) await simulator.StepAsync(db, SimulationStep.Revalue);
        }
        var after = await MeasureAsync();
        output.WriteLine($"before: {before}\nafter:  {after}");

        Assert.InRange(after.ValueToListPrice, before.ValueToListPrice * 0.85m, before.ValueToListPrice * 1.15m);
        Assert.InRange(after.Ltv, 0.50m, 1.00m);
    }
}
