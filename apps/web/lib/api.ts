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
  listerLabel: string;
  listerKind: "owner" | "agent";
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

export type ApiResult<T> = { ok: true; data: T } | { ok: false; status: number; code: string };

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

async function get<T>(path: string): Promise<ApiResult<T>> {
  let response: Response;
  try {
    response = await fetch(`${baseUrl()}${path}`, {
      cache: "no-store",
      headers: { accept: "application/json" },
      signal: AbortSignal.timeout(8000),
    });
  } catch (error) {
    console.error(`API request failed: ${path}`, error);
    return { ok: false, status: 503, code: "api.unreachable" };
  }

  if (!response.ok) {
    let code = "api.error";
    try {
      const problem = (await response.json()) as { code?: unknown };
      if (typeof problem.code === "string") code = problem.code;
    } catch {
      // Body was not JSON; keep the generic code. The status is still reported.
    }
    return { ok: false, status: response.status, code };
  }

  return { ok: true, data: (await response.json()) as T };
}

export function getComparison(parcelId: string, sort: Sort): Promise<ApiResult<Comparison>> {
  return get<Comparison>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/comparison?sort=${sort}`);
}

export function searchParcels(village: string, page: number): Promise<ApiResult<Paged<ParcelSummary>>> {
  const params = new URLSearchParams({ page: String(page), pageSize: "20" });
  if (village) params.set("village", village);
  return get<Paged<ParcelSummary>>(`/api/v1/parcels?${params.toString()}`);
}
