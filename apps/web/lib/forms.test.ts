import { test } from "node:test";
import assert from "node:assert/strict";
import { parseOfferForm, parseParcelForm, parseRupees, safeNextPath, toMetres } from "./forms.ts";

const form = (values: Record<string, string>) => new Map(Object.entries(values));

const validParcel = {
  district: "Rangareddy",
  mandal: "Shankarpalle",
  village: "Mokila",
  surveyNumber: "123/A",
  landClass: "non_agricultural",
  areaValue: "300",
  areaUnit: "square_yard",
  areaSource: "lister",
  lengthUnit: "feet",
  frontage: "40",
  depth: "67.5",
  facing: "east",
  roadWidth: "30",
  roadWidthSource: "approved_layout",
  roadType: "layout_road",
  accessRight: "direct_public_road",
  distanceToMainRoadM: "250",
};

test("parses a full parcel form and converts feet to metres", () => {
  const result = parseParcelForm(form(validParcel));
  assert.ok(result.ok);
  assert.equal(result.value.frontageM, 12.19);
  assert.equal(result.value.depthM, 20.57);
  assert.equal(result.value.road?.widthM, 9.14);
  assert.equal(result.value.road?.widthSource, "approved_layout");
  assert.equal(result.value.area.value, 300);
  assert.equal(result.value.stateCode, "TG");
  assert.equal(result.value.lgdVillageCode, null);
});

test("metres are kept as metres", () => {
  const result = parseParcelForm(form({ ...validParcel, lengthUnit: "metres", frontage: "12" }));
  assert.ok(result.ok);
  assert.equal(result.value.frontageM, 12);
});

test("reports every missing required field", () => {
  const result = parseParcelForm(form({}));
  assert.ok(!result.ok);
  const fields = result.errors.map((e) => e.field).sort();
  assert.deepEqual(fields, ["areaSource", "areaUnit", "areaValue", "district", "landClass", "mandal", "surveyNumber", "village"]);
});

test("road width needs its source", () => {
  const result = parseParcelForm(form({ ...validParcel, roadWidthSource: "" }));
  assert.ok(!result.ok);
  assert.deepEqual(result.errors, [{ field: "roadWidthSource", code: "road.width_source_required" }]);
});

test("road is omitted when no road fact is given", () => {
  const { roadWidth, roadWidthSource, roadType, accessRight, distanceToMainRoadM, ...rest } = validParcel;
  void [roadWidth, roadWidthSource, roadType, accessRight, distanceToMainRoadM];
  const result = parseParcelForm(form(rest));
  assert.ok(result.ok);
  assert.equal(result.value.road, null);
});

test("rejects unknown codes and bad numbers", () => {
  const result = parseParcelForm(form({ ...validParcel, facing: "up", frontage: "-3", lgdVillageCode: "12a" }));
  assert.ok(!result.ok);
  assert.deepEqual(result.errors.map((e) => e.field).sort(), ["facing", "frontage", "lgdVillageCode"]);
});

test("accepts Telugu digits in numbers and LGD code", () => {
  const result = parseParcelForm(form({ ...validParcel, areaValue: "౩౦౦", lgdVillageCode: "౫౭౪౧౨౩" }));
  assert.ok(result.ok);
  assert.equal(result.value.area.value, 300);
  assert.equal(result.value.lgdVillageCode, "574123");
});

test("parses rupees as Indians write them", () => {
  assert.equal(parseRupees("48,50,000"), 4_850_000);
  assert.equal(parseRupees("₹ 1,25,00,000"), 12_500_000);
  assert.equal(parseRupees("1000.50"), 1000.5);
  assert.equal(parseRupees("1000.505"), null);
  assert.equal(parseRupees("0"), null);
  assert.equal(parseRupees("abc"), null);
  assert.equal(parseRupees("200000000000"), null);
});

test("offer form rules", () => {
  const agent = parseOfferForm(form({ listerKind: "agent", askingPrice: "50,00,000", brokeragePercent: "1.5", brokerageTerms: "" }));
  assert.ok(agent.ok);
  assert.deepEqual(agent.value, { listerKind: "agent", askingPriceRupees: 5_000_000, buyerBrokeragePercent: 1.5, brokerageTerms: null });

  const undisclosed = parseOfferForm(form({ listerKind: "agent", askingPrice: "5000000" }));
  assert.ok(undisclosed.ok);
  assert.equal(undisclosed.value.buyerBrokeragePercent, null);

  const owner = parseOfferForm(form({ listerKind: "owner", askingPrice: "5000000", brokeragePercent: "1" }));
  assert.ok(!owner.ok);
  assert.equal(owner.errors[0]?.code, "offer.owner_brokerage");

  const tooHigh = parseOfferForm(form({ listerKind: "agent", askingPrice: "5000000", brokeragePercent: "12" }));
  assert.ok(!tooHigh.ok);
});

test("only same-site next paths are allowed", () => {
  assert.equal(safeNextPath("/te/sell", "/en"), "/te/sell");
  assert.equal(safeNextPath("//evil.example", "/en"), "/en");
  assert.equal(safeNextPath("https://evil.example", "/en"), "/en");
  assert.equal(safeNextPath("/\\evil.example", "/en"), "/en");
  assert.equal(safeNextPath(undefined, "/en"), "/en");
});

test("length conversion", () => {
  assert.equal(toMetres(30, "feet"), 9.14);
  assert.equal(toMetres(100, "feet"), 30.48);
  assert.equal(toMetres(9, "metres"), 9);
});
