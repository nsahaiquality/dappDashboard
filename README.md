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

- **Headline KPIs:** total exposure, collateral market value and forced sale value, loan-to-value,
  exposure 30+ days past due, shortfall if everything were liquidated now, and recovery rate.
- **Breakdowns:** exposure vs collateral by asset class, exposure by delinquency bucket, collateral
  by country, top vendors, and the remarketing pipeline (repossessed → listed → sold).
- **Live event feed:** revaluations, payments, missed payments, defaults, repossessions, auction
  sales and market moves, pushed over SignalR as they happen.
- **Asset explorer:** a searchable, paged list of assets with each asset's valuation history.

## Repository layout

```
src/
  AssetDashboard.Domain/          Entities, enums, valuation + amortisation maths (no dependencies)
  AssetDashboard.Infrastructure/  EF Core (Postgres), migrations, synthetic data generator,
                                  statistics queries, market simulator
  AssetDashboard.Api/             ASP.NET Core minimal API, SignalR hub, snapshot broadcaster
tests/
  AssetDashboard.Tests/           xUnit tests (domain maths, generator realism)
web/                              Angular 22 frontend (signals, zoneless, Chart.js, @microsoft/signalr)
docs/                             Architecture and domain model
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

```bash
# 1. Database
docker compose up -d

# 2. Backend: applies migrations and seeds about 20k contracts / 31k assets on first start
dotnet tool restore
dotnet run --project src/AssetDashboard.Api          # http://localhost:5080

# 3. Frontend: proxies /api and /hubs to the backend
cd web && npm install && npm start                   # http://localhost:4200
```

Useful endpoints:

| Endpoint | Purpose |
|---|---|
| `GET /api/dashboard/snapshot` | Current portfolio statistics |
| `GET /api/assets?assetClass=&status=&search=&page=&pageSize=` | Paged asset list |
| `GET /api/assets/{id}` | Asset detail with valuation history |
| `POST /api/simulator/pause` / `resume` | Stop or start the live data simulator |
| `/hubs/dashboard` | SignalR hub (`Snapshot`, `PortfolioEvent` messages) |
| `/openapi/v1.json` | OpenAPI document (Development) |
| `/health` | Health check |

### Configuration (`src/AssetDashboard.Api/appsettings.Development.json`)

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:AssetDb` | local Docker Postgres | Database |
| `Database:MigrateOnStartup` | `true` | Apply EF migrations on start |
| `Seed:Contracts` | `20000` | Size of the synthetic portfolio (try 200000 for scale tests) |
| `Seed:RandomSeed` | `42` | Same seed gives the same portfolio |
| `Simulator:Enabled` / `EventsPerSecond` | `true` / `4` | Live change rate |

To regenerate the data, drop the database (`docker compose down -v`) and restart the API.

## Development

```bash
dotnet test                                    # backend tests
cd web && npx ng build                         # frontend build check
dotnet ef migrations add <Name> -p src/AssetDashboard.Infrastructure -s src/AssetDashboard.Infrastructure -o Data/Migrations
```

See [docs/architecture.md](docs/architecture.md) and [docs/domain-model.md](docs/domain-model.md).
