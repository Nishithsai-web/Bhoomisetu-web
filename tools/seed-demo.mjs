// Fills an empty development database with sample land and homes through the real API, the way listers would:
// sign in with a one-time code, list land, drop its pin, add prices, list homes for rent.
// Used by the screenshot workflow so the map and pages can be seen with data. Never point it at production:
// it needs the development SMS sender, which writes codes to the API log (the API refuses to start with it
// outside Development).
//
//   node tools/seed-demo.mjs <api-url> <api-log-file>

import { readFileSync } from "node:fs";

const [api, logFile] = process.argv.slice(2);
if (!api || !logFile) {
  console.error("usage: node tools/seed-demo.mjs <api-url> <api-log-file>");
  process.exit(2);
}

// Only a local API: this script signs in with made-up numbers and lists made-up land, so it must never reach a
// shared or real server, whatever the API itself would allow.
const host = (() => {
  try {
    return new URL(api).hostname;
  } catch {
    return "";
  }
})();
if (!["localhost", "127.0.0.1", "::1", "[::1]"].includes(host)) {
  console.error(`refusing to seed ${api}: only a local API (localhost or 127.0.0.1) may be seeded`);
  process.exit(2);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(token, method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: { "content-type": "application/json", ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path} → ${response.status} ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : null;
}

/** Signs in through the OTP endpoints, reading the code the development sender wrote to the log. */
async function signIn(phone) {
  const last4 = phone.slice(-4);
  const before = readFileSync(logFile, "utf8").length;
  await call(null, "POST", "/api/v1/auth/otp/request", { phone });
  for (let i = 0; i < 50; i++) {
    const fresh = readFileSync(logFile, "utf8").slice(before);
    const match = [...fresh.matchAll(/DEVELOPMENT OTP for \S*?(\d{4}): (\d{6})/g)].filter((m) => m[1] === last4).pop();
    if (match) return (await call(null, "POST", "/api/v1/auth/otp/verify", { phone, code: match[2] })).accessToken;
    await sleep(100);
  }
  throw new Error(`no code in the log for ${phone}`);
}

// Pseudo-random but repeatable, so every run draws the same map.
let seed = 42;
const rand = () => ((seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648);
const jitter = (spread) => (rand() - 0.5) * spread;

// Villages west of Hyderabad, with real approximate centres.
const villages = [
  { village: "Mokila", mandal: "Shankarpalle", district: "Rangareddy", lat: 17.432, lng: 78.196 },
  { village: "Shankarpalle", mandal: "Shankarpalle", district: "Rangareddy", lat: 17.456, lng: 78.131 },
  { village: "Kokapet", mandal: "Gandipet", district: "Rangareddy", lat: 17.395, lng: 78.333 },
  { village: "Moinabad", mandal: "Moinabad", district: "Rangareddy", lat: 17.33, lng: 78.27 },
  { village: "Chevella", mandal: "Chevella", district: "Rangareddy", lat: 17.31, lng: 78.135 },
  { village: "Kandi", mandal: "Kandi", district: "Sangareddy", lat: 17.59, lng: 78.07 },
  { village: "Tellapur", mandal: "Ramachandrapuram", district: "Sangareddy", lat: 17.46, lng: 78.285 },
];

const listers = ["9848000101", "9848000102", "9848000103", "9848000104", "9848000105"];
const tokens = [];
for (const phone of listers) tokens.push(await signIn(phone));
console.log(`signed in ${tokens.length} listers`);

let parcels = 0;
let offers = 0;
for (let i = 0; i < 48; i++) {
  const v = villages[i % villages.length];
  const farm = i % 3 === 0;
  const landClass = farm ? "agricultural" : i % 3 === 1 ? "non_agricultural" : "converted";
  const area = farm ? { value: [1, 2, 3, 5][i % 4], unit: "acre" } : { value: [200, 267, 300, 400, 500][i % 5], unit: "square_yard" };
  const owner = tokens[i % tokens.length];
  const body = {
    stateCode: "TG",
    district: v.district,
    mandal: v.mandal,
    village: v.village,
    lgdVillageCode: null,
    surveyNumber: `${100 + i}/${(i % 4) + 1}`,
    landClass,
    area,
    areaSource: "lister",
    frontageM: null,
    depthM: null,
    facing: ["east", "north", "west", "south"][i % 4],
    road: { widthM: [6, 9, 12, 18][i % 4], widthSource: "measured_on_map", type: farm ? "village_road" : "layout_road", accessRight: "direct_public_road", distanceToMainRoadM: [50, 250, 800, 1500][i % 4] },
  };
  const parcel = await call(owner, "POST", "/api/v1/parcels", body);
  parcels++;
  await call(owner, "PUT", `/api/v1/parcels/${parcel.id}/pin`, { latitude: v.lat + jitter(0.05), longitude: v.lng + jitter(0.06), source: "map" });

  // Most land has one to three prices from different listers; every seventh has none yet.
  if (i % 7 === 6) continue;
  const base = farm ? area.value * (8_000_000 + rand() * 12_000_000) : area.value * (12_000 + rand() * 30_000);
  const count = 1 + (i % 3);
  for (let k = 0; k < count; k++) {
    const lister = tokens[(i + k + 1) % tokens.length];
    const price = Math.round((base * (1 + k * 0.04)) / 1000) * 1000;
    await call(lister, "POST", `/api/v1/parcels/${parcel.id}/offers`, k === 0 && i % 2 === 0 ? { listerKind: "owner", askingPriceRupees: price } : { listerKind: "agent", askingPriceRupees: price, buyerBrokeragePercent: 1 });
    offers++;
  }
}
console.log(`listed ${parcels} pieces of land with ${offers} prices`);

const areas = [
  { locality: "Gachibowli", mandal: "Serilingampally", district: "Rangareddy", lat: 17.44, lng: 78.35 },
  { locality: "Kondapur", mandal: "Serilingampally", district: "Rangareddy", lat: 17.465, lng: 78.364 },
  { locality: "Tellapur", mandal: "Ramachandrapuram", district: "Sangareddy", lat: 17.46, lng: 78.285 },
  { locality: "Kokapet", mandal: "Gandipet", district: "Rangareddy", lat: 17.395, lng: 78.333 },
];
const types = ["apartment", "apartment", "independent_house", "villa", "portion", "room"];
let homes = 0;
for (let i = 0; i < 24; i++) {
  const a = areas[i % areas.length];
  const type = types[i % types.length];
  const bedrooms = type === "room" ? 0 : type === "villa" ? 4 : 1 + (i % 3);
  const sqft = type === "room" ? 180 : bedrooms * 550 + 150;
  const rent = Math.round((sqft * (18 + rand() * 22)) / 500) * 500;
  await call(tokens[i % tokens.length], "POST", "/api/v1/rentals", {
    listerKind: i % 2 ? "agent" : "owner",
    propertyType: type,
    bedrooms,
    areaSqft: sqft,
    rentRupees: rent,
    depositRupees: rent * 2,
    furnishing: ["unfurnished", "semi", "full"][i % 3],
    preferredTenants: "any",
    parking: i % 2 === 0,
    availableFrom: "2026-12-01",
    locality: a.locality,
    mandal: a.mandal,
    district: a.district,
    latitude: a.lat + jitter(0.03),
    longitude: a.lng + jitter(0.03),
  });
  homes++;
}
console.log(`listed ${homes} homes for rent`);
