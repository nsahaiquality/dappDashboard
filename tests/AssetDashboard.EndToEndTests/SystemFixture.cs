using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>
/// Starts the whole system once, exactly as a developer runs it: the Aspire AppHost with PostgreSQL,
/// the API and the Angular app (fresh database, Ephemeral=true). Then installs and launches Chromium.
/// The Angular dev proxy and the API use fixed ports (4200, 5080), so a dev instance must not be running.
/// </summary>
public sealed class SystemFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public Uri WebUrl { get; private set; } = null!;
    public HttpClient Api { get; private set; } = null!;
    public Sql Sql { get; private set; } = null!;

    /// <summary>Where traces, screenshots and downloads of this run are written.</summary>
    public static string ArtifactsDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

    public async Task InitializeAsync()
    {
        foreach (var port in new[] { 4200, 5080 })
            if (PortInUse(port))
                throw new InvalidOperationException(
                    $"Port {port} is in use. Stop the running AppHost / API / ng serve first; the end-to-end tests start their own.");

        using var cts = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AssetDashboard_AppHost>(
            ["--Ephemeral=true", "--DcpPublisher:RandomizePorts=false"], cts.Token);
        App = await builder.BuildAsync(cts.Token);
        await App.StartAsync(cts.Token);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await App.ResourceNotifications.WaitForResourceAsync("web", KnownResourceStates.Running, cts.Token);

        Api = new HttpClient { BaseAddress = App.GetEndpoint("api", "http") };
        WebUrl = App.GetEndpoint("web", "http");
        Sql = new Sql((await App.GetConnectionStringAsync("AssetDb", cts.Token))!);
        await WaitForWebAsync(cts.Token);

        // The simulator stays paused unless a test resumes it, so figures hold still while they are compared.
        (await Api.PostAsync("/api/simulator/pause", null, cts.Token)).EnsureSuccessStatusCode();

        Directory.CreateDirectory(ArtifactsDir);
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
            throw new InvalidOperationException("Playwright could not install Chromium.");
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>ng serve compiles before it answers; wait until the app shell is served.</summary>
    private async Task WaitForWebAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        while (true)
        {
            try
            {
                if ((await http.GetAsync(WebUrl, ct)).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(1000, ct);
        }
    }

    private static bool PortInUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("localhost", port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
        Api?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SystemCollection : ICollectionFixture<SystemFixture>
{
    public const string Name = "system";
}
