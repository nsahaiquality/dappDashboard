# 0005. Business rules as pure functions in the domain project

**Status:** Accepted (2026-10-09)

## Context
The figures management sees (asset values, repayments, expected loss, refinancing decisions,
recovery costs) depend on business rules that must be easy to read, review and test.

## Decision
All business rules live in `AssetDashboard.Domain` as static functions of their inputs:
`ValuationModel`, `Amortization`, `CreditRisk`, `RefinancingPolicy`, `RemarketingRules`. The
domain project has no dependencies. Services and simulators call these rules; they contain none.

## Consequences
- Each rule is in one place and unit-tested in milliseconds without a database.
- A business expert can review a rule by reading one short file.
- Where a rule must run inside SQL for performance (PD in the refinancing summary), it is mirrored
  as a query expression; a test keeps both in agreement.

## Alternatives considered
- **Rules inside services or SQL:** faster to write, but scattered and hard to test.
- **A rules engine:** flexible, but heavy for a handful of formulas.
