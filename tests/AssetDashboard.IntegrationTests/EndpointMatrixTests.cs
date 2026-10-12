using System.Net;
using System.Text.Json;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.Extensions.DependencyInjection;

namespace AssetDashboard.IntegrationTests;

/// <summary>Every endpoint that accepts the portfolio filter, called with every kind of filter.</summary>
[Collection(ApiCollection.Name)]
public class EndpointMatrixTests(ApiFixture api)
{
    private static readonly string[] FilteredEndpoints =
    [
        "/api/dashboard/snapshot", "/api/assets", "/api/assets/export",
        "/api/refinancing/summary", "/api/refinancing/requests", "/api/refinancing/candidates",
        "/api/remarketing/summary", "/api/remarketing/cases", "/api/remarketing/auctions", "/api/vendors",
    ];

    private static readonly string[] FilterQueries =
    [
        "", "assetClass=Transportation", "country=be", "vendorId=1", "productType=OperatingLease", "underwaterOnly=true",
    ];

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var endpoint in FilteredEndpoints)
            foreach (var query in FilterQueries)
                data.Add(endpoint, query);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Filtered_endpoint_answers_and_respects_the_filter(string endpoint, string query)
    {
        var response = await api.Client.GetAsync($"{endpoint}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        if (endpoint.EndsWith("/export"))
        {
            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("Serial,", await response.Content.ReadAsStringAsync());
            return;
        }

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        if (json.TryGetProperty("filter", out var filter))
        {
            // Summaries echo the filter they were computed for (country normalised to upper case).
            if (query.StartsWith("assetClass=")) Assert.Equal("Transportation", filter.GetProperty("assetClass").GetString());
            if (query.StartsWith("country=")) Assert.Equal("BE", filter.GetProperty("country").GetString());
            if (query.StartsWith("vendorId=")) Assert.Equal(1, filter.GetProperty("vendorId").GetInt32());
            if (query.StartsWith("productType=")) Assert.Equal("OperatingLease", filter.GetProperty("productType").GetString());
            Assert.Equal(query.StartsWith("underwaterOnly="), filter.GetProperty("underwaterOnly").GetBoolean());
        }
        if (json.TryGetProperty("items", out var items))
        {
            Assert.True(json.GetProperty("total").GetInt32() >= items.GetArrayLength());
            if (query.StartsWith("assetClass=") && items.GetArrayLength() > 0 && items[0].TryGetProperty("assetClass", out _))
                Assert.All(items.EnumerateArray(), i => Assert.Equal("Transportation", i.GetProperty("assetClass").GetString()));
        }
    }

    [Theory]
    [InlineData("/api/assets?assetClass=Spaceships")]
    [InlineData("/api/assets?page=abc")]
    [InlineData("/api/refinancing/requests?status=Maybe")]
    public async Task Invalid_parameters_are_rejected_with_400(string url)
    {
        var response = await api.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await api.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Endpoints whose behaviour changes state; they use their own database.</summary>
[Collection(MutableApiCollection.Name)]
public class StatefulEndpointTests(MutableApiFixture fixture)
{
    private readonly ApiFixture api = fixture.Api;

    [Fact]
    public async Task Simulator_can_be_resumed_and_paused()
    {
        var resumed = await (await api.Client.PostAsync("/api/simulator/resume", null)).Content.ReadAsStringAsync();
        var stateWhileRunning = await api.Client.GetStringAsync("/api/simulator");
        var paused = await (await api.Client.PostAsync("/api/simulator/pause", null)).Content.ReadAsStringAsync();
        var stateWhilePaused = await api.Client.GetStringAsync("/api/simulator");

        Assert.Contains("\"running\":true", resumed);
        Assert.Contains("\"running\":true", stateWhileRunning);
        Assert.Contains("\"running\":false", paused);
        Assert.Contains("\"running\":false", stateWhilePaused);
    }

    [Fact]
    public async Task Events_endpoint_returns_the_last_50_oldest_first()
    {
        var sink = api.Factory.Services.GetRequiredService<IPortfolioEventSink>();
        var start = DateTime.UtcNow;
        for (var i = 0; i < 60; i++)
            await sink.PublishAsync(new PortfolioEvent(start.AddMilliseconds(i), PortfolioEventType.Revaluation, $"test event {i}"));

        var events = (await api.GetAsync<List<PortfolioEvent>>("/api/dashboard/events"))!;

        Assert.Equal(50, events.Count);
        Assert.Equal("test event 59", events[^1].Message);
        Assert.Equal(events.Select(e => e.At).OrderBy(t => t), events.Select(e => e.At));
    }
}
