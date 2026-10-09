using System.Threading.Channels;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AssetDashboard.IntegrationTests;

[Collection(MutableApiCollection.Name)]
public class RealTimeAndSimulatorTests(MutableApiFixture fixture)
{
    private readonly ApiFixture api = fixture.Api;

    [Fact]
    public async Task Hub_sends_a_snapshot_on_connect_and_after_SetFilter()
    {
        var snapshots = Channel.CreateUnbounded<PortfolioSnapshot>();
        var server = api.Factory.Server;
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/dashboard"), o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .AddJsonProtocol(o => o.PayloadSerializerOptions = ApiFixture.Json)
            .Build();
        connection.On<PortfolioSnapshot>("Snapshot", s => snapshots.Writer.TryWrite(s));

        await connection.StartAsync();
        var filter = new PortfolioFilter(AssetClass: AssetClass.Construction);
        await connection.InvokeAsync("SetFilter", filter);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        PortfolioSnapshot received;
        do received = await snapshots.Reader.ReadAsync(timeout.Token);
        while (received.Filter.Key != filter.Key);

        Assert.All(received.ByAssetClass, c => Assert.Equal(AssetClass.Construction, c.AssetClass));
    }

    [Theory]
    [InlineData(SimulationStep.Revalue)]
    [InlineData(SimulationStep.ReceivePayment)]
    [InlineData(SimulationStep.MissPayment)]
    [InlineData(SimulationStep.Repossess)]
    [InlineData(SimulationStep.ListForSale)]
    [InlineData(SimulationStep.Sell)]
    [InlineData(SimulationStep.MarketShock)]
    public async Task Every_simulator_step_runs_against_postgres(SimulationStep step)
    {
        var simulator = api.Factory.Services.GetRequiredService<MarketSimulator>();
        PortfolioEvent? evt = null;
        // A step may find nothing to act on (returns null); a few attempts make that unlikely.
        for (var i = 0; i < 5 && evt is null; i++)
        {
            await using var scope = api.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
            evt = await simulator.StepAsync(db, step);
        }

        Assert.NotNull(evt);
        Assert.False(string.IsNullOrWhiteSpace(evt.Message));
    }
}
