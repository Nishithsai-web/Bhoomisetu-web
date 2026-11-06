// Pure parsing for the listing forms, kept free of Next.js imports so it is unit-tested with node --test.
// The API validates again; this layer gives fast, translated, per-field feedback.

import { isAreaUnit, parseAreaValue, toAsciiDigits } from "./units.ts";
import type { CreateOffer, CreateParcel } from "./api.ts";

export const LAND_CLASSES = ["agricultural", "non_agricultural", "converted", "unknown"] as const;
export const AREA_SOURCES = ["record", "deed", "survey", "lister"] as const;
export const FACINGS = ["east", "north", "west", "south", "north_east", "north_west", "south_east", "south_west"] as const;
export const ROAD_TYPES = ["national_highway", "state_highway", "main_road", "layout_road", "village_road", "cart_track"] as const;
export const ROAD_WIDTH_SOURCES = ["approved_layout", "measured_on_map", "lister"] as const;
export const ACCESS_RIGHTS = ["direct_public_road", "through_private_land_with_right", "through_private_land_no_record", "unknown"] as const;
export const LENGTH_UNITS = ["feet", "metres"] as const;

export const METRES_PER_FOOT = 0.3048;

/** A field-level problem. `code` is translated through the dictionary's `errors` section. */
export type FieldError = { field: string; code: string };

export type Parsed<T> = { ok: true; value: T } | { ok: false; errors: FieldError[] };

type Form = { get(name: string): unknown };

const text = (form: Form, name: string) => {
  const value = form.get(name);
  return typeof value === "string" ? value.trim() : "";
};

const oneOf = <T extends string>(values: readonly T[], value: string): value is T => (values as readonly string[]).includes(value);

/** Positive number from a field, or null when blank. Telugu and Devanagari digits accepted. */
function optionalNumber(form: Form, name: string, errors: FieldError[]): number | null {
  const raw = text(form, name);
  if (raw === "") return null;
  const value = parseAreaValue(raw);
  if (value === null) {
    errors.push({ field: name, code: "form.number_invalid" });
    return null;
  }
  return value;
}

/** Converts a length typed in feet or metres to metres, rounded to centimetres. */
export function toMetres(value: number, unit: string): number {
  const metres = unit === "feet" ? value * METRES_PER_FOOT : value;
  return Math.round(metres * 100) / 100;
}

