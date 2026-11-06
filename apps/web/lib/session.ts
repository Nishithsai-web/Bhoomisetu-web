import { cookies } from "next/headers";

// The API token lives only in an httpOnly cookie, so page scripts (and any injected script) cannot read it.
// Server components and server actions read it and call the API on the user's behalf.

const COOKIE = "bs_session";

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
