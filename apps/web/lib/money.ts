// Rupee amounts shown the way Indian buyers read them: grouped 12,34,567 and summarised in lakh or crore.

const LAKH = 100_000;
const CRORE = 10_000_000;

export type AmountWords = { lakh: string; crore: string };

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
  const number = (value: number) =>
    new Intl.NumberFormat(intlLocale, { maximumFractionDigits: 2 }).format(value);
  if (amount >= CRORE) {
    return `₹${number(amount / CRORE)} ${words.crore}`;
  }
  if (amount >= LAKH) {
    return `₹${number(amount / LAKH)} ${words.lakh}`;
  }
  return formatRupees(amount, intlLocale);
}

export function formatNumber(value: number, intlLocale: string, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat(intlLocale, { maximumFractionDigits }).format(value);
}
