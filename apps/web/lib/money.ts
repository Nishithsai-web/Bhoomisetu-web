// Rupee amounts shown the way Indian buyers read them: grouped 12,34,567 and summarised in lakh or crore.

const LAKH = 100_000;
const CRORE = 10_000_000;

/** Singular and plural words, because Telugu (లక్ష / లక్షలు) and English usage differ. */
export type AmountWords = { lakh: string; lakhs: string; crore: string; crores: string };

/** ₹ with Indian digit grouping, no decimals for amounts of a rupee or more. */
export function formatRupees(amount: number, intlLocale: string): string {
  return new Intl.NumberFormat(intlLocale, {
    style: "currency",
    currency: "INR",
    maximumFractionDigits: amount >= 1 ? 0 : 2,
  }).format(amount);
}

/** "₹1.25 crore" or "₹48.5 lakh"; plain rupees below one lakh. Words come from the dictionary. */
export function formatRupeesShort(amount: number, intlLocale: string, words: AmountWords): string {
  const round2 = (value: number) => Math.round(value * 100) / 100;
  const number = (value: number) => new Intl.NumberFormat(intlLocale, { maximumFractionDigits: 2 }).format(value);
  const lakhs = round2(amount / LAKH);
  // 99,99,999 rounds to 100 lakh, which is read as one crore.
  if (amount >= CRORE || lakhs >= 100) {
    const crores = round2(amount / CRORE);
    return `₹${number(crores)} ${crores === 1 ? words.crore : words.crores}`;
  }
  if (amount >= LAKH) {
    return `₹${number(lakhs)} ${lakhs === 1 ? words.lakh : words.lakhs}`;
  }
  return formatRupees(amount, intlLocale);
}

export function formatNumber(value: number, intlLocale: string, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat(intlLocale, { maximumFractionDigits }).format(value);
}
