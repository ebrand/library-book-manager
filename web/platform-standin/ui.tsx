import { useId, type ReactNode } from "react";

export function Button(props: {
  children: ReactNode;
  onClick?: () => void;
  type?: "button" | "submit";
  disabled?: boolean;
  label?: string;
  variant?: "primary" | "secondary" | "danger";
}) {
  return (
    <button
      type={props.type ?? "button"}
      onClick={props.onClick}
      disabled={props.disabled}
      aria-label={props.label}
      data-variant={props.variant ?? "secondary"}
    >
      {props.children}
    </button>
  );
}

export function TextField(props: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | undefined;
  inputMode?: "text" | "numeric";
  autoComplete?: string;
  type?: "text" | "email" | "password";
}) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="field">
      <label htmlFor={id}>{props.label}</label>
      <input
        id={id}
        type={props.type ?? "text"}
        value={props.value}
        inputMode={props.inputMode}
        autoComplete={props.autoComplete}
        aria-invalid={props.error ? true : undefined}
        aria-describedby={props.error ? errorId : undefined}
        onChange={(e) => props.onChange(e.target.value)}
      />
      {props.error && (
        <span id={errorId} className="field-error">
          {props.error}
        </span>
      )}
    </div>
  );
}

export function SelectField<T extends string>(props: {
  label: string;
  value: T;
  options: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  const id = useId();
  return (
    <div className="field">
      <label htmlFor={id}>{props.label}</label>
      <select id={id} value={props.value} onChange={(e) => props.onChange(e.target.value as T)}>
        {props.options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  );
}

export function Form(props: { onSubmit: () => void; children: ReactNode; label: string }) {
  return (
    <form
      aria-label={props.label}
      onSubmit={(e) => {
        e.preventDefault();
        props.onSubmit();
      }}
    >
      {props.children}
    </form>
  );
}

export function Table(props: {
  caption: string;
  columns: readonly string[];
  rows: readonly { key: string; cells: readonly ReactNode[] }[];
}) {
  return (
    <table>
      <caption>{props.caption}</caption>
      <thead>
        <tr>
          {props.columns.map((c) => (
            <th key={c} scope="col">
              {c}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {props.rows.map((row) => (
          <tr key={row.key}>
            {row.cells.map((cell, i) => (
              <td key={i}>{cell}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function Dialog(props: { open: boolean; title: string; children?: ReactNode; actions: ReactNode }) {
  const id = useId();
  if (!props.open) return null;
  return (
    <div role="dialog" aria-modal="true" aria-labelledby={id} className="dialog">
      <h2 id={id}>{props.title}</h2>
      {props.children}
      <div className="dialog-actions">{props.actions}</div>
    </div>
  );
}

export function Alert(props: { children: ReactNode }) {
  return <div role="alert">{props.children}</div>;
}

export function Status(props: { children: ReactNode }) {
  return <div role="status">{props.children}</div>;
}

export function Nav(props: { label: string; children: ReactNode }) {
  return <nav aria-label={props.label}>{props.children}</nav>;
}
