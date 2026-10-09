using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace AssetDashboard.IntegrationTests;

/// <summary>
/// Starts a throwaway PostgreSQL container and the real API against it (migrations + a small seeded
/// portfolio, simulator off). Shared by all integration tests; requires a running Docker daemon.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const int SeedContracts = 1_500;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:AssetDb", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Seed:Enabled", "true");
            b.UseSetting("Seed:Contracts", SeedContracts.ToString());
            b.UseSetting("Seed:RandomSeed", "7");
            b.UseSetting("Simulator:Enabled", "false");
            b.UseSetting("History:BackfillDays", "120");
        });
        Client = Factory.CreateClient();
    }

    public Task<T?> GetAsync<T>(string url) => Client.GetFromJsonAsync<T>(url, Json);

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Read-only tests share one database.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>Tests that change data (simulator, hub) get their own database so read-only tests stay deterministic.</summary>
public sealed class MutableApiFixture : IAsyncLifetime
{
    public ApiFixture Api { get; } = new();
    public Task InitializeAsync() => Api.InitializeAsync();
    public Task DisposeAsync() => Api.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class MutableApiCollection : ICollectionFixture<MutableApiFixture>
{
    public const string Name = "mutable-api";
}
