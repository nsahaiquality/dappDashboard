// Aspire AppHost: starts the whole system with one command and opens the Aspire dashboard
// (resources, logs, traces, metrics).  dotnet run --project src/AssetDashboard.AppHost
var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL in a container with a persistent volume; the API seeds it on first start.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17-alpine") // same major version as docker-compose and the integration tests
    .WithDataVolume("assetdash-aspire-pgdata17");
var database = postgres.AddDatabase("AssetDb", databaseName: "assetdashboard");

// The API. "AssetDb" becomes ConnectionStrings__AssetDb; the http launch profile keeps port 5080,
// which the Angular dev proxy expects.
var api = builder.AddProject<Projects.AssetDashboard_Api>("api", launchProfileName: "http")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = url.Url.TrimEnd('/') + "/swagger";
        url.DisplayText = "Swagger (API docs)";
    });

// The Angular app (npm install, then `npm run start` = ng serve on 4200, proxying /api and /hubs to 5080).
builder.AddJavaScriptApp("web", "../../web", "start")
    .WithNpm(install: true)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Dashboard");

builder.Build().Run();
