import { test } from "node:test";
import assert from "node:assert/strict";
import { compactRupees, isNew } from "./compactPrice.ts";

test("compact prices read like spoken amounts", () => {
  assert.equal(compactRupees(4_500_000), "₹45L");
  assert.equal(compactRupees(12_345_678), "₹1.2Cr");
  assert.equal(compactRupees(18_000), "₹18k");
  assert.equal(compactRupees(950), "₹950");
  assert.equal(compactRupees(99_950), "₹1L");
  assert.equal(compactRupees(9_999_999), "₹1Cr");
  assert.equal(compactRupees(999_999), "₹10L");
  assert.equal(compactRupees(9_500_000), "₹95L");
  assert.equal(compactRupees(95_000), "₹95k");
});

test("new means listed within the last days", () => {
  const now = new Date("2026-11-09T12:00:00Z");
  assert.equal(isNew("2026-11-05T12:00:00Z", now), true);
  assert.equal(isNew("2026-10-20T12:00:00Z", now), false);
  assert.equal(isNew(null, now), false);
});
