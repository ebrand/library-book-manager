import { vi } from "vitest";

export interface RecordedRequest {
  method: string;
  path: string;
  query: URLSearchParams;
  body: unknown;
  headers: Headers;
}

type Responder = (request: RecordedRequest) => { status: number; body?: unknown };

/**
 * Replaces global fetch with a scripted stand-in for the v1 API. Each route is answered by
 * the first matching responder; every request is recorded so tests can assert on exactly
 * what the UI sent.
 */
export function fakeApi() {
  const requests: RecordedRequest[] = [];
  const routes: { method: string; path: string | RegExp; respond: Responder }[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === "string" ? input : input.toString(), "https://app.test");
    const method = (init?.method ?? "GET").toUpperCase();
    const request: RecordedRequest = {
      method,
      path: url.pathname,
      query: url.searchParams,
      body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined,
      headers: new Headers(init?.headers),
    };
    requests.push(request);
    const route = routes.find(
      (r) => r.method === method && (typeof r.path === "string" ? r.path === url.pathname : r.path.test(url.pathname)),
    );
    if (!route) return new Response(JSON.stringify({ error: "NOT_SCRIPTED", message: `${method} ${url.pathname}` }), { status: 599 });
    const { status, body } = route.respond(request);
    return new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });
  });
  vi.stubGlobal("fetch", fetchMock);

  return {
    requests,
    on(method: string, path: string | RegExp, respond: Responder | { status: number; body?: unknown }) {
      routes.unshift({ method, path, respond: typeof respond === "function" ? respond : () => respond });
      return this;
    },
    sent(method: string, path: string) {
      return requests.filter((r) => r.method === method && r.path === path);
    },
  };
}

export const title = (overrides: Partial<Record<string, unknown>> = {}) => ({
  isbn: "9780261102217",
  title: "The Hobbit",
  author: "J. R. R. Tolkien",
  publicationYear: 1937,
  copyCount: 0,
  availableCopies: 0,
  available: false,
  copies: [],
  ...overrides,
});

export const error = (code: string, field?: string) => ({ error: code, message: `server says ${code}`, field: field ?? null });
