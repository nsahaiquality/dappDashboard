import { NO_FILTER, sameFilter } from './models';

describe('sameFilter', () => {
  it('treats missing and null fields as equal (the API omits or nulls unset filter fields)', () => {
    expect(sameFilter(NO_FILTER, { underwaterOnly: false })).toBe(true);
    expect(
      sameFilter({ ...NO_FILTER, country: 'NL' }, { country: 'NL', underwaterOnly: false }),
    ).toBe(true);
  });

  it('detects any difference', () => {
    expect(sameFilter(NO_FILTER, { ...NO_FILTER, assetClass: 'Healthcare' })).toBe(false);
    expect(sameFilter(NO_FILTER, { ...NO_FILTER, underwaterOnly: true })).toBe(false);
    expect(sameFilter({ ...NO_FILTER, vendorId: 7 }, { ...NO_FILTER, vendorId: 8 })).toBe(false);
  });
});
