#!/usr/bin/env node
// Fails when the API can return an error code that has no plain-language message in the web app.
// Codes are found in the C# sources: any call to Invalid/Forbidden/Failure/Problem/Conflict whose arguments
// contain a dotted code literal, plus the codes listed below that are built at run time.
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../../src", import.meta.url));

/** Codes the scan cannot see: built by string interpolation or by helpers without a literal. */
export const RUNTIME_CODES = [
  "not_found", // ApiResults.NotFound
  "location.district_invalid", // ParcelLocation: $"location.{field}_invalid"
  "location.mandal_invalid",
  "location.village_invalid",
  "rate_limited", // Program.cs rate limiter OnRejected
];

const callStart = /\b(?:Invalid|Forbidden|Failure|Problem|Conflict)\s*\(/g;
const codeShape = /^[a-z0-9_]+(?:\.[a-z0-9_]+)+$/;

/**
 * String literals passed directly to the call starting at `open` (the index of its "("), in order.
 * Brackets are matched and string contents skipped, so ";" or ")" inside a message, nested calls and
 * multi-line arguments do not end the call early.
 */
function argumentLiterals(source, open) {
  const literals = [];
  let depth = 0;
  for (let i = open; i < source.length; i++) {
    const ch = source[i];
    if (ch === '"') {
      const verbatim = source[i - 1] === "@";
      let j = i + 1;
      let text = "";
      while (j < source.length) {
        if (verbatim && source[j] === '"' && source[j + 1] === '"') { text += '"'; j += 2; continue; }
        if (!verbatim && source[j] === "\\") { text += source[j + 1]; j += 2; continue; }
        if (source[j] === '"') break;
        text += source[j++];
      }
      if (depth === 1 && source[i - 1] !== "$" && source[i - 2] !== "$") literals.push(text);
      i = j;
    } else if (ch === "(") depth++;
    else if (ch === ")" && --depth === 0) break;
  }
  return literals;
}

export function findCodes(source) {
  const codes = [];
  for (const m of source.matchAll(callStart)) {
    const code = argumentLiterals(source, m.index + m[0].length - 1).find((text) => codeShape.test(text));
    if (code) codes.push(code);
  }
  return codes;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const codes = new Set(RUNTIME_CODES);
  for (const file of readdirSync(root, { recursive: true })) {
    if (String(file).endsWith(".cs")) for (const code of findCodes(readFileSync(join(root, String(file)), "utf8"))) codes.add(code);
  }
  const en = JSON.parse(readFileSync(fileURLToPath(new URL("../messages/en.json", import.meta.url)), "utf8"));
  const missing = [...codes].filter((code) => !Object.hasOwn(en.errors, code)).sort();
  if (missing.length > 0) {
    console.error(`API error codes without a message in messages/*.json "errors":\n  ${missing.join("\n  ")}`);
    process.exit(1);
  }
  console.log(`All ${codes.size} API error codes have messages (te and hi are checked by check:i18n).`);
}
