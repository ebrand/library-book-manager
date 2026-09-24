export interface ApiErrorBody {
  error: string;
  message: string;
  field?: string | null;
}

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; error: ApiErrorBody };

export interface DataClient {
  get<T>(path: string, query?: Record<string, string | number | undefined>): Promise<ApiResult<T>>;
  post<T>(path: string, body: unknown): Promise<ApiResult<T>>;
  patch<T>(path: string, body: unknown): Promise<ApiResult<T>>;
  put<T>(path: string, body: unknown): Promise<ApiResult<T>>;
  del<T = void>(path: string): Promise<ApiResult<T>>;
}

export function createDataClient(options: { baseUrl?: string; headers?: () => Record<string, string> } = {}): DataClient {
  const base = options.baseUrl ?? "";

  async function send<T>(method: string, path: string, body?: unknown): Promise<ApiResult<T>> {
    let response: Response;
    try {
      response = await fetch(base + path, {
        method,
        headers: {
          Accept: "application/json",
          ...(body === undefined ? {} : { "Content-Type": "application/json" }),
          ...options.headers?.(),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch {
      return { ok: false, status: 0, error: { error: "NETWORK", message: "The server could not be reached." } };
    }
    const text = await response.text();
    const parsed: unknown = text ? JSON.parse(text) : undefined;
    if (response.ok) return { ok: true, status: response.status, data: parsed as T };
    const error = (parsed as ApiErrorBody | undefined) ?? { error: `HTTP_${response.status}`, message: response.statusText };
    return { ok: false, status: response.status, error };
  }

  return {
    get: (path, query) => {
      const params = new URLSearchParams();
      for (const [k, v] of Object.entries(query ?? {})) if (v !== undefined) params.set(k, String(v));
      const qs = params.toString();
      return send("GET", qs ? `${path}?${qs}` : path);
    },
    post: (path, body) => send("POST", path, body),
    patch: (path, body) => send("PATCH", path, body),
    put: (path, body) => send("PUT", path, body),
    del: (path) => send("DELETE", path),
  };
}
