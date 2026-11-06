#!/usr/bin/env node
// Fails when te.json or hi.json differ from en.json in keys, leave a value empty, or drop a {placeholder}.
// Machine checks only: every new string still needs review by a native speaker before release.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

export function flatten(object, prefix = "") {
  const out = new Map();
  for (const [key, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === "object" && !Array.isArray(value)) {
      for (const [k, v] of flatten(value, path)) out.set(k, v);
    } else {
      out.set(path, value);
    }
  }
  return out;
}

const placeholders = (text) => [...String(text).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

export function compare(reference, candidate, name) {
  const problems = [];
  const ref = flatten(reference);
  const cand = flatten(candidate);
  for (const [key, value] of ref) {
    if (!cand.has(key)) {
      problems.push(`${name}: missing ${key}`);
      continue;
    }
    const translated = cand.get(key);
    if (typeof translated !== "string" || translated.trim() === "") {
      problems.push(`${name}: empty ${key}`);
      continue;
    }
    const expected = placeholders(value).join(",");
    const actual = placeholders(translated).join(",");
    if (expected !== actual) {
      problems.push(`${name}: ${key} placeholders {${actual}} should be {${expected}}`);
    }
  }
  for (const key of cand.keys()) {
    if (!ref.has(key)) problems.push(`${name}: extra key ${key} not in en.json`);
  }
  return problems;
}

function load(locale) {
  return JSON.parse(readFileSync(fileURLToPath(new URL(`../messages/${locale}.json`, import.meta.url)), "utf8"));
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const en = load("en");
  const problems = ["te", "hi"].flatMap((locale) => compare(en, load(locale), locale));
  if (problems.length > 0) {
    console.error(problems.join("\n"));
    process.exit(1);
  }
  console.log(`Translations complete: ${flatten(en).size} keys in en, te and hi.`);
}
