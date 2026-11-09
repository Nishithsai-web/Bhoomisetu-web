import { NextResponse, type NextRequest } from "next/server";
import { getMapParcels, isLandClass } from "@/lib/api";

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
  const landClass = q.get("landClass") ?? "";
  const result = await getMapParcels(area, isLandClass(landClass) ? landClass : undefined);
  if (!result.ok) {
    return NextResponse.json({ code: result.code }, { status: result.status });
  }
  return NextResponse.json(result.data, { headers: { "cache-control": "no-store" } });
}
