const eurCompact = new Intl.NumberFormat('en-IE', {
  style: 'currency',
  currency: 'EUR',
  notation: 'compact',
  maximumFractionDigits: 1,
});
const eurKpi = new Intl.NumberFormat('en-IE', {
  style: 'currency',
  currency: 'EUR',
  notation: 'compact',
  maximumFractionDigits: 2,
});
const eurFull = new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 });
const pct = new Intl.NumberFormat('en-IE', { style: 'percent', maximumFractionDigits: 1 });
const int = new Intl.NumberFormat('en-IE');

export const formatEur = (v: number) => eurCompact.format(v);
/** Headline figures: one more digit so €3.02B doesn't read as €3B. */
export const formatEurKpi = (v: number) => eurKpi.format(v);
export const formatEurFull = (v: number) => eurFull.format(v);
export const formatPct = (v: number) => pct.format(v);
export const formatInt = (v: number) => int.format(v);

/** "MaterialHandling" → "Material handling" */
export const humanize = (s: string) =>
  s.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ (\w)/g, (m) => m.toLowerCase());
