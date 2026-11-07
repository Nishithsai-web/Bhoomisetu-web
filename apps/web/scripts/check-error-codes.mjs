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
];

const call = /\b(?:Invalid|Forbidden|Failure|Problem|Conflict)\s*\(([^;]*?)\)\s*[;,:)]/gs;
const literal = /@?"([a-z0-9_]+(?:\.[a-z0-9_]+)+)"/;

export function findCodes(source) {
  const codes = [];
  for (const m of source.matchAll(call)) {
    const code = literal.exec(m[1]);
    if (code) codes.push(code[1]);
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
