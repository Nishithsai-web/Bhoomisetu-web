// Area units and conversion for instant display while typing. The API (Bhoomisetu.Modules.Land.Area) is the
// authority for stored values; both are tested against shared/fixtures/area-conversions.json.

export const AREA_UNITS = [
  "square_yard",
  "gunta",
  "acre",
  "cent",
  "square_foot",
  "square_metre",
  "hectare",
  "ankanam",
] as const;

export type AreaUnit = (typeof AREA_UNITS)[number];

const SQUARE_FOOT = 0.09290304; // exact: 0.3048 m squared
const SQUARE_YARD = 0.83612736; // exact: 0.9144 m squared

export const SQUARE_METRES_PER_UNIT: Readonly<Record<AreaUnit, number>> = {
  square_metre: 1,
  square_foot: SQUARE_FOOT,
  square_yard: SQUARE_YARD,
  gunta: 121 * SQUARE_YARD,
  acre: 4840 * SQUARE_YARD,
  cent: 48.4 * SQUARE_YARD,
  hectare: 10_000,
  ankanam: 72 * SQUARE_FOOT,
};

export function isAreaUnit(value: unknown): value is AreaUnit {
  return typeof value === "string" && (AREA_UNITS as readonly string[]).includes(value);
}

/** Converts Telugu (౦-౯) and Devanagari (०-९) digits to ASCII, so numbers typed in any of our scripts parse. */
export function toAsciiDigits(input: string): string {
  return input.replace(/[౦-౯०-९]/g, (ch) => {
    const code = ch.charCodeAt(0);
    const zero = code >= 0x0c66 ? 0x0c66 : 0x0966;
    return String(code - zero);
  });
}

/** Parses a user-typed positive number ("1,200.5", "౧౨౦౦"). Returns null when it is not a positive number. */
export function parseAreaValue(input: string): number | null {
  const cleaned = toAsciiDigits(input).replace(/[,\s]/g, "");
  if (!/^\d+(\.\d+)?$/.test(cleaned)) {
    return null;
  }
  const value = Number(cleaned);
  return Number.isFinite(value) && value > 0 ? value : null;
}

/** The same area in every unit. */
export function convertArea(value: number, from: AreaUnit): Record<AreaUnit, number> {
  if (!(value > 0) || !Number.isFinite(value)) {
    throw new RangeError("Area must be a positive number.");
  }
  const squareMetres = value * SQUARE_METRES_PER_UNIT[from];
  const result = {} as Record<AreaUnit, number>;
  for (const unit of AREA_UNITS) {
    result[unit] = squareMetres / SQUARE_METRES_PER_UNIT[unit];
  }
  return result;
}
