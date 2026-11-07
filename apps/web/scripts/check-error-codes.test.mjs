import { test } from "node:test";
import assert from "node:assert/strict";
import { findCodes } from "./check-error-codes.mjs";

test("finds codes in the three C# call shapes", () => {
  const source = `ApiResults.Invalid("a.b", "x"); ApiResults.Problem(StatusCodes.Status409Conflict, "c.d", "y"); Result<int>.Failure("e.f", "z"); Invalid(error.Code)`;
  assert.deepEqual(findCodes(source), ["a.b", "c.d", "e.f"]);
});
