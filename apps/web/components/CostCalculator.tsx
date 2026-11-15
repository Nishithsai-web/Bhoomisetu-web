"use client";

import { useId, useState } from "react";
import { type AreaKind, type StateCode, buyingCosts } from "@/lib/costs";
import { toAsciiDigits } from "@/lib/units";
import { formatNumber, formatRupees } from "@/lib/money";

export type CostLabels = {
  price: string;
  state: string;
  stateTG: string;
  stateAP: string;
  area: string;
  urban: string;
  rural: string;
  loanToggle: string;
  loanAmount: string;
  loanRate: string;
  loanYears: string;
  invalid: string;
  duties: string;
  stamp: string;
  transfer: string;
  registration: string;
  dutiesTotal: string;
  upfront: string;
  loanHeading: string;
  emi: string;
  totalInterest: string;
  totalPaid: string;
  liveSummary: string;
  sourceNote: string;
};

/** Accepts 50,00,000, 50 00 000 or Telugu/Devanagari digits. Empty means zero. */
function parseRupees(text: string): number | null {
  const cleaned = toAsciiDigits(text).replace(/[,\s₹]/g, "");
  if (cleaned === "") return 0;
  if (!/^\d+(\.\d+)?$/.test(cleaned)) return null;
  return Number(cleaned);
}

export function CostCalculator({ intlLocale, labels }: { intlLocale: string; labels: CostLabels }) {
  const [priceText, setPriceText] = useState("50,00,000");
  const [state, setState] = useState<StateCode>("TG");
  const [area, setArea] = useState<AreaKind>("urban");
  const [withLoan, setWithLoan] = useState(false);
  const [loanText, setLoanText] = useState("40,00,000");
  const [rateText, setRateText] = useState("8.5");
  const [yearsText, setYearsText] = useState("20");
  const priceId = useId();
  const stateId = useId();
  const areaId = useId();
  const loanToggleId = useId();
  const loanId = useId();
  const rateId = useId();
  const yearsId = useId();
  const errorId = useId();

  const price = parseRupees(priceText);
  const loanAmount = parseRupees(loanText);
  const rate = Number(toAsciiDigits(rateText).trim());
  const years = Number(toAsciiDigits(yearsText).trim());
  const loanValid = !withLoan || (loanAmount !== null && Number.isFinite(rate) && rate >= 0 && years > 0 && years <= 40);
  const valid = price !== null && price > 0 && loanValid;

  const costs = valid
    ? buyingCosts(
        price,
        state,
        area,
        withLoan && loanAmount !== null ? { amount: loanAmount, annualRatePercent: rate, years } : undefined,
      )
    : null;

  const names: Record<"stamp" | "transfer" | "registration", string> = {
    stamp: labels.stamp,
    transfer: labels.transfer,
    registration: labels.registration,
  };

  return (
    <div className="converter card">
      <div className="converter-inputs">
        <div className="field">
          <label htmlFor={priceId}>{labels.price}</label>
          <input id={priceId} inputMode="numeric" value={priceText} onChange={(e) => setPriceText(e.target.value)} aria-invalid={price === null || price === 0} aria-describedby={errorId} />
        </div>
        <div className="field">
          <label htmlFor={stateId}>{labels.state}</label>
          <select id={stateId} value={state} onChange={(e) => setState(e.target.value === "AP" ? "AP" : "TG")}>
            <option value="TG">{labels.stateTG}</option>
            <option value="AP">{labels.stateAP}</option>
          </select>
        </div>
        {state === "TG" && (
          <div className="field">
            <label htmlFor={areaId}>{labels.area}</label>
            <select id={areaId} value={area} onChange={(e) => setArea(e.target.value === "rural" ? "rural" : "urban")}>
              <option value="urban">{labels.urban}</option>
              <option value="rural">{labels.rural}</option>
            </select>
          </div>
        )}
      </div>

      <div className="field">
        <label htmlFor={loanToggleId}>
          <input id={loanToggleId} type="checkbox" checked={withLoan} onChange={(e) => setWithLoan(e.target.checked)} /> {labels.loanToggle}
        </label>
      </div>
      {withLoan && (
        <div className="converter-inputs">
          <div className="field">
            <label htmlFor={loanId}>{labels.loanAmount}</label>
            <input id={loanId} inputMode="numeric" value={loanText} onChange={(e) => setLoanText(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor={rateId}>{labels.loanRate}</label>
            <input id={rateId} inputMode="decimal" value={rateText} onChange={(e) => setRateText(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor={yearsId}>{labels.loanYears}</label>
            <input id={yearsId} inputMode="numeric" value={yearsText} onChange={(e) => setYearsText(e.target.value)} />
          </div>
        </div>
      )}

      {costs === null ? (
        <p id={errorId} role="alert" className="error">
          {labels.invalid}
        </p>
      ) : (
        <>
          <h2>{labels.duties}</h2>
          <div className="table-wrap"><table>
            <tbody>
              {costs.duties.map((duty) => (
                <tr key={duty.key}>
                  <th scope="row">
                    {names[duty.key]} ({formatNumber(duty.percent, intlLocale)}%)
                  </th>
                  <td>{formatRupees(duty.amount, intlLocale)}</td>
                </tr>
              ))}
              <tr>
                <th scope="row">{labels.dutiesTotal}</th>
                <td>{formatRupees(costs.dutiesTotal, intlLocale)}</td>
              </tr>
              <tr>
                <th scope="row">{labels.upfront}</th>
                <td>
                  <strong>{formatRupees(costs.upfrontTotal, intlLocale)}</strong>
                </td>
              </tr>
            </tbody>
          </table></div>

          {costs.loan && (
            <>
              <h2>{labels.loanHeading}</h2>
              <div className="table-wrap"><table>
                <tbody>
                  <tr>
                    <th scope="row">{labels.emi}</th>
                    <td>{formatRupees(costs.loan.emi, intlLocale)}</td>
                  </tr>
                  <tr>
                    <th scope="row">{labels.totalInterest}</th>
                    <td>{formatRupees(costs.loan.totalInterest, intlLocale)}</td>
                  </tr>
                  <tr>
                    <th scope="row">{labels.totalPaid}</th>
                    <td>{formatRupees(costs.loan.totalPaid, intlLocale)}</td>
                  </tr>
                </tbody>
              </table></div>
            </>
          )}

          <p className="visually-hidden" aria-live="polite">
            {labels.liveSummary.replace("{upfront}", formatRupees(costs.upfrontTotal, intlLocale))}
          </p>
        </>
      )}
      <p className="muted">{labels.sourceNote}</p>
    </div>
  );
}
