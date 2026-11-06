import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { AREA_UNITS, convertArea, isAreaUnit, parseAreaValue, toAsciiDigits } from "./units.ts";

type Fixture = { cases: { value: number; unit: string; expected: Record<string, number> }[] };

const fixturePath = fileURLToPath(new URL("../../../shared/fixtures/area-conversions.json", import.meta.url));
const fixture = JSON.parse(readFileSync(fixturePath, "utf8")) as Fixture;

test("matches every case in the shared fixture used by the API tests", () => {
  assert.ok(fixture.cases.length >= 8);
  for (const c of fixture.cases) {
    assert.ok(isAreaUnit(c.unit), `unknown unit ${c.unit}`);
    const result = convertArea(c.value, c.unit);
    for (const [unit, expected] of Object.entries(c.expected)) {
      assert.ok(isAreaUnit(unit));
      const actual = result[unit];
      assert.ok(Math.abs(actual - expected) <= 1e-6, `${c.value} ${c.unit} -> ${unit}: expected ${expected}, got ${actual}`);
    }
  }
});

test("fixture covers every unit", () => {
  const covered = new Set(Object.keys(fixture.cases[0]!.expected));
  for (const unit of AREA_UNITS) {
    assert.ok(covered.has(unit), `${unit} missing from fixture`);
  }
});

test("textbook relationships", () => {
  const acre = convertArea(1, "acre");
  assert.equal(Math.round(acre.gunta * 1e9) / 1e9, 40);
  assert.equal(Math.round(acre.square_yard * 1e9) / 1e9, 4840);
  assert.equal(Math.round(acre.cent * 1e9) / 1e9, 100);
  assert.equal(Math.round(convertArea(1, "gunta").square_yard * 1e9) / 1e9, 121);
});

test("rejects non-positive values", () => {
  assert.throws(() => convertArea(0, "acre"), RangeError);
  assert.throws(() => convertArea(-1, "acre"), RangeError);
  assert.throws(() => convertArea(Number.NaN, "acre"), RangeError);
});

test("parses numbers typed in English, Telugu and Hindi digits", () => {
  assert.equal(parseAreaValue("1,200.5"), 1200.5);
  assert.equal(parseAreaValue(" 200 "), 200);
  assert.equal(parseAreaValue("౨౦౦"), 200);
  assert.equal(parseAreaValue("२००"), 200);
  assert.equal(parseAreaValue("0"), null);
  assert.equal(parseAreaValue("-5"), null);
  assert.equal(parseAreaValue("12a"), null);
  assert.equal(parseAreaValue(""), null);
  assert.equal(parseAreaValue("1e3"), null);
});

test("converts digit scripts", () => {
  assert.equal(toAsciiDigits("సర్వే ౧౨౩/A"), "సర్వే 123/A");
  assert.equal(toAsciiDigits("१२३"), "123");
});

test("unit guard", () => {
  assert.ok(isAreaUnit("gunta"));
  assert.ok(!isAreaUnit("bigha"));
  assert.ok(!isAreaUnit(3));
});
