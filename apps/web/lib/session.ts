import { cookies } from "next/headers";

// The API token lives only in an httpOnly cookie, so page scripts (and any injected script) cannot read it.
// Server components and server actions read it and call the API on the user's behalf.

// "__Host-" makes browsers refuse the cookie unless it is Secure, host-only and path "/", so no subdomain or
// plain-HTTP page can set or overwrite it. Local development runs on http://localhost, so it uses a plain name.
const COOKIE = process.env.NODE_ENV === "production" ? "__Host-bs_session" : "bs_session";

export async function getSessionToken(): Promise<string | null> {
  const store = await cookies();
  return store.get(COOKIE)?.value ?? null;
}

export async function setSession(token: string, expiresAt: string): Promise<void> {
  const store = await cookies();
  const expires = new Date(expiresAt);
  store.set(COOKIE, token, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    expires: Number.isNaN(expires.getTime()) ? undefined : expires,
  });
}

export async function clearSession(): Promise<void> {
  const store = await cookies();
  store.delete(COOKIE);
}
