// Server-side client for the Bhoomisetu API. Types mirror the API's JSON (camelCase).

export type Sort = "best_value" | "lowest_price" | "best_verified" | "fastest_response";
export const SORTS: readonly Sort[] = ["best_value", "lowest_price", "best_verified", "fastest_response"];

export type Badge =
  | "best_value"
  | "lowest_price"
  | "verify_before_visiting"
  | "no_brokerage"
  | "availability_stale"
  | "authority_not_confirmed";

export type ComparedOffer = {
  rank: number;
  offerId: string;
  /** The lister's chosen display name, if any; otherwise show "Lister {listerNumber}" in the reader's language. */
  listerName: string | null;
  listerNumber: number;
  listerKind: "owner" | "agent";
  /** Says "owner" but is not yet the owner of record; priced and badged like an agent. */
  ownerClaimUnverified: boolean;
  authority: "owner_of_record" | "owner_confirmed_agent" | "not_confirmed";
  checks: ("phone_otp" | "identity" | "rera_agent")[];
  askingPriceRupees: number;
  buyerBrokeragePercent: number | null;
  brokerageAssumed: boolean;
  effectivePriceRupees: number;
  effectivePricePerSquareYardRupees: number;
  effectivePricePerAcreRupees: number;
  medianResponseMinutes: number | null;
  availabilityConfirmedAt: string;
  score: number;
  breakdown: { price: number; authority: number; checks: number; response: number; freshness: number };
  badges: Badge[];
};

export type Comparison = {
  parcelId: string;
  areaSquareYards: number;
  areaAcres: number;
  sort: Sort;
  medianEffectivePriceRupees: number | null;
  rules: {
    priceWeight: number;
    authorityWeight: number;
    checksWeight: number;
    responseWeight: number;
    freshnessWeight: number;
    lowPriceOutlierRatio: number;
    assumedUndisclosedBrokeragePercent: number;
    staleAfterDays: number;
  };
  offers: ComparedOffer[];
};

export type ParcelSummary = {
  id: string;
  stateCode: string;
  district: string;
  mandal: string;
  village: string;
  surveyNumber: string;
  landClass: string;
  areaSquareYards: number;
  activeOffers: number;
};

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number };

export type ApiProblem = {
  ok: false;
  status: number;
  code: string;
  /** Present on 409 parcel.already_exists. */
  existingParcelId?: string;
  /** Present on 429 OTP responses. */
  retryAfterSeconds?: number;
};

export type ApiResult<T> = { ok: true; data: T } | ApiProblem;

export function isSort(value: unknown): value is Sort {
  return typeof value === "string" && (SORTS as readonly string[]).includes(value);
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isUuid(value: string): boolean {
  return UUID.test(value);
}

function baseUrl(): string {
  const url = process.env.BHOOMISETU_API_URL;
  if (!url) {
    throw new Error("BHOOMISETU_API_URL is not set.");
  }
  return url.replace(/\/+$/, "");
}

async function request<T>(method: "GET" | "POST" | "DELETE", path: string, body?: unknown, token?: string): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { accept: "application/json" };
  if (body !== undefined) headers["content-type"] = "application/json";
  if (token) headers.authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${baseUrl()}${path}`, {
      method,
      cache: "no-store",
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(8000),
    });
  } catch (error) {
    console.error(`API request failed: ${method} ${path}`, error);
    return { ok: false, status: 503, code: "api.unreachable" };
  }

  if (!response.ok) {
    const problem: ApiProblem = { ok: false, status: response.status, code: response.status === 401 ? "auth.required" : "api.error" };
    try {
      const json = (await response.json()) as { code?: unknown; existingParcelId?: unknown; retryAfterSeconds?: unknown };
      if (typeof json.code === "string") problem.code = json.code;
      if (typeof json.existingParcelId === "string") problem.existingParcelId = json.existingParcelId;
      if (typeof json.retryAfterSeconds === "number") problem.retryAfterSeconds = json.retryAfterSeconds;
    } catch {
      // Body was not JSON (for example a bare 401); keep the status-based code.
    }
    return problem;
  }

  if (response.status === 202 || response.status === 204) {
    return { ok: true, data: undefined as T };
  }
  return { ok: true, data: (await response.json()) as T };
}

const get = <T,>(path: string, token?: string) => request<T>("GET", path, undefined, token);

export function getComparison(parcelId: string, sort: Sort): Promise<ApiResult<Comparison>> {
  return get<Comparison>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/comparison?sort=${sort}`);
}

