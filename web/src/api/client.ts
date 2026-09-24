import { createDataClient, type ApiResult, type DataClient } from "@platform-standin/index";
import { currentSession, endSession } from "../auth/session";

const noticeFor: Record<string, string> = {
  SESSION_EXPIRED: "Your session expired after 30 days without use. Sign in again.",
  SESSION_INVALID: "You have been signed out. Sign in again.",
  UNAUTHENTICATED: "You have been signed out. Sign in again.",
};

// Same-origin: the web app is served alongside the API, so paths are relative (QUESTIONS.md Q-WEB-01).
const raw = createDataClient({
  headers: (): Record<string, string> => {
    const session = currentSession();
    return session ? { Authorization: `Bearer ${session.token}` } : {};
  },
});

/** A refused session sends the user back to sign-in, whichever request found out. */
async function watch<T>(pending: Promise<ApiResult<T>>): Promise<ApiResult<T>> {
  const result = await pending;
  if (!result.ok && result.status === 401) {
    const notice = noticeFor[result.error.error];
    if (notice && currentSession()) endSession(notice);
  }
  return result;
}

export const client: DataClient = {
  get: (path, query) => watch(raw.get(path, query)),
  post: (path, body) => watch(raw.post(path, body)),
  patch: (path, body) => watch(raw.patch(path, body)),
  put: (path, body) => watch(raw.put(path, body)),
  del: (path) => watch(raw.del(path)),
};