export function parseParcelForm(form: Form): Parsed<CreateParcel> {
  const errors: FieldError[] = [];
  const required = (name: string) => {
    const value = text(form, name);
    if (value === "") errors.push({ field: name, code: "form.required" });
    return value;
  };

  const district = required("district");
  const mandal = required("mandal");
  const village = required("village");
  const surveyNumber = required("surveyNumber");

  const landClass = text(form, "landClass");
  if (!oneOf(LAND_CLASSES, landClass)) errors.push({ field: "landClass", code: "form.required" });

  const areaValue = parseAreaValue(text(form, "areaValue"));
  if (areaValue === null) errors.push({ field: "areaValue", code: "form.number_invalid" });
  const areaUnit = text(form, "areaUnit");
  if (!isAreaUnit(areaUnit)) errors.push({ field: "areaUnit", code: "form.required" });

  const areaSource = text(form, "areaSource");
  if (!oneOf(AREA_SOURCES, areaSource)) errors.push({ field: "areaSource", code: "form.required" });

  const lengthUnit = text(form, "lengthUnit") || "feet";
  if (!oneOf(LENGTH_UNITS, lengthUnit)) errors.push({ field: "lengthUnit", code: "form.required" });

  const frontage = optionalNumber(form, "frontage", errors);
  const depth = optionalNumber(form, "depth", errors);
  const facing = text(form, "facing");
  if (facing !== "" && !oneOf(FACINGS, facing)) errors.push({ field: "facing", code: "form.required" });

  const roadWidth = optionalNumber(form, "roadWidth", errors);
  const roadWidthSource = text(form, "roadWidthSource");
  if (roadWidth !== null && !oneOf(ROAD_WIDTH_SOURCES, roadWidthSource)) {
    errors.push({ field: "roadWidthSource", code: "road.width_source_required" });
  }
  const roadType = text(form, "roadType");
  if (roadType !== "" && !oneOf(ROAD_TYPES, roadType)) errors.push({ field: "roadType", code: "form.required" });
  const accessRight = text(form, "accessRight");
  if (accessRight !== "" && !oneOf(ACCESS_RIGHTS, accessRight)) errors.push({ field: "accessRight", code: "form.required" });
  const distance = optionalNumber(form, "distanceToMainRoadM", errors);

  const lgd = toAsciiDigits(text(form, "lgdVillageCode"));
  if (lgd !== "" && !/^\d{1,10}$/.test(lgd)) errors.push({ field: "lgdVillageCode", code: "location.lgd_invalid" });

  if (errors.length > 0) return { ok: false, errors };

  const hasRoad = roadWidth !== null || roadType !== "" || accessRight !== "" || distance !== null;
  return {
    ok: true,
    value: {
      stateCode: "TG",
      district,
      mandal,
      village,
      lgdVillageCode: lgd === "" ? null : lgd,
      surveyNumber,
      landClass,
      area: { value: areaValue as number, unit: areaUnit },
      areaSource,
      frontageM: frontage === null ? null : toMetres(frontage, lengthUnit),
      depthM: depth === null ? null : toMetres(depth, lengthUnit),
      facing: facing === "" ? null : facing,
      road: hasRoad
        ? {
            widthM: roadWidth === null ? null : toMetres(roadWidth, lengthUnit),
            widthSource: roadWidth === null ? null : roadWidthSource,
            type: roadType === "" ? null : roadType,
            accessRight: accessRight === "" ? null : accessRight,
            distanceToMainRoadM: distance === null ? null : Math.round(distance),
          }
        : null,
    },
  };
}

/** Rupees typed as "48,50,000", "4850000" or in Telugu/Devanagari digits; at most two decimals. */
export function parseRupees(raw: string): number | null {
  const cleaned = toAsciiDigits(raw).replace(/[,\s₹]/g, "");
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  const value = Number(cleaned);
  return value > 0 && value <= 100_000_000_000 ? value : null;
}

export function parseOfferForm(form: Form): Parsed<CreateOffer> {
  const errors: FieldError[] = [];
  const listerKind = text(form, "listerKind");
  if (listerKind !== "owner" && listerKind !== "agent") errors.push({ field: "listerKind", code: "form.required" });

  const price = parseRupees(text(form, "askingPrice"));
  if (price === null) errors.push({ field: "askingPrice", code: "offer.price_invalid" });

  let brokerage: number | null = null;
  const rawBrokerage = toAsciiDigits(text(form, "brokeragePercent"));
  if (rawBrokerage !== "") {
    if (!/^\d+(\.\d{1,2})?$/.test(rawBrokerage) || Number(rawBrokerage) > 10) {
      errors.push({ field: "brokeragePercent", code: "offer.brokerage_invalid" });
    } else {
      brokerage = Number(rawBrokerage);
    }
  }
  if (listerKind === "owner" && brokerage !== null && brokerage > 0) {
    errors.push({ field: "brokeragePercent", code: "offer.owner_brokerage" });
  }

  const terms = text(form, "brokerageTerms");
  if (terms.length > 500) errors.push({ field: "brokerageTerms", code: "offer.terms_too_long" });

  if (errors.length > 0) return { ok: false, errors };
  return {
    ok: true,
    value: {
      listerKind: listerKind as "owner" | "agent",
      askingPriceRupees: price as number,
      buyerBrokeragePercent: listerKind === "owner" ? null : brokerage,
      brokerageTerms: terms === "" ? null : terms,
    },
  };
}

/** Only same-site relative paths, so a crafted ?next= cannot send people to another site after sign-in. */
export function safeNextPath(raw: unknown, fallback: string): string {
  if (typeof raw !== "string" || !raw.startsWith("/") || raw.startsWith("//") || raw.includes("\\")) return fallback;
  return raw;
}
