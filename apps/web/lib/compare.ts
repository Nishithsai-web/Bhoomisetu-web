// Turns a pros-and-cons point from the API (a code and values) into a sentence in the reader's language.
// The facts and thresholds are decided by the API (NearbyComparison); this only words them.

export type Point = { code: string; kind: "pro" | "con" | "info"; values: Record<string, string> };

/** {placeholders} filled from values; code-valued fields (kind, furnishing) are first turned into labels. */
export function describePoint(point: Point, texts: Record<string, string>, labels: { unit: string; kinds: Record<string, string>; furnishing: Record<string, string> }): string {
  const template = texts[point.code] ?? point.code;
  const values: Record<string, string> = { ...point.values, unit: labels.unit };
  if (values.kind) values.kind = labels.kinds[values.kind] ?? values.kind;
  if (values.furnishing) values.furnishing = labels.furnishing[values.furnishing] ?? values.furnishing;
  return template.replace(/\{(\w+)\}/g, (_, key: string) => values[key] ?? "");
}

/** Pros first, then cons, then neutral facts: the order a buyer reads them in. */
export function sortPoints<T extends Point>(points: T[]): T[] {
  const order = { pro: 0, con: 1, info: 2 } as const;
  return [...points].sort((a, b) => order[a.kind] - order[b.kind]);
}
