const eurCompact = new Intl.NumberFormat('en-IE', {
  style: 'currency',
  currency: 'EUR',
  notation: 'compact',
  maximumFractionDigits: 1,
});
// Three significant digits: €3.00B, €812M, €42.5M (never a bare "€3B").
const eurKpi = new Intl.NumberFormat('en-IE', {
  style: 'currency',
  currency: 'EUR',
  notation: 'compact',
  minimumSignificantDigits: 3,
  maximumSignificantDigits: 3,
});
const eurFull = new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 });
const pct = new Intl.NumberFormat('en-IE', { style: 'percent', maximumFractionDigits: 1 });
const int = new Intl.NumberFormat('en-IE');

export const formatEur = (v: number) => eurCompact.format(v);
/** Headline figures at a fixed precision. */
export const formatEurKpi = (v: number) => eurKpi.format(v);
export const formatEurFull = (v: number) => eurFull.format(v);
export const formatPct = (v: number) => pct.format(v);
export const formatInt = (v: number) => int.format(v);

/** "MaterialHandling" → "Material handling" */
export const humanize = (s: string) =>
  s.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ (\w)/g, (m) => m.toLowerCase());
