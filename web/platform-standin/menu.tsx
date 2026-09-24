import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Link, usePathname } from "./router";

export interface MenuItem {
  to: string;
  label: string;
}

/**
 * The application's main menu: a name that leads home, the pages on offer (the one being
 * shown marked as current), and a trailing slot for account controls. Below 48rem the items
 * fold behind a "Menu" button.
 */
export function Menu(props: { label: string; home: MenuItem; items: readonly MenuItem[]; end?: ReactNode }) {
  const path = usePathname();
  const listId = useId();
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      toggle.current?.focus();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <nav aria-label={props.label} className="menu">
      <Link to={props.home.to} className="menu-home" onNavigate={() => setOpen(false)}>
        {props.home.label}
      </Link>
      <button
        ref={toggle}
        type="button"
        className="menu-toggle"
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen((o) => !o)}
      >
        Menu
      </button>
      <ul id={listId} className="menu-items" data-open={open}>
        {props.items.map((item) => (
          <li key={item.to}>
            <Link to={item.to} current={item.to === path} onNavigate={() => setOpen(false)}>
              {item.label}
            </Link>
          </li>
        ))}
      </ul>
      {props.end && (
        <div className="menu-end" data-open={open}>
          {props.end}
        </div>
      )}
    </nav>
  );
}
