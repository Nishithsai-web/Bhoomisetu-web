import { test } from "node:test";
import assert from "node:assert/strict";
import { findCodes } from "./check-error-codes.mjs";

test("finds codes in the C# call shapes the API uses", () => {
  const source = `
    ApiResults.Invalid("a.b", "x");
    ApiResults.Problem(StatusCodes.Status409Conflict, "c.d", "y");
    ApiResults.Problem(429, "g.h_2", "y");
    Result<int>.Failure("e.f", "z");
    ApiResults.Invalid(code: "i.j.k", message: "m");
    ApiResults.Forbidden(@"l.m", "n");
    return ApiResults.Problem(
        StatusCodes.Status409Conflict,
        "p.q",
        "multi-line");
    ApiResults.Invalid("r.s", "You are the owner; you do not (really) need this.");
    ApiResults.Problem(Status(), "t.u", "nested call first");
    ApiResults.Invalid("v.w", "say \\"hi\\" then go");
    Invalid(error.Code);`;
  assert.deepEqual(findCodes(source), ["a.b", "c.d", "g.h_2", "e.f", "i.j.k", "l.m", "p.q", "r.s", "t.u", "v.w"]);
});
