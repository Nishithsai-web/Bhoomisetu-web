import type { ReactNode } from "react";

/**
 * One form field with its label, a plain-language hint and its error, wired for screen readers.
 * Every form in the app uses this, so a change to how fields look or read is made in one place.
 */
export function Field({
  id,
  label,
  hint,
  error,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  children: (describedBy: string | undefined, invalid: boolean) => ReactNode;
}) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;

  return (
    <div className={error ? "field has-error" : "field"}>
      <label htmlFor={id}>{label}</label>
      {hint && (
        <p id={hintId} className="hint">
          {hint}
        </p>
      )}
      {children(describedBy, Boolean(error))}
      {error && (
        <p id={errorId} className="error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
