# 0002. SignalR with throttled snapshots, one group per filter

**Status:** Accepted (2026-10-09)

## Context
The dashboard must reflect portfolio changes in near real time. Changes can arrive in bursts
(a market move revalues thousands of assets). Users filter by asset class, country, vendor,
product and "underwater only", and each user should see figures for their own filter.

## Decision
- One SignalR hub. The server pushes two messages: `PortfolioEvent` (every change, small) and
  `Snapshot` (the complete set of figures for one filter).
- Snapshots are recomputed at most once per second, only when something changed (dirty flag),
  and at least every 30 seconds.
- Each connection joins the group of its filter (`SetFilter`); the broadcaster computes one
  snapshot per watched filter and sends it to that group.

## Consequences
- A burst of 1,000 changes costs one recompute per watched filter, not 1,000.
- Viewers with the same filter share one computation; cost grows with distinct filters, not viewers.
- Clients stay simple: the latest snapshot is the whole truth, with no delta merging.
- Recompute cost bounds the refresh rate on very large portfolios (see SCRUM-60).
- More than one API instance needs a backplane (Redis or Azure SignalR Service) and a single
  instance doing the recompute.

## Alternatives considered
- **Pushing deltas:** smaller messages, but every client must merge them correctly; fragile.
- **Polling from the browser:** simpler server, but seconds of delay and load per viewer.
- **Server-sent events:** one-way only; SignalR adds reconnects, groups and a typed hub.
