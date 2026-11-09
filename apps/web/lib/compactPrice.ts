// Short prices for map tags, the way they are said aloud: ₹45L, ₹1.2Cr, ₹18k.

export function compactRupees(amount: number): string {
  const trim = (value: number) => (Math.round(value * 10) / 10).toString();
  // Move up a unit only when the smaller one would round to 100: 99,950 is "₹1L", 95,000 stays "₹95k".
  const r = (value: number) => Math.round(value * 10) / 10;
  if (amount >= 10_000_000 || r(amount / 100_000) >= 100) return `₹${trim(amount / 10_000_000)}Cr`;
  if (amount >= 100_000 || r(amount / 1_000) >= 100) return `₹${trim(amount / 100_000)}L`;
  if (amount >= 1_000) return `₹${trim(amount / 1_000)}k`;
  return `₹${Math.round(amount)}`;
}

/** True when the listing is newer than the given number of days. */
export function isNew(createdAt: string | null | undefined, now: Date, days = 7): boolean {
  if (!createdAt) return false;
  const created = Date.parse(createdAt);
  return Number.isFinite(created) && now.getTime() - created < days * 24 * 60 * 60 * 1000;
}
