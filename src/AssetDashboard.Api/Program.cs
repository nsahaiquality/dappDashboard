using System.Text.Json.Serialization;
using AssetDashboard.Api;
using AssetDashboard.Api.RealTime;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.History;
using AssetDashboard.Infrastructure.Refinancing;
using AssetDashboard.Infrastructure.Remarketing;
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
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
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
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapHealthChecks("/health");
app.MapDashboardApi();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
