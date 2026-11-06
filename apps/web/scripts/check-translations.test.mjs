import { test } from "node:test";
import assert from "node:assert/strict";
import { compare } from "./check-translations.mjs";

test("passes identical structures", () => {
  assert.deepEqual(compare({ a: { b: "x {n}" } }, { a: { b: "y {n}" } }, "te"), []);
});

test("reports missing, empty, extra and placeholder problems", () => {
  const problems = compare({ a: "x", b: "y {count}", c: "z" }, { a: "", b: "y", d: "w" }, "hi");
  assert.deepEqual(problems.sort(), [
    "hi: b placeholders {} should be {count}",
    "hi: empty a",
    "hi: extra key d not in en.json",
    "hi: missing c",
  ]);
});
