import { test } from "node:test";
import assert from "node:assert/strict";
import { formatFileSize, formatIndianMobile, whatsappLink } from "./contact.ts";

test("builds a wa.me link with the message encoded", () => {
  assert.equal(whatsappLink("+919848012345", "Survey 12/A & price?"), "https://wa.me/919848012345?text=Survey%2012%2FA%20%26%20price%3F");
});

test("refuses anything that is not an Indian mobile number", () => {
  for (const bad of ["919848012345", "+915848012345", "+91984801234", "+1 2025550100", "javascript:alert(1)", ""]) {
    assert.equal(whatsappLink(bad, "hi"), null, bad);
  }
});

test("formats numbers and sizes for people", () => {
  assert.equal(formatIndianMobile("+919848012345"), "+91 98480 12345");
  assert.equal(formatFileSize(2.5 * 1024 * 1024, "en-IN"), "2.5 MB");
  assert.equal(formatFileSize(300, "en-IN"), "1 KB");
});
