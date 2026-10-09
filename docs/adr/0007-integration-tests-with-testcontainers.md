# 0007. Integration tests against real PostgreSQL with Testcontainers

**Status:** Accepted (2026-10-09)

## Context
Several EF Core queries compiled but failed at runtime against PostgreSQL (sorting on projected
records). An in-memory database would not have caught them.

## Decision
`tests/AssetDashboard.IntegrationTests` starts a throwaway `postgres:17-alpine` container per test
collection (Testcontainers), boots the real API with `WebApplicationFactory` (migrations, seeded
data), and calls every endpoint, the SignalR hub and every simulator step. Tests that change data
use their own database.

## Consequences
- Query translation errors fail the build, not the dashboard; two were caught this way.
- The suite runs in about 10 seconds but needs Docker locally and in CI.
- The same harness checks the API documentation (every endpoint described).

## Alternatives considered
- **EF Core in-memory provider:** fast but does not translate SQL; misses the real failures.
- **A shared test database:** tests interfere with each other and depend on its state.
