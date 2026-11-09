import { test } from "node:test";
import assert from "node:assert/strict";
import { describePoint, sortPoints } from "./compare.ts";

const texts = { cheaper: "{percent}% cheaper per {unit}", different_type: "Different type: {kind}", furnishing: "Furnishing: {furnishing}" };
const labels = { unit: "sq yd", kinds: { converted: "Converted land" }, furnishing: { full: "Fully furnished" } };

test("fills values and the unit", () => {
  assert.equal(describePoint({ code: "cheaper", kind: "pro", values: { percent: "18" } }, texts, labels), "18% cheaper per sq yd");
});

test("turns codes into labels", () => {
  assert.equal(describePoint({ code: "different_type", kind: "info", values: { kind: "converted" } }, texts, labels), "Different type: Converted land");
  assert.equal(describePoint({ code: "furnishing", kind: "info", values: { furnishing: "full" } }, texts, labels), "Furnishing: Fully furnished");
});

test("unknown codes fall back to the code; pros come first", () => {
  assert.equal(describePoint({ code: "new_rule", kind: "info", values: {} }, texts, labels), "new_rule");
  const sorted = sortPoints([
    { code: "a", kind: "info", values: {} },
    { code: "b", kind: "con", values: {} },
    { code: "c", kind: "pro", values: {} },
  ]);
  assert.deepEqual(sorted.map((p) => p.code), ["c", "b", "a"]);
});