/** Free-text search over village, mandal and district names. */
export function searchParcels(query: string, page: number): Promise<ApiResult<Paged<ParcelSummary>>> {
  const params = new URLSearchParams({ page: String(page), pageSize: "20" });
  if (query) params.set("q", query);
  return get<Paged<ParcelSummary>>(`/api/v1/parcels?${params.toString()}`);
}

export type SignIn = { accessToken: string; expiresAt: string; userId: string };

export function requestOtp(phone: string): Promise<ApiResult<void>> {
  return request<void>("POST", "/api/v1/auth/otp/request", { phone });
}

export function verifyOtp(phone: string, code: string): Promise<ApiResult<SignIn>> {
  return request<SignIn>("POST", "/api/v1/auth/otp/verify", { phone, code });
}

export type Me = { id: string; phoneMasked: string; displayName: string | null; preferredLocale: string; checks: string[]; isTrustReviewer: boolean };

export function getMe(token: string): Promise<ApiResult<Me>> {
  return get<Me>("/api/v1/me", token);
}

export type CreateParcel = {
  stateCode: string;
  district: string;
  mandal: string;
  village: string;
  lgdVillageCode: string | null;
  surveyNumber: string;
  landClass: string;
  area: { value: number; unit: string };
  areaSource: string;
  frontageM: number | null;
  depthM: number | null;
  facing: string | null;
  road: { widthM: number | null; widthSource: string | null; type: string | null; accessRight: string | null; distanceToMainRoadM: number | null } | null;
};

export function createParcel(body: CreateParcel, token: string): Promise<ApiResult<{ id: string }>> {
  return request<{ id: string }>("POST", "/api/v1/parcels", body, token);
}

export type CreateOffer = { listerKind: "owner" | "agent"; askingPriceRupees: number; buyerBrokeragePercent: number | null; brokerageTerms: string | null };

export function createOffer(parcelId: string, body: CreateOffer, token: string): Promise<ApiResult<{ id: string }>> {
  return request<{ id: string }>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/offers`, body, token);
}

export type MyOffer = {
  offerId: string;
  parcelId: string;
  village: string;
  mandal: string;
  district: string;
  surveyNumber: string;
  areaSquareYards: number;
  listerKind: "owner" | "agent";
  askingPriceRupees: number;
  buyerBrokeragePercent: number | null;
  status: "active" | "withdrawn" | "sold";
  availabilityConfirmedAt: string;
  daysSinceConfirmed: number;
  needsConfirmation: boolean;
};

export function getMyOffers(token: string): Promise<ApiResult<MyOffer[]>> {
  return get<MyOffer[]>("/api/v1/me/offers", token);
}

export type MyOfferAction = "still-available" | "sold" | "withdraw";

export function updateMyOffer(parcelId: string, action: MyOfferAction, token: string): Promise<ApiResult<void>> {
  return request<void>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/offers/mine/${action}`, undefined, token);
}

export type ChangeRequest = {
  id: string;
  parcelId: string;
  field: string;
  currentValue: string | null;
  proposedValue: string;
  reason: string;
  status: "pending" | "approved" | "rejected";
  createdAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
};

export function requestChange(parcelId: string, body: { field: string; proposedValue: string; reason: string }, token: string): Promise<ApiResult<ChangeRequest>> {
  return request<ChangeRequest>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/change-requests`, body, token);
}

export function getMyChangeRequests(token: string): Promise<ApiResult<ChangeRequest[]>> {
  return get<ChangeRequest[]>("/api/v1/me/change-requests", token);
}
