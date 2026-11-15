import { test } from "node:test";
import assert from "node:assert/strict";
import { buyingCosts, emi } from "./costs.ts";

const LAKH = 100_000;

test("Telangana urban plot: stamp 4%, transfer 1.5%, registration 0.5%", () => {
  const costs = buyingCosts(50 * LAKH, "TG", "urban");
  assert.deepEqual(
    costs.duties.map((d) => [d.key, d.amount]),
    [
      ["stamp", 200_000],
      ["transfer", 75_000],
      ["registration", 25_000],
    ],
  );
  assert.equal(costs.dutiesTotal, 300_000);
  assert.equal(costs.upfrontTotal, 50 * LAKH + 300_000);
  assert.equal(costs.loan, null);
});

test("Telangana rural plot: stamp 5.5% and registration 2%, no transfer duty", () => {
  const costs = buyingCosts(10 * LAKH, "TG", "rural");
  assert.deepEqual(costs.duties.map((d) => d.key), ["stamp", "registration"]);
  assert.equal(costs.dutiesTotal, 55_000 + 20_000);
});

test("Andhra Pradesh: stamp 5%, transfer 1.5%, registration 1% for any area", () => {
  const costs = buyingCosts(50 * LAKH, "AP", "urban");
  assert.equal(costs.dutiesTotal, 250_000 + 75_000 + 50_000);
  assert.deepEqual(buyingCosts(50 * LAKH, "AP", "rural").duties, costs.duties);
});

test("EMI on a reducing balance: 10 lakh at 10% for 10 years is about 13,215 a month", () => {
  assert.ok(Math.abs(emi(10 * LAKH, 10, 10) - 13_214.71) < 1);
});

test("zero interest is simple division", () => {
  assert.equal(emi(12_000, 0, 1), 1_000);
});

test("a loan adds EMI and total interest, and no interest when the loan is zero", () => {
  const costs = buyingCosts(50 * LAKH, "TG", "urban", { amount: 10 * LAKH, annualRatePercent: 10, years: 10 });
  if (!costs.loan) throw new Error("expected a loan result");
  assert.equal(costs.loan.months, 120);
  assert.equal(costs.loan.totalPaid, costs.loan.emi * 120);
  assert.equal(costs.loan.totalInterest, costs.loan.totalPaid - 10 * LAKH);
  const none = buyingCosts(50 * LAKH, "TG", "urban", { amount: 0, annualRatePercent: 10, years: 10 });
  assert.equal(none.loan, null);
});
