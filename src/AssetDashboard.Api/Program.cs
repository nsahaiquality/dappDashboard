using System.Text.Json.Serialization;
using AssetDashboard.Api;
using AssetDashboard.Api.RealTime;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.History;
using AssetDashboard.Infrastructure.Refinancing;
using AssetDashboard.Infrastructure.Remarketing;
using AssetDashboard.Infrastructure.Sources;
using AssetDashboard.Infrastructure.Vendors;
using AssetDashboard.Infrastructure.Seeding;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AssetDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("AssetDb")));

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection("Seed"));
builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection("Simulator"));
builder.Services.AddScoped<PortfolioSeeder>();
builder.Services.AddScoped<PortfolioStatsService>();
builder.Services.Configure<HistoryOptions>(builder.Configuration.GetSection("History"));
builder.Services.AddScoped<PortfolioHistoryService>();
builder.Services.AddScoped<RefinancingService>();
builder.Services.AddScoped<RemarketingService>();
builder.Services.AddScoped<VendorService>();
builder.Services.AddScoped<SourceService>();
builder.Services.Configure<SourceFeedOptions>(builder.Configuration.GetSection("Sources"));
builder.Services.AddHostedService<SourceFeedSimulator>();

// Real-time pipeline: simulator → event sink → SignalR clients, plus throttled snapshot pushes.
builder.Services.AddSingleton<SnapshotBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SnapshotBroadcaster>());
builder.Services.AddSingleton<SignalREventSink>();
builder.Services.AddSingleton<IPortfolioEventSink>(sp => sp.GetRequiredService<SignalREventSink>());
builder.Services.AddSingleton<MarketSimulator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketSimulator>());
builder.Services.AddHostedService<HistoryRecorder>();

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Numbers are numbers (the web default also accepts strings, which makes the OpenAPI schema "number or string").
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new()
    {
        Title = "Asset Portfolio Dashboard API",
        Version = "v1",
        Description = "Read API behind the asset portfolio dashboard: portfolio snapshot, assets, history, refinancing, " +
                      "remarketing, vendors and data sources. Live updates are pushed over SignalR at /hubs/dashboard " +
                      "(server messages 'Snapshot' and 'PortfolioEvent'; client method 'SetFilter'). " +
                      "Training exercise on synthetic data; not related to any business.",
    };
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
    await db.Database.MigrateAsync();

    var seed = app.Configuration.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions();
    if (seed.Enabled)
    {
        await scope.ServiceProvider.GetRequiredService<PortfolioSeeder>().SeedAsync(seed);
        var history = app.Configuration.GetSection("History").Get<HistoryOptions>() ?? new HistoryOptions();
        await scope.ServiceProvider.GetRequiredService<PortfolioHistoryService>().EnsureBackfilledAsync(history.BackfillDays, seed.RandomSeed);
        var sources = app.Configuration.GetSection("Sources").Get<SourceFeedOptions>() ?? new SourceFeedOptions();
        await SourceFeedSimulator.EnsureSeededAsync(db, sources.HistoryDays, seed.RandomSeed);
    }
}

// Interactive API documentation: /openapi/v1.json (OpenAPI document) and /swagger (Swagger UI); on unless ApiDocs:Enabled=false.
if (app.Configuration.GetValue("ApiDocs:Enabled", true))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Asset Portfolio Dashboard API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "Asset Portfolio Dashboard API";
        o.DisplayRequestDuration();
        o.EnableTryItOutByDefault();
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.MapHealthChecks("/health");
app.MapDashboardApi();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
