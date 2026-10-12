# Backend guide

A walkthrough of the .NET code: where things live, how a request is served, how the live
pipeline works, and the conventions that keep it correct. Read
[architecture.md](architecture.md) first for the big picture.

## Solution layout

```
AssetDashboard.slnx
src/
  AssetDashboard.Domain/            pure model and rules, no dependencies
    Entities.cs                     Vendor, Customer, Contract, Asset, AssetValuation, RemarketingCase,
                                    Auction, RefinancingRequest, PortfolioHistoryPoint, SourceSystem, LoadRun
    Enums.cs                        AssetClass, ContractStatus, AssetStatus, RemarketingChannel, ...
    ValuationModel.cs               depreciation curves → market and forced sale value
    Amortization.cs                 annuity maths: installment, outstanding balance
    CreditRisk.cs                   PD, LGD, expected loss; RefinancingPolicy (credit decision)
    RemarketingRules.cs             storage rates, selling fees, hammer price, sale costs
  AssetDashboard.Infrastructure/    everything that touches the database or runs in the background
    Data/                           AssetDbContext, design-time factory, Migrations/
    Dashboard/                      PortfolioFilter, PortfolioStatsService, DTOs
    History/                        PortfolioHistoryService, HistoryRecorder
    Refinancing/                    RefinancingService
    Remarketing/                    RemarketingService
    Vendors/                        VendorService
    Sources/                        SourceService, SourceFeedSimulator, SourceCatalog
    Simulation/                     MarketSimulator, IPortfolioEventSink
    Seeding/                        AssetCatalog, PortfolioGenerator, PortfolioSeeder, BulkCopy
  AssetDashboard.AppHost/           .NET Aspire orchestration: Postgres, API, Angular; dashboard
  AssetDashboard.ServiceDefaults/   OpenTelemetry, health checks, resilience (used by the API)
  AssetDashboard.Api/               transport and hosting only
    Program.cs                      dependency injection, startup, API docs, routes
    Endpoints.cs                    /api/* grouped by area, with OpenAPI metadata
    RealTime/                       DashboardHub, SnapshotBroadcaster, SignalREventSink
tests/
  AssetDashboard.Tests/             unit tests (no database)
  AssetDashboard.IntegrationTests/  real API + throwaway Postgres (Testcontainers)
```

## Startup: what `Program.cs` does, in order

1. **Adds the Aspire service defaults** (`AddServiceDefaults`): OpenTelemetry, health checks,
   service discovery, HTTP resilience.
2. **Registers services.** `AssetDbContext` (Npgsql), one scoped read service per screen, the
   options classes (`Seed`, `Simulator`, `History`, `Sources`), and four background services:
   `SnapshotBroadcaster`, `MarketSimulator`, `HistoryRecorder`, `SourceFeedSimulator`.
   `SnapshotBroadcaster`, `SignalREventSink` and `MarketSimulator` are singletons registered both
   as themselves and as hosted services, so the hub and endpoints can call them directly.
3. **Configures JSON.** Enums travel as strings (`JsonStringEnumConverter`) over HTTP and SignalR;
   numbers are strict, so OpenAPI documents them as numbers.
4. **Prepares the database** when `Database:MigrateOnStartup` is true (Development): applies
   migrations, seeds an empty database (`PortfolioSeeder`), backfills history and seeds the source
   systems. Each step does nothing if its data already exists.
5. **Maps the API documentation** (`/openapi/v1.json`, `/swagger`, `/` redirects) unless
   `ApiDocs:Enabled` is false.
6. **Maps routes:** `/health` and `/alive` (`MapDefaultEndpoints`), `/api/*` (`Endpoints.MapDashboardApi`), `/hubs/dashboard`.

