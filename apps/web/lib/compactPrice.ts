// Short prices for map tags, the way they are said aloud: ₹45L, ₹1.2Cr, ₹18k.

export function compactRupees(amount: number): string {
  const trim = (value: number) => (Math.round(value * 10) / 10).toString();
  // Compare after rounding, so 99,950 is "₹1L" (not "₹100k") and 99,99,999 is "₹1Cr" (not "₹100L").
  const r = (value: number) => Math.round(value * 10) / 10;
  if (r(amount / 10_000_000) >= 1) return `₹${trim(amount / 10_000_000)}Cr`;
  if (r(amount / 100_000) >= 1) return `₹${trim(amount / 100_000)}L`;
  if (amount >= 1_000) return `₹${trim(amount / 1_000)}k`;
  return `₹${Math.round(amount)}`;
}

/** True when the listing is newer than the given number of days. */
export function isNew(createdAt: string | null | undefined, now: Date, days = 7): boolean {
  if (!createdAt) return false;
  const created = Date.parse(createdAt);
  return Number.isFinite(created) && now.getTime() - created < days * 24 * 60 * 60 * 1000;
}
