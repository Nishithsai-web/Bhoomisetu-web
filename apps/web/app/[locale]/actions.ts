"use server";

import { redirect } from "next/navigation";
import { createOffer, createParcel, isUuid, requestOtp, verifyOtp } from "@/lib/api";
import { parseOfferForm, parseParcelForm, safeNextPath, type FieldError } from "@/lib/forms";
import { isLocale } from "@/lib/i18n";
import { clearSession, getSessionToken, setSession } from "@/lib/session";

/** What a form shows after a submit: field errors, a form-level error code, or the next step. */
export type FormState = {
  errors?: FieldError[];
  error?: string;
  step?: "phone" | "code";
  phone?: string;
  values?: Record<string, string>;
};

const value = (form: FormData, name: string) => {
  const v = form.get(name);
  return typeof v === "string" ? v.trim() : "";
};

/** Keeps what the person typed, so a mistake never makes them fill the whole form again. */
const echo = (form: FormData): Record<string, string> => {
  const out: Record<string, string> = {};
  for (const [key, v] of form.entries()) {
    if (typeof v === "string" && !key.startsWith("$")) out[key] = v;
  }
  return out;
};

const localeOf = (form: FormData) => {
  const locale = value(form, "locale");
  return isLocale(locale) ? locale : "en";
};

/**
 * One action for both sign-in steps, so the screen always follows the latest server answer:
 * intent "request" sends a code and moves to the code step; intent "verify" checks it and signs in.
 */
export async function signInAction(previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const phone = value(form, "phone");

  if (value(form, "intent") !== "verify") {
    const sent = await requestOtp(phone);
    if (sent.ok) return { step: "code", phone };
    // "Send a new code" pressed too early: stay on the code step, the earlier code still works.
    const stay = previous.step === "code" && sent.code === "otp.too_soon";
    return { step: stay ? "code" : "phone", phone, error: sent.code };
  }

  const result = await verifyOtp(phone, value(form, "code"));
  if (!result.ok) {
    // A finished or locked code needs a fresh one, so send the person back to the phone step.
    const restart = ["otp.expired", "otp.no_active_code", "otp.too_many_attempts"].includes(result.code);
    return { step: restart ? "phone" : "code", phone, error: result.code };
  }
  await setSession(result.data.accessToken, result.data.expiresAt);
  redirect(safeNextPath(value(form, "next"), `/${locale}`));
}

export async function signOutAction(form: FormData): Promise<void> {
  await clearSession();
  redirect(`/${localeOf(form)}`);
}

export async function createParcelAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/sell`);

  const parsed = parseParcelForm(form);
  if (!parsed.ok) return { errors: parsed.errors, values: echo(form) };

  const result = await createParcel(parsed.value, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/sell`);
    }
    if (result.code === "parcel.already_exists" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(`/${locale}/parcels/${result.existingParcelId}/offer?existing=1`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/parcels/${result.data.id}/offer?new=1`);
}

export async function createOfferAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };

  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/offer`);

  const parsed = parseOfferForm(form);
  if (!parsed.ok) return { errors: parsed.errors, values: echo(form) };

  const result = await createOffer(parcelId, parsed.value, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/offer`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/parcels/${parcelId}/compare`);
}
