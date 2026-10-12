# 0006. Synthetic data and simulators until client access

**Status:** Accepted (2026-10-09)

## Context
Access to the client's asset database is not available yet, and real customer data must not be
used in a preparation exercise.

## Decision
Generate a deterministic, realistic vendor-finance portfolio (`PortfolioGenerator`, seeded) and
change it live with simulators (`MarketSimulator`, `SourceFeedSimulator`). The simulators publish
through `IPortfolioEventSink`, the same interface a real change feed will use.

## Consequences
- Screens, endpoints and tests can be built and demonstrated before access is granted.
- Same seed, same data: tests and demos are repeatable.
- Realism depends on assumptions (documented in `docs/domain-model.md`); they must be validated
  with domain experts once the client's data model is known (SCRUM-18).
- Replacing the simulators touches only the data feed, not screens or transport.

## Alternatives considered
- **Wait for client data:** blocks all work for weeks.
- **Static fixture files:** no live behaviour and no scale testing.
