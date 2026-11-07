import { NextResponse, type NextRequest } from "next/server";
import { fetchDocumentFile, isUuid } from "@/lib/api";
import { isLocale } from "@/lib/i18n";
import { clearSession, getSessionToken } from "@/lib/session";

type Context = { params: Promise<{ parcelId: string; documentId: string }> };

/**
 * Streams one document file from the API to the browser. The session token stays in its httpOnly cookie;
 * the API decides whether this person may open the file (subscribed buyer, the uploader or a reviewer).
 */
export async function GET(request: NextRequest, { params }: Context) {
  const { parcelId, documentId } = await params;
  const requested = request.nextUrl.searchParams.get("l") ?? "en";
  const locale = isLocale(requested) ? requested : "en";
  if (!isUuid(parcelId) || !isUuid(documentId)) {
    return new NextResponse("Not found", { status: 404 });
  }

  const back = `/${locale}/parcels/${parcelId}/documents`;
  const token = await getSessionToken();
  if (!token) {
    return NextResponse.redirect(new URL(`/${locale}/signin?next=${encodeURIComponent(back)}`, request.url));
  }

  const result = await fetchDocumentFile(parcelId, documentId, token);
  if (!(result instanceof Response)) {
    if (result.status === 401) {
      await clearSession();
      return NextResponse.redirect(new URL(`/${locale}/signin?next=${encodeURIComponent(back)}`, request.url));
    }
    return NextResponse.redirect(new URL(`${back}?error=${encodeURIComponent(result.code)}`, request.url));
  }

  const headers = new Headers({
    "content-type": result.headers.get("content-type") ?? "application/octet-stream",
    "x-content-type-options": "nosniff",
    "cache-control": "private, no-store",
    // Opens PDFs and images in the browser's own viewer; never runs them as part of this site.
    "content-security-policy": "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox",
  });
  const disposition = result.headers.get("content-disposition");
  if (disposition) headers.set("content-disposition", disposition.replace(/^attachment/i, "inline"));
  const length = result.headers.get("content-length");
  if (length) headers.set("content-length", length);
  return new NextResponse(result.body, { status: 200, headers });
}
