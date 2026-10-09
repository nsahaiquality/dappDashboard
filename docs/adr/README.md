# Architecture decision records

Each record explains one significant decision: the situation, what was chosen, what it costs,
and what else was considered. Records are not edited after acceptance; a new record supersedes an
old one.

| # | Decision | Status |
|---|---|---|
| [0001](0001-dotnet-10-minimal-apis.md) | .NET 10 with ASP.NET Core minimal APIs | Accepted |
| [0002](0002-signalr-throttled-snapshots-per-filter.md) | SignalR with throttled snapshots, one group per filter | Accepted |
| [0003](0003-angular-signals.md) | Angular with signals, zoneless | Accepted |
| [0004](0004-postgresql-ef-core.md) | PostgreSQL with EF Core and migrations | Accepted |
| [0005](0005-domain-rules-as-pure-functions.md) | Business rules as pure functions in the domain project | Accepted |
| [0006](0006-synthetic-data-and-simulators.md) | Synthetic data and simulators until client access | Accepted |
| [0007](0007-integration-tests-with-testcontainers.md) | Integration tests against real PostgreSQL with Testcontainers | Accepted |
| [0008](0008-bulk-seeding-with-copy.md) | Bulk seeding with binary COPY | Accepted |

Template: Context · Decision · Consequences · Alternatives considered.
