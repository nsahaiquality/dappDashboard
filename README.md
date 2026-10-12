# dappDashboard: Asset Portfolio Dashboard

> **Disclaimer**
> This repository is a personal learning and preparation exercise. It has **no relation to any
> business, company, client or organisation**. All data is synthetic and randomly generated; all
> company, vendor, customer and manufacturer names are fictional. Nothing here reflects the
> systems, data, methods or opinions of any real organisation.

A real-time dashboard for an equipment-finance asset portfolio. It shows the exposure and the
collateral value behind it, delinquency, and how much is recovered when repossessed assets are
sold at auction. The stack is **.NET 10 + ASP.NET Core + SignalR**, **PostgreSQL** and **Angular**.

## What it shows

- **Global filters:** asset class, country, vendor, product and "underwater only". They drive
  every widget, and clicking a class, vendor or country in a chart applies that filter.
- **Headline KPIs:** total exposure, collateral market value and forced sale value, loan-to-value,
  exposure 30+ days past due, shortfall if everything were liquidated now, and recovery rate.
- **Breakdowns:** exposure vs collateral by asset class, exposure by delinquency bucket, collateral
  by country, top vendors, the remarketing pipeline (repossessed → listed → sold) and recovery
  breakdown, and a refinancing watchlist of asset classes with LTV above 100%.
- **Live event feed:** revaluations, payments, missed payments, defaults, repossessions, auction
  sales and market moves, pushed over SignalR as they happen.
- **Asset explorer:** a searchable list that can be sorted by value, LTV or days past due, with
  CSV export and a side panel comparing each asset's valuation history to its share of the exposure.

## Repository layout

```
src/
  AssetDashboard.Domain/          Entities, enums, valuation + amortisation maths (no dependencies)
  AssetDashboard.Infrastructure/  EF Core (Postgres), migrations, synthetic data generator,
                                  statistics queries, market simulator
  AssetDashboard.Api/             ASP.NET Core minimal API, SignalR hub, snapshot broadcaster
  AssetDashboard.AppHost/         .NET Aspire: runs Postgres + API + Angular, dashboard with traces/metrics/logs
  AssetDashboard.ServiceDefaults/ OpenTelemetry, health checks (/health, /alive), resilience
tests/
  AssetDashboard.Tests/           xUnit unit tests (domain maths, generator realism, filters)
  AssetDashboard.IntegrationTests/ Real API + Postgres via Testcontainers (every query, hub, simulator)
  AssetDashboard.EndToEndTests/   Whole system (Aspire) in a real browser (Playwright): journeys, numbers vs DB, real time, WCAG AA
web/                              Angular 22 frontend (signals, zoneless, router, Chart.js, @microsoft/signalr, IBM Plex)
docs/                             Architecture, backend and frontend guides, domain model, ADRs
backlog/                          Jira import file (epics + tasks)
docker-compose.yml                PostgreSQL (+ optional pgAdmin)
```

