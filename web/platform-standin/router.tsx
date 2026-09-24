import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

const NAVIGATE_EVENT = "platform-standin:navigate";

export function navigate(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new Event(NAVIGATE_EVENT));
}

/** The path currently shown; re-renders on navigation. */
export function usePathname() {
  const [path, setPath] = useState(window.location.pathname);
  useEffect(() => {
    const update = () => setPath(window.location.pathname);
    window.addEventListener("popstate", update);
    window.addEventListener(NAVIGATE_EVENT, update);
    return () => {
      window.removeEventListener("popstate", update);
      window.removeEventListener(NAVIGATE_EVENT, update);
    };
  }, []);
  return path;
}

type Params = Record<string, string>;
const ParamsContext = createContext<Params>({});

export function useParams(): Params {
  return useContext(ParamsContext);
}

function match(pattern: string, path: string): Params | null {
  const want = pattern.split("/").filter(Boolean);
  const have = path.split("/").filter(Boolean);
  if (want.length !== have.length) return null;
  const params: Params = {};
  for (let i = 0; i < want.length; i++) {
    const w = want[i]!;
    const h = decodeURIComponent(have[i]!);
    if (w.startsWith(":")) params[w.slice(1)] = h;
    else if (w !== h) return null;
  }
  return params;
}

/** Renders the first route whose pattern matches the current path; `:name` segments become params. */
export function Router(props: { routes: readonly { path: string; element: ReactNode }[]; fallback: ReactNode }) {
  const path = usePathname();
  for (const route of props.routes) {
    const params = match(route.path, path);
    if (params) return <ParamsContext.Provider value={params}>{route.element}</ParamsContext.Provider>;
  }
  return <>{props.fallback}</>;
}

export function Link(props: { to: string; children: ReactNode; current?: boolean; onNavigate?: () => void; className?: string }) {
  return (
    <a
      href={props.to}
      className={props.className}
      aria-current={props.current ? "page" : undefined}
      onClick={(e) => {
        // Let modified clicks (new tab, new window) behave as ordinary links.
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
        e.preventDefault();
        props.onNavigate?.();
        navigate(props.to);
      }}
    >
      {props.children}
    </a>
  );
}
