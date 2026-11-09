import { NextResponse, type NextRequest } from "next/server";
import { getMapParcels, getRentalMap, isLandClass } from "@/lib/api";

/**
 * The map page asks this route for the land inside the visible area; it forwards to the API (whose address
 * stays on the server) after checking the numbers.
 */
export async function GET(request: NextRequest) {
  const q = request.nextUrl.searchParams;
  const area = { south: Number(q.get("south")), west: Number(q.get("west")), north: Number(q.get("north")), east: Number(q.get("east")) };
  if (Object.values(area).some((v) => !Number.isFinite(v))) {
    return NextResponse.json({ code: "map.area_invalid" }, { status: 400 });
  }
  const newDays = q.get("newDays") === "7" ? 7 : undefined;
  if (q.get("mode") === "rent") {
    const bedrooms = Number(q.get("bedrooms") ?? "0");
    const homes = await getRentalMap(area, Number.isInteger(bedrooms) && bedrooms > 0 && bedrooms <= 10 ? bedrooms : undefined, newDays);
    return homes.ok
      ? NextResponse.json(homes.data, { headers: { "cache-control": "no-store" } })
      : NextResponse.json({ code: homes.code }, { status: homes.status });
  }
  const landClass = q.get("landClass") ?? "";
  const result = await getMapParcels(area, isLandClass(landClass) ? landClass : undefined, newDays);
  if (!result.ok) {
    return NextResponse.json({ code: result.code }, { status: result.status });
  }
  return NextResponse.json(result.data, { headers: { "cache-control": "no-store" } });
}
