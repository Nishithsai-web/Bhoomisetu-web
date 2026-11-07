"use server";

import { redirect } from "next/navigation";
import {
  createOffer,
  createParcel,
  decideChangeRequest,
  findUserByPhone,
  isUuid,
  mergeDuplicate,
  requestChange,
  requestOtp,
  setListerConfirmation,
  setOwnerOfRecord,
  signOut,
  updateMyOffer,
  verifyOtp,
  type MyOfferAction,
} from "@/lib/api";
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
    // Cloudflare Turnstile puts its token in this field when the widget is shown (BHOOMISETU_TURNSTILE_SITE_KEY).
    const sent = await requestOtp(phone, value(form, "cf-turnstile-response"));
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
  const token = await getSessionToken();
  // End the session on the server too; the cookie is cleared even if the API cannot be reached.
  if (token) await signOut(token);
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

/** Sends people whose session ended back to sign in, then to the page they were on. */
async function requireToken(locale: string, back: string): Promise<string> {
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=${encodeURIComponent(back)}`);
  return token;
}

/** The owner of record confirms, or removes confirmation from, whoever made an offer. */
export async function ownerConfirmAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const offerId = value(form, "offerId");
  if (!isUuid(parcelId) || !isUuid(offerId)) redirect(`/${locale}`);
  const back = `/${locale}/parcels/${parcelId}/compare`;
  const token = await requireToken(locale, back);
  const result = await setListerConfirmation(parcelId, offerId, value(form, "intent") === "confirm", token);
  redirect(result.ok ? `${back}?done=owner` : `${back}?error=${encodeURIComponent(result.code)}`);
}

// ---------------------------------------------------------------- reviewer console

const REVIEW = (locale: string) => `/${locale}/review`;

export async function decideChangeAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const requestId = value(form, "requestId");
  const decision = value(form, "decision");
  if (!isUuid(requestId) || (decision !== "approve" && decision !== "reject")) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await decideChangeRequest(requestId, decision, value(form, "note").slice(0, 1000), token);
  redirect(result.ok ? `${REVIEW(locale)}?done=decided` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

export async function mergeDuplicateAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await mergeDuplicate(parcelId, token);
  redirect(result.ok ? `${REVIEW(locale)}?done=merged` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

/** Finds the account by mobile number, then records it as owner of record, in one step for the reviewer. */
export async function setOwnerAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const phone = value(form, "phone");
  const evidence = value(form, "evidence");
  const errors: FieldError[] = [];
  if (!isUuid(parcelId)) errors.push({ field: "parcelId", code: "review.parcel_id_invalid" });
  if (phone === "") errors.push({ field: "phone", code: "phone.empty" });
  if (evidence.length < 3) errors.push({ field: "evidence", code: "admin.evidence_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  const token = await requireToken(locale, REVIEW(locale));
  const user = await findUserByPhone(phone, token);
  if (!user.ok) return { error: user.status === 404 ? "review.no_account" : user.code, values: echo(form) };
  const result = await setOwnerOfRecord(parcelId, user.data.id, evidence, token);
  if (!result.ok) return { error: result.code, values: echo(form) };
  redirect(`${REVIEW(locale)}?done=owner_set`);
}
