"use client";

import { useId, useState } from "react";
import { AREA_UNITS, type AreaUnit, convertArea, isAreaUnit, parseAreaValue } from "@/lib/units";
import { formatNumber } from "@/lib/money";

type Labels = { value: string; unit: string; invalid: string; results: string; names: Record<AreaUnit, string> };

export function AreaConverter({ intlLocale, labels }: { intlLocale: string; labels: Labels }) {
  const [text, setText] = useState("200");
  const [unit, setUnit] = useState<AreaUnit>("square_yard");
  const valueId = useId();
  const unitId = useId();
  const errorId = useId();

  const value = parseAreaValue(text);
  const results = value === null ? null : convertArea(value, unit);

  return (
    <div className="converter card">
      <div className="converter-inputs">
        <div className="field">
          <label htmlFor={valueId}>{labels.value}</label>
          <input
            id={valueId}
            inputMode="decimal"
            value={text}
            onChange={(event) => setText(event.target.value)}
            aria-invalid={value === null}
            aria-describedby={value === null ? errorId : undefined}
          />
        </div>
        <div className="field">
          <label htmlFor={unitId}>{labels.unit}</label>
          <select
            id={unitId}
            value={unit}
            onChange={(event) => {
              if (isAreaUnit(event.target.value)) setUnit(event.target.value);
            }}
          >
            {AREA_UNITS.map((u) => (
              <option key={u} value={u}>
                {labels.names[u]}
              </option>
            ))}
          </select>
        </div>
      </div>

      {results === null ? (
        <p id={errorId} role="alert" className="error">
          {labels.invalid}
        </p>
      ) : (
        <>
          <h2>{labels.results}</h2>
          <dl className="unit-grid" aria-live="polite">
            {AREA_UNITS.map((u) => (
              <div key={u} className={u === unit ? "unit selected" : "unit"}>
                <dt>{labels.names[u]}</dt>
                <dd>{formatNumber(results[u], intlLocale, results[u] < 1 ? 6 : 4)}</dd>
              </div>
            ))}
          </dl>
        </>
      )}
    </div>
  );
}
