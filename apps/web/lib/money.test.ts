import { test } from "node:test";
import assert from "node:assert/strict";
import { formatNumber, formatRupees, formatRupeesShort } from "./money.ts";

const words = { lakh: "lakh", crore: "crore" };

test("uses Indian digit grouping", () => {
  assert.equal(formatRupees(5_050_000, "en-IN"), "₹50,50,000");
  assert.equal(formatNumber(1234567.891, "en-IN"), "12,34,567.89");
});

test("summarises in lakh and crore", () => {
  assert.equal(formatRupeesShort(12_500_000, "en-IN", words), "₹1.25 crore");
  assert.equal(formatRupeesShort(4_850_000, "en-IN", words), "₹48.5 lakh");
  assert.equal(formatRupeesShort(100_000, "en-IN", words), "₹1 lakh");
  assert.equal(formatRupeesShort(99_999, "en-IN", words), "₹99,999");
});

test("keeps paise only below one rupee", () => {
  assert.equal(formatRupees(0.5, "en-IN"), "₹0.50");
});
