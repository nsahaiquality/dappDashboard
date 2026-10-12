# 0004. PostgreSQL with EF Core and migrations

**Status:** Accepted (2026-10-09)

## Context
The dashboard needs a read model it can aggregate quickly. The client's source database is not
known yet.

## Decision
PostgreSQL 17 (Docker locally) accessed through EF Core 10 with the Npgsql provider. Schema
changes are EF migrations. Enums are stored as strings; money as `numeric(18,2)`.

## Consequences
- Strong SQL aggregation (`GROUP BY`, filtered aggregates, `NULLS NOT DISTINCT`) and binary `COPY`.
- Migrations give a reviewed history of every schema change.
- A few PostgreSQL-specific features are used (`ILIKE`, `NULLS NOT DISTINCT`, `COPY`); moving to
  SQL Server would need modest changes in those places.
- Some LINQ shapes do not translate; integration tests catch them (ADR 0007).

## Alternatives considered
- **SQL Server:** likely in the client estate; possible later. PostgreSQL runs natively on any
  laptop and in every cloud.
- **Dapper with hand-written SQL:** more control, but no migrations or model, and more code.
