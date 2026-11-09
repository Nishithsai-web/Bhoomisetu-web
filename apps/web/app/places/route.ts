import { NextResponse, type NextRequest } from "next/server";
import { searchPlaces } from "@/lib/api";

/** Place search for the map, forwarded to the API (whose address stays on the server). */
export async function GET(request: NextRequest) {
  const q = (request.nextUrl.searchParams.get("q") ?? "").trim().slice(0, 100);
  const lang = request.nextUrl.searchParams.get("lang") ?? "en";
  if (q.length < 2) return NextResponse.json([], { status: 200 });
  const result = await searchPlaces(q, lang === "te" || lang === "hi" ? lang : "en");
  return result.ok
    ? NextResponse.json(result.data, { headers: { "cache-control": "private, max-age=300" } })
    : NextResponse.json({ code: result.code }, { status: result.status });
}
