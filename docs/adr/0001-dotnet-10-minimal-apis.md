# 0001. .NET 10 with ASP.NET Core minimal APIs

**Status:** Accepted (2026-10-09)

## Context
The client runs a Microsoft-centric estate and expects C# and .NET. Development happens on macOS.
The API is read-heavy: about twenty GET endpoints returning aggregates and paged lists.

## Decision
Use .NET 10 (long-term support until November 2028) and ASP.NET Core minimal APIs, grouped by
area in `Endpoints.cs`, with OpenAPI metadata on every endpoint.

## Consequences
- Cross-platform: the same code runs on macOS for development and on Windows or Linux in production.
- Minimal APIs keep each endpoint to a few lines (bind, call a service, return a record).
- OpenAPI and Swagger UI come from the endpoint metadata, with no hand-written spec.
- Larger APIs may want controllers or vertical-slice folders; switching is mechanical.

## Alternatives considered
- **.NET Framework 4.8:** Windows-only and in maintenance mode; rejected.
- **Controllers (MVC):** more ceremony for no benefit at this size.
