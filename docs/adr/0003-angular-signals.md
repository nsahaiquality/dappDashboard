# 0003. Angular with signals, zoneless

**Status:** Accepted (2026-10-09)

## Context
The frontend must be maintainable by an enterprise team and redraw efficiently as live data
arrives several times per second.

## Decision
Angular 22 with standalone components, signals for all state, zoneless change detection, and
lazy-loaded routes. Shared state lives in three root services: `FilterService`,
`DashboardHubService`, `ApiService`. Views derive from state with `computed`.

## Consequences
- Live data is a signal: a new snapshot redraws exactly the views that read it.
- No zone.js: smaller bundle and no hidden change-detection passes.
- Angular's structure (services, routing, testing builders) suits larger teams.
- Team members used to older Angular (NgModules, RxJS-heavy state) need a short introduction.

## Alternatives considered
- **React:** equally capable; Angular is more common in the client's sector and more prescriptive.
- **Blazor:** one language with the backend, but a smaller ecosystem for charts and dashboards.
