#!/usr/bin/env node
// Fails when the API can return an error code that has no plain-language message in the web app.
// Codes are found in the C# sources: ApiResults.Invalid/Forbidden/Problem(...) and Result.Failure(...).
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../../src", import.meta.url));
const pattern = /(?:Invalid|Forbidden|Failure)\(\s*"([a-z_]+\.[a-z_]+)"|Problem\(\s*StatusCodes\.\w+,\s*"([a-z_]+\.[a-z_]+)"/g;

export function findCodes(source) {
  return [...source.matchAll(pattern)].map((m) => m[1] ?? m[2]);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const codes = new Set();
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