Configuration lives in `appsettings.Development.json`; every key is listed in the
[README](../README.md#configuration-srcassetdashboardapiappsettingsdevelopmentjson). Override any
key with an environment variable, e.g. `Seed__Contracts=1000000`.

## Domain: the rules

Everything in `AssetDashboard.Domain` is a plain class or a static function of its inputs. That
makes the business rules readable in one place and testable without a database
([ADR 0005](adr/0005-domain-rules-as-pure-functions.md)).

| Rule | Where | What it decides |
|---|---|---|
| Asset value | `ValuationModel.MarketValue` | Declining balance per class, first-year drop, condition, market index; never above list price |
| Forced sale value | `ValuationModel.ForcedSaleValue` | Class haircut for quick liquidation (35% for IT to 75% for agriculture) |
| Repayments | `Amortization` | Installment with balloon, balance after n payments, interest part |
| Credit risk | `CreditRisk` | PD by grade with arrears uplift; LGD from net forced sale value; EL = PD × LGD × EAD |
| Refinancing | `RefinancingPolicy.Decide` | Decline above 110% LTV, grade 9+, rate cuts above grade 4, defaults; otherwise approve with a priced add-on |
| Remarketing | `RemarketingRules` | Storage per day by class, seller fee by channel, hammer price from bids, sale-cost breakdown |

## Infrastructure: data access

### The EF Core model

`AssetDbContext` sets two conventions for the whole model: money is `numeric(18,2)` and every
enum is stored as a readable string (`varchar(32)`). Per-entity configuration adds lengths,
indexes (status, asset class, days past due, maturity date) and the unique "one history row per
day and class" index (`NULLS NOT DISTINCT`).

Change the model, then add a migration:

```bash
dotnet ef migrations add <Name> -p src/AssetDashboard.Infrastructure -s src/AssetDashboard.Infrastructure -o Data/Migrations
```

The design-time factory (`AssetDbContextFactory`) lets `dotnet ef` build the model without
starting the API. Migrations that add a non-null column to existing rows must fill it (see
`*_Refinancing.cs`, which computes `MaturityDate` with SQL).

### The filter

`PortfolioFilter` is the one filter every screen shares: asset class, country, vendor, product
type, "underwater only". It has an `Apply` overload per entity (contracts, assets, remarketing
cases), so each service filters with one call and filters mean the same everywhere. Its `Key`
names the SignalR group of everyone watching that filter. Over HTTP it binds from the query string
through `Endpoints.FilterQuery`.

### Read services

Each screen has one service (`PortfolioStatsService`, `PortfolioHistoryService`,
`RefinancingService`, `RemarketingService`, `VendorService`, `SourceService`). They share a pattern:

- `AsNoTracking()` queries, filtered with `PortfolioFilter.Apply`.
- Aggregates are computed in SQL with `GroupBy` + `Sum`/`Count`; a handful of grouped queries are
  joined in memory when that is simpler than one large query (e.g. `VendorService`).
- Lists are paged on the server (`page`, `pageSize` clamped to 200) and return `PagedResult<T>`.
- Results are immutable records (DTOs) declared next to the service; they are the API contract.

### Rules that keep EF Core queries translatable

The integration tests exist because these mistakes compile but fail at runtime:

| Do | Don't | Why |
|---|---|---|
| Sort the entity (or an anonymous type), then `Select` into the record | `Select(x => new Dto(...)).OrderBy(d => d.Field)` | EF Core cannot sort on a member of a constructed record |
| `g.Where(pred).Sum(...)` inside a group | Client-side loops over groups | Translates to `SUM(CASE WHEN ...)` |
| Ternaries for lookups (`grade == 1 ? 0.003m : ...`) | Calling a C# helper inside the query | A method call cannot be translated; a ternary becomes `CASE` |
| `DateOnly.DayNumber` differences for day counts | `TimeSpan` arithmetic on `DateOnly` | `DayNumber` translates; `TimeSpan` on dates does not |
| `(decimal?)null` in conditional projections | bare `null` | Gives the expression a type |

### Seeding and generators

`PortfolioGenerator` builds a deterministic portfolio from a seed (same seed, same data). It works
in three steps so large portfolios can stream: `CreateParties` (vendors, customers),
`CreateContracts` (yields contracts with assets, valuations, recoveries and refinancing requests)
and `ApplyVolumeTargets`. `PortfolioSeeder` writes chunks of 10,000 contracts with `BulkCopy`, a
model-driven binary `COPY` writer, then resets the identity sequences
([ADR 0008](adr/0008-bulk-seeding-with-copy.md)). `AssetCatalog` holds the reference data: asset
classes, categories, price ranges, fictional manufacturers, countries and cities.

### Background services

| Service | What it does | Cadence |
|---|---|---|
| `MarketSimulator` | One random change per tick: revaluation, a credit step (payment, missed payment, repossession, listing or private sale, by the contract's state), new business, auction run, price cut, refinancing request or decision, market shock. Publishes a `PortfolioEvent` ([details](domain-model.md#simulator)) | `Simulator:EventsPerSecond` (4) |
| `SourceFeedSimulator` | Runs each source system's load when its interval has passed; publishes failures and recoveries | every 30 s |
| `HistoryRecorder` | Rewrites today's history rows from the live portfolio | every 5 min |
| `SnapshotBroadcaster` | Recomputes snapshots for watched filters when the portfolio changed | every 1 s |

`MarketSimulator.StepAsync(db, step)` runs one specific step, so tests can exercise each one
against a real database; `NextEventAsync(db)` runs one step chosen the way the live loop chooses.

## Api: transport

### Endpoints

`Endpoints.cs` maps one route group per area (`Dashboard`, `Assets`, `History`, `Refinancing`,
`Remarketing`, `Vendors`, `Data sources`, `Reference data`, `Simulator`). Every endpoint declares
a summary and a description, and every parameter a `[Description]`; these feed the Swagger page,
and an integration test fails if one is missing. Endpoints stay thin: bind parameters, call one
service method, return its record (or `404`).

### Real time

| Class | Role |
|---|---|
| `DashboardHub` | SignalR hub at `/hubs/dashboard`. On connect: joins the unfiltered group, sends the latest snapshot and the last 50 events. `SetFilter(filter)`: moves the connection to that filter's group and replies with its snapshot |
| `SnapshotBroadcaster` | Keeps one entry per watched filter (with a watcher count). Each second, if something changed or a snapshot is older than 30 s, recomputes and sends it to that filter's group. Drops filters nobody watches |
| `SignalREventSink` | The API's implementation of `IPortfolioEventSink`: keeps the last 50 events, broadcasts each to all clients and marks the broadcaster dirty |

The simulators only know `IPortfolioEventSink`. A future change feed from the client's systems
publishes through the same interface ([architecture.md](architecture.md#from-simulator-to-real-data)).

## Conventions

- **Records for DTOs, one file per area.** XML summaries on public types appear in the Swagger
  schemas.
- **Deterministic randomness** in generators (`new Random(seed)`); simulators are unseeded unless
  `Simulator:RandomSeed` is set (tests set it).
- **Culture-invariant text** for event messages (`CultureInfo("en-IE")`), so the server's locale
  never changes what users read.
- **Money** is `decimal`; ratios are rounded to 4 decimals at the edge (`Ratio`).
- **Enums as strings** everywhere (database, JSON, OpenAPI).
- **No business rule in a service or endpoint**: add it to the domain and unit-test it there.

## Recipes

### Add an endpoint for a new view

1. Put any new business rule in `AssetDashboard.Domain` with unit tests.
2. Create `Infrastructure/<Area>/<Area>Service.cs` with its result records; filter with
   `PortfolioFilter.Apply`; follow the EF Core rules above.
3. Register the service in `Program.cs` (`AddScoped`).
4. Add a `Map<Area>` group in `Endpoints.cs` with tag, summary, description and parameter
   descriptions.
5. Add an integration test class in `tests/AssetDashboard.IntegrationTests` (filter matrix,
   invariants, reconciliation with hand-written SQL). The documentation tests check the rest.
6. The contract test now fails: run the tests with `UPDATE_API_CONTRACT=1`, regenerate the
   frontend types (`cd web && npm run api:generate`) and commit both
   ([frontend-guide.md](frontend-guide.md#data-contracts)).

### Add a field or entity

1. Change `Entities.cs` (and `AssetDbContext.OnModelCreating` for lengths and indexes).
2. Fill it in `PortfolioGenerator` (seeded data) and wherever the simulator changes it.
3. Add a migration (command above); fill existing rows in the migration if needed.
4. `BulkCopy` picks up the new column automatically from the EF model.

### Add a live event type

1. Add the value to `PortfolioEventType` (`Dashboard/DashboardDtos.cs`).
2. Publish it from the simulator or feed through `IPortfolioEventSink`.
3. Update the contract and regenerate the frontend types, then add the overview's label and
   category ([frontend-guide.md](frontend-guide.md#add-a-live-event-type)). Unknown types still
   render with a neutral label.

## Running and debugging

```bash
dotnet run --project src/AssetDashboard.AppHost       # everything + Aspire dashboard (localhost:15101)

docker compose up -d                                   # Postgres
dotnet run --project src/AssetDashboard.Api            # API on :5080, Swagger at /swagger
dotnet test tests/AssetDashboard.Tests                 # unit tests
dotnet test tests/AssetDashboard.IntegrationTests      # needs Docker
```

To start from a fresh database: `docker compose down -v` (or remove the `assetdash-aspire-pgdata17`
volume when using Aspire), then run again. Pause the simulator from the dashboard or with
`POST /api/simulator/pause` when debugging a query.

**Finding a slow query:** run under Aspire, open Traces, pick a `snapshot.recompute` (or any
request) and expand it: each SQL statement is a child span with its duration and text. Custom
spans and metrics are defined in `Infrastructure/AssetDashboardTelemetry.cs`.
