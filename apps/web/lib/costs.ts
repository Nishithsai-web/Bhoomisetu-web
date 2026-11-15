// Buying costs on top of the agreed price: stamp duty, transfer duty, registration fee, and an optional home-loan EMI.
// Rates are the figures commonly cited for 2026 (sources listed beside each state). They change, so the page always
// tells the buyer to confirm at the state registration portal before paying.

export type StateCode = "TG" | "AP";
export type AreaKind = "urban" | "rural";

export type Duty = {
  key: "stamp" | "transfer" | "registration";
  percent: number;
  amount: number;
};

export type Loan = { amount: number; annualRatePercent: number; years: number };

export type BuyingCosts = {
  price: number;
  duties: Duty[];
  dutiesTotal: number;
  upfrontTotal: number;
  loan: null | { emi: number; totalPaid: number; totalInterest: number; months: number };
};

type Rate = { key: Duty["key"]; percent: number };

/** Percent of the price, by state (and by urban or rural area for Telangana). */
export const RATES: Record<StateCode, Record<AreaKind | "all", Rate[]>> = {
  // Telangana: urban plots pay stamp 4% + transfer 1.5% + registration 0.5%; rural (gram panchayat) plots pay
  // stamp 5.5% + registration 2%. Source: https://homefirstindia.com/blog/article/stamp-duty-registration-charges-telangana
  TG: {
    urban: [
      { key: "stamp", percent: 4 },
      { key: "transfer", percent: 1.5 },
      { key: "registration", percent: 0.5 },
    ],
    rural: [
      { key: "stamp", percent: 5.5 },
      { key: "registration", percent: 2 },
    ],
    all: [],
  },
  // Andhra Pradesh: stamp 5% + registration 1% + transfer 1.5%.
  // Source: https://homefirstindia.com/blog/article/stamp-duty-registration-charges-andhra-pradesh
  AP: {
    urban: [],
    rural: [],
    all: [
      { key: "stamp", percent: 5 },
      { key: "transfer", percent: 1.5 },
      { key: "registration", percent: 1 },
    ],
  },
};

/** Monthly EMI on a reducing-balance loan. A zero rate is simple division. */
export function emi(principal: number, annualRatePercent: number, years: number): number {
  const months = Math.round(years * 12);
  const r = annualRatePercent / 12 / 100;
  if (r === 0) return principal / months;
  const growth = Math.pow(1 + r, months);
  return (principal * r * growth) / (growth - 1);
}

export function buyingCosts(price: number, state: StateCode, area: AreaKind, loan?: Loan): BuyingCosts {
  const rates = state === "TG" ? RATES.TG[area] : RATES.AP.all;
  const duties: Duty[] = rates.map((rate) => ({
    key: rate.key,
    percent: rate.percent,
    amount: Math.round((price * rate.percent) / 100),
  }));
  const dutiesTotal = duties.reduce((sum, duty) => sum + duty.amount, 0);

  let loanResult: BuyingCosts["loan"] = null;
  if (loan && loan.amount > 0 && loan.years > 0) {
    const months = Math.round(loan.years * 12);
    // The total comes from the exact EMI, so a zero-rate loan repays exactly the amount borrowed.
    const totalPaid = Math.round(emi(loan.amount, loan.annualRatePercent, loan.years) * months);
    const monthly = Math.round(emi(loan.amount, loan.annualRatePercent, loan.years));
    loanResult = { emi: monthly, totalPaid, totalInterest: Math.max(0, totalPaid - loan.amount), months };
  }

  return { price, duties, dutiesTotal, upfrontTotal: price + dutiesTotal, loan: loanResult };
}
