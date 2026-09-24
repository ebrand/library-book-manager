import { useSyncExternalStore } from "react";

export type Role = "member" | "librarian" | "administrator";

export interface SessionUser {
  userId: string;
  email: string;
  role: Role;
}

export interface Session {
  token: string;
  user: SessionUser;
}

/**
 * The signed-in session: the bearer token and who it belongs to. Kept in sessionStorage, so it
 * survives a reload but not closing the tab, and never includes the password
 * (QUESTIONS.md Q-AUTH-01).
 */
const KEY = "lbm.session";

let cachedRaw: string | null | undefined;
let notice: string | null = null;
let snapshot: { session: Session | null; notice: string | null } = { session: null, notice: null };
const listeners = new Set<() => void>();

function readRaw(): string | null {
  try {
    return window.sessionStorage.getItem(KEY);
  } catch {
    return null;
  }
}

function parse(raw: string | null): Session | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Session;
    return typeof value.token === "string" && value.user ? value : null;
  } catch {
    return null;
  }
}

function read() {
  const raw = readRaw();
  if (raw !== cachedRaw) {
    cachedRaw = raw;
    snapshot = { session: parse(raw), notice };
  }
  return snapshot;
}

function changed() {
  cachedRaw = undefined;
  read();
  listeners.forEach((l) => l());
}

export function currentSession(): Session | null {
  return read().session;
}

export function startSession(session: Session) {
  notice = null;
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify(session));
  } catch {
    // Storage refused (private mode): the session lasts until the page is reloaded.
  }
  cachedRaw = JSON.stringify(session);
  snapshot = { session, notice };
  listeners.forEach((l) => l());
}

/** Forgets the session; <paramref name="why"/> is shown on the sign-in page. */
export function endSession(why: string | null) {
  notice = why;
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {
    // nothing stored
  }
  changed();
}

export function useSession() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    read,
  );
}
