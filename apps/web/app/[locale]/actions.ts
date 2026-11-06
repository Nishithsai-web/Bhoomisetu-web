"use server";

import { redirect } from "next/navigation";
import { createOffer, createParcel, isUuid, requestChange, requestOtp, updateMyOffer, verifyOtp, type MyOfferAction } from "@/lib/api";
import { CHANGE_FIELDS, parseOfferForm, parseParcelForm, safeNextPath, toMetres, type FieldError } from "@/lib/forms";
import { parseAreaValue } from "@/lib/units";
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

  // People paste codes as "123 456"; spaces and dashes are not part of the code.
  const result = await verifyOtp(phone, value(form, "code").replace(/[\s-]/g, ""));
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

const MY_OFFER_ACTIONS: readonly MyOfferAction[] = ["still-available", "sold", "withdraw"];

/** One-tap updates on the My listings page. Plain form posts, so they work even before JavaScript loads. */
export async function myOfferAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const action = value(form, "action") as MyOfferAction;
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/mine`);
  if (!isUuid(parcelId) || !MY_OFFER_ACTIONS.includes(action)) redirect(`/${locale}/mine?error=api.error`);

  const result = await updateMyOffer(parcelId, action, token);
  if (!result.ok && result.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/mine`);
  }
  redirect(result.ok ? `/${locale}/mine?done=${action}` : `/${locale}/mine?error=${encodeURIComponent(result.code)}`);
}

export async function changeRequestAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/correct`);

  const field = value(form, "field");
  const reason = value(form, "reason");
  let proposed = value(form, "proposedValue");
  const errors: FieldError[] = [];
  if (!(CHANGE_FIELDS as readonly string[]).includes(field)) errors.push({ field: "field", code: "form.required" });
  if (proposed === "") errors.push({ field: "proposedValue", code: "form.required" });
  if (reason.length < 3) errors.push({ field: "reason", code: "change.reason_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  // Lengths may be typed in feet; the API stores metres.
  if (["frontage_m", "depth_m", "road_width_m"].includes(field) && value(form, "lengthUnit") === "feet") {
    const feet = parseAreaValue(proposed); // positive number; Telugu and Devanagari digits accepted
    if (feet === null) return { errors: [{ field: "proposedValue", code: "form.number_invalid" }], values: echo(form) };
    proposed = String(toMetres(feet, "feet"));
  }

  const result = await requestChange(parcelId, { field, proposedValue: proposed, reason }, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/correct`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/mine?done=change-requested`);
}