## Prerequisites (macOS)

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.x | https://dotnet.microsoft.com/download |
| Node.js | 22+ | `brew install node` |
| Docker runtime | any (Docker Desktop puts its CLI in `~/.docker/bin`; open a new terminal if `docker` isn't found) | [Docker Desktop](https://www.docker.com/products/docker-desktop/), [OrbStack](https://orbstack.dev) or `brew install colima docker docker-compose && colima start` |

## Running locally

### Everything at once (recommended): .NET Aspire

```bash
dotnet run --project src/AssetDashboard.AppHost
```

This starts PostgreSQL 17 (its own container and volume), the API (seeded on first start) and the
Angular app, and opens the **Aspire dashboard** at http://localhost:15101. There you can see every
resource, its logs, and live **traces** (one click from a snapshot recompute to its SQL queries)
and **metrics** (snapshot recompute time per filter, events, simulator steps, source loads). The
app is at http://localhost:4200 and the API docs at http://localhost:5080/swagger, as below.

### Step by step

```bash
# 1. Database
docker compose up -d

# 2. Backend: applies migrations and seeds about 20k contracts / 31k assets on first start
dotnet tool restore
dotnet run --project src/AssetDashboard.Api          # http://localhost:5080

# 3. Frontend: proxies /api and /hubs to the backend
cd web && npm install && npm start                   # http://localhost:4200
```

Useful endpoints (all documented, with try-it-out, at http://localhost:5080/swagger):

| Endpoint | Purpose |
|---|---|
| `GET /api/dashboard/snapshot?assetClass=&country=&vendorId=&productType=&underwaterOnly=` | Portfolio statistics for a filter |
| `GET /api/reference` | Vendors and countries for the filter dropdowns |
| `GET /api/assets?<filter>&status=&search=&sort=MarketValue\|Ltv\|DaysPastDue&page=&pageSize=` | Paged asset list |
| `GET /api/assets/export?<same query>` | CSV export (up to 100k rows) |
| `GET /api/assets/{id}` | Asset detail with valuation and exposure history |
| `GET /api/history?days=&assetClass=` | Daily portfolio history (24 months backfilled + live) |
| `GET /api/refinancing/summary?<filter>` | Requests by status, approval rate, expected loss by class, maturity wall |
| `GET /api/refinancing/requests?<filter>&status=` | Refinancing requests and decisions |
| `GET /api/refinancing/candidates?<filter>` | Contracts maturing, with balloon due, underwater or in arrears, with PD/LGD/EL |
| `GET /api/remarketing/summary?<filter>` | Pipeline, stock ageing, recovery by channel/class, costs, upcoming auctions |
| `GET /api/remarketing/cases?<filter>&status=&channel=` | Recovery cases with cost breakdown |
| `GET /api/remarketing/auctions?<filter>&status=` | Auctions with lots, hammer total, sell-through |
| `GET /api/vendors?<filter>&sort=Exposure\|DefaultRate\|Origination\|TargetAttainment` | Vendor performance |
| `GET /api/vendors/{id}` | Vendor detail: 24-month origination, mixes, delinquency, top customers |
| `GET /api/sources` / `GET /api/sources/{id}/runs` | Source systems with freshness, and their load runs |
| `POST /api/simulator/pause` / `resume` | Stop or start the live data simulator |
| `/hubs/dashboard` | SignalR hub: server sends `Snapshot` and `PortfolioEvent`; client calls `SetFilter(filter)` |
| `/swagger` | **Interactive API documentation** (Swagger UI; the API root redirects here). Set `ApiDocs:Enabled=false` to turn off |
| `/openapi/v1.json` | OpenAPI document behind the Swagger page |
| `/health` | Health check |

### Configuration (`src/AssetDashboard.Api/appsettings.Development.json`)

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:AssetDb` | local Docker Postgres | Database |
| `Database:MigrateOnStartup` | `true` | Apply EF migrations on start |
| `Seed:Contracts` | `20000` | Size of the synthetic portfolio; 1,000,000 seeds in a few minutes (binary COPY) |
| `Seed:RandomSeed` | `42` | Same seed gives the same portfolio |
| `Simulator:Enabled` / `EventsPerSecond` | `true` / `4` | Live change rate |
| `History:BackfillDays` / `CaptureIntervalMinutes` | `730` / `5` | Synthetic history length; how often today's row is refreshed |
| `Sources:Enabled` / `HistoryDays` | `true` / `30` | Simulated source-system loads |

To regenerate the data, drop the database (`docker compose down -v`) and restart the API. For a one-off large
portfolio: `Seed__Contracts=1000000 dotnet run --project src/AssetDashboard.Api` on an empty database.

## Development

```bash
dotnet test tests/AssetDashboard.Tests           # unit tests (no Docker needed)
dotnet test tests/AssetDashboard.IntegrationTests # API + queries against a throwaway Postgres (Docker required)
dotnet test tests/AssetDashboard.EndToEndTests   # whole system via Aspire + Playwright (Docker; stop any running AppHost first)
UPDATE_API_CONTRACT=1 dotnet test tests/AssetDashboard.IntegrationTests --filter ApiContractTests  # accept an API change
cd web && npm run api:generate                   # regenerate frontend types from contracts/openapi.v1.json
cd web && npm run test:ci                         # frontend unit tests (Vitest) with coverage
cd web && npx ng build                         # frontend build check
dotnet ef migrations add <Name> -p src/AssetDashboard.Infrastructure -s src/AssetDashboard.Infrastructure -o Data/Migrations
```

## Documentation

See [docs/README.md](docs/README.md) for the full map:
[architecture](docs/architecture.md) (C4 diagrams, live update flow),
[backend guide](docs/backend-guide.md), [frontend guide](docs/frontend-guide.md),
[domain model](docs/domain-model.md) and [decision records](docs/adr/README.md).
The API is documented interactively at `http://localhost:5080/swagger`.
