import { formatEur, formatEurFull, formatEurKpi, formatInt, formatPct, humanize } from './format';

describe('format', () => {
  it('shows headline euros with three significant digits, never a bare "€3B"', () => {
    expect(formatEurKpi(3_000_000_000)).toBe('€3.00B');
    expect(formatEurKpi(3_024_000_000)).toBe('€3.02B');
    expect(formatEurKpi(246_580_000)).toBe('€247M');
  });

  it('formats compact and full euros, percentages and counts in en-IE', () => {
    expect(formatEur(1_250_000)).toBe('€1.3M');
    expect(formatEurFull(1_234_567.89)).toBe('€1,234,568');
    expect(formatPct(0.7391)).toBe('73.9%');
    expect(formatInt(19_808)).toBe('19,808');
  });

  it('turns enum names into readable labels', () => {
    expect(humanize('MaterialHandling')).toBe('Material handling');
    expect(humanize('InRemarketing')).toBe('In remarketing');
    expect(humanize('Loan')).toBe('Loan');
  });
});
