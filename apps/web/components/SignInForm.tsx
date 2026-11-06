"use client";

import { useActionState } from "react";
import { signInAction, type FormState } from "@/app/[locale]/actions";
import { Field } from "./Field";

type Labels = {
  phone: string;
  phoneHint: string;
  sendCode: string;
  code: string;
  codeHint: string;
  verify: string;
  changeNumber: string;
  resend: string;
  privacy: string;
  sending: string;
};

/** Phone number, then code, driven by one server action so the step always matches the server's answer. */
export function SignInForm({ locale, next, labels, errors }: { locale: string; next: string; labels: Labels; errors: Record<string, string> }) {
  const [state, action, pending] = useActionState<FormState, FormData>(signInAction, { step: "phone" });
  const phone = state.phone ?? "";
  const error = state.error ? (Object.hasOwn(errors, state.error) ? errors[state.error] : errors.generic) : undefined;

  // Field errors are not live regions, so the sign-in error is announced here once.
  const announcement = error ? (
    <p role="alert" className="visually-hidden">
      {error}
    </p>
  ) : null;

  const hidden = (
    <>
      {announcement}
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="next" value={next} />
    </>
  );

  if (state.step !== "code") {
    return (
      <form action={action} className="card form" noValidate>
        {hidden}
        <input type="hidden" name="intent" value="request" />
        <Field id="phone" label={labels.phone} hint={labels.phoneHint} error={error}>
          {(describedBy, invalid) => (
            <input
              id="phone"
              name="phone"
              type="tel"
              inputMode="tel"
              autoComplete="tel-national"
              defaultValue={phone}
              required
              aria-describedby={describedBy}
              aria-invalid={invalid}
            />
          )}
        </Field>
        <button type="submit" disabled={pending}>
          {pending ? labels.sending : labels.sendCode}
        </button>
        <p className="hint">{labels.privacy}</p>
      </form>
    );
  }

  return (
    <div className="card form">
      <form action={action} noValidate>
        {hidden}
        <input type="hidden" name="intent" value="verify" />
        <input type="hidden" name="phone" value={phone} />
        <Field id="code" label={labels.code} hint={labels.codeHint.replace("{phone}", phone)} error={error}>
          {(describedBy, invalid) => (
            <input
              id="code"
              name="code"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={12}
              required
              autoFocus
              aria-describedby={describedBy}
              aria-invalid={invalid}
            />
          )}
        </Field>
        <button type="submit" disabled={pending}>
          {pending ? labels.sending : labels.verify}
        </button>
      </form>
      <form action={action} className="form-inline">
        {hidden}
        <input type="hidden" name="intent" value="request" />
        <input type="hidden" name="phone" value={phone} />
        <button type="submit" className="secondary" disabled={pending}>
          {labels.resend}
        </button>
      </form>
      <p>
        <a href={`/${locale}/signin?next=${encodeURIComponent(next)}`}>{labels.changeNumber}</a>
      </p>
    </div>
  );
}
