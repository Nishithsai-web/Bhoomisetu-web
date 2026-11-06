import type { ReactNode } from "react";

/**
 * One form field with its label, a plain-language hint and its error, wired for screen readers.
 * Every form in the app uses this, so a change to how fields look or read is made in one place.
 * Field errors are not live regions: the form's ErrorSummary announces once, then focus moves here.
 */
export function Field({
  id,
  label,
  hint,
  error,
  requiredMark,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  /** Text such as "(required)" shown after the label; omit for optional fields. */
  requiredMark?: string;
  children: (describedBy: string | undefined, invalid: boolean) => ReactNode;
}) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;

  return (
    <div className={error ? "field has-error" : "field"}>
      <label htmlFor={id}>
        {label}
        {requiredMark && <span className="required"> {requiredMark}</span>}
      </label>
      {hint && (
        <p id={hintId} className="hint">
          {hint}
        </p>
      )}
      {children(describedBy, Boolean(error))}
      {error && (
        <p id={errorId} className="error">
          {error}
        </p>
      )}
    </div>
  );
}
