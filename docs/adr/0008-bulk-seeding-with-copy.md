# 0008. Bulk seeding with binary COPY

**Status:** Accepted (2026-10-09)

## Context
"The dataset is large": the dashboard must be proven at client scale (around 1 million
contracts). Seeding with EF Core `SaveChanges` took 13 s for 20,000 contracts and would take
hours at 1 million, with memory growing with the portfolio.

## Decision
Generate contracts as a stream, in chunks of 10,000, assign keys in the seeder, and write every
table with PostgreSQL binary `COPY` (`BulkCopy`), driven by the EF Core model (table and column
names, store types, value converters). Reset identity sequences and run `ANALYZE` afterwards.

## Consequences
- 1,000,000 contracts (1.57 million assets, 5.7 million valuations) in 2 min 20 s; 20,000 in 3 s.
- Memory stays flat whatever the size.
- New columns are picked up automatically from the model.
- PostgreSQL-specific; another database would need its own bulk path.

## Alternatives considered
- **EF Core batching:** simple but too slow and memory-hungry at scale.
- **Third-party bulk-extension libraries:** commercial licences or less control over keys.
