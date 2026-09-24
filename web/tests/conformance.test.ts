// @vitest-environment node
import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { extname, join, relative } from "node:path";
import { describe, expect, it } from "vitest";

// plt-ui-001 and plt-ui-react-001. Their criteria describe the platform conformance check,
// which is not available here; these tests hold this repository to what that check looks for.

const root = join(__dirname, "..");
const pkg = JSON.parse(readFileSync(join(root, "package.json"), "utf8")) as {
  dependencies: Record<string, string>;
  devDependencies: Record<string, string>;
};

function filesUnder(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    return statSync(full).isDirectory() ? filesUnder(full) : [full];
  });
}

const appSources = () => filesUnder(join(root, "src"));

describe("plt-ui-001", () => {
  it("REQ-UI-004: the repository depends on typescript and every application source is .ts or .tsx", () => {
    expect(pkg.devDependencies.typescript ?? pkg.dependencies.typescript).toBeDefined();
    const sources = [...appSources(), ...filesUnder(join(root, "platform-standin"))];
    expect(sources.length).toBeGreaterThan(0);
    const notTypeScript = sources.filter((f) => ![".ts", ".tsx", ".css", ".md"].includes(extname(f)));
    expect(notTypeScript.map((f) => relative(root, f))).toEqual([]);
  });

  it("REQ-UI-002: tsconfig.json sets strict to true", () => {
    const tsconfig = JSON.parse(readFileSync(join(root, "tsconfig.json"), "utf8")) as { compilerOptions: { strict?: boolean } };
    expect(tsconfig.compilerOptions.strict).toBe(true);
  });

  it("AC-UI-002-2: a build with a type error in any source file exits non-zero and produces no bundle (with a clean-build control)", () => {
    const work = mkdtempSync(join(tmpdir(), "lbm-web-build-"));
    try {
      cpSync(root, work, { recursive: true, filter: (src) => !/node_modules|[\\/]dist([\\/]|$)/.test(relative(root, src)) });
      symlinkSync(join(root, "node_modules"), join(work, "node_modules"), "dir");

      const clean = spawnSync("npm", ["run", "build"], { cwd: work, encoding: "utf8" });
      expect(clean.status, clean.stdout + clean.stderr).toBe(0);
      expect(existsSync(join(work, "dist", "index.html"))).toBe(true);
      rmSync(join(work, "dist"), { recursive: true });

      writeFileSync(join(work, "src", "typeError.ts"), "export const broken: number = \"not a number\";\n");
      const broken = spawnSync("npm", ["run", "build"], { cwd: work, encoding: "utf8" });
      expect(broken.status).not.toBe(0);
      expect(broken.stdout + broken.stderr).toContain("typeError.ts");
      expect(existsSync(join(work, "dist"))).toBe(false);
    } finally {
      rmSync(work, { recursive: true, force: true });
    }
  });

  it("REQ-UI-003 (stand-in, Q-STD-01): application code defines no buttons, form controls, dialogs, tables or navigation of its own", () => {
    const primitive = /<(button|input|select|textarea|table|dialog|nav)[\s>/]/;
    const offenders = appSources()
      .filter((f) => f.endsWith(".tsx"))
      .filter((f) => primitive.test(readFileSync(f, "utf8")))
      .map((f) => relative(root, f));
    expect(offenders).toEqual([]);
  });
});

describe("plt-ui-react-001", () => {
  it("REQ-UIR-001: package.json depends on react 18 or later, and that is what is installed", () => {
    const declared = pkg.dependencies.react;
    expect(declared).toBeDefined();
    expect(Number(/^\D*(\d+)/.exec(declared!)?.[1])).toBeGreaterThanOrEqual(18);
    const installed = JSON.parse(readFileSync(join(root, "node_modules", "react", "package.json"), "utf8")) as { version: string };
    expect(Number(installed.version.split(".")[0])).toBeGreaterThanOrEqual(18);
  });

  it("REQ-UIR-002 (stand-in, Q-STD-01): routing and data fetching come only from the platform module, not an alternative", () => {
    const alternatives = /from\s+["'](react-router[^"']*|@tanstack\/[^"']*|swr|axios|wouter|@reach\/router)["']|\bfetch\(/;
    const offenders = appSources()
      .filter((f) => alternatives.test(readFileSync(f, "utf8")))
      .map((f) => relative(root, f));
    expect(offenders).toEqual([]);
    const deps = Object.keys({ ...pkg.dependencies, ...pkg.devDependencies });
    expect(deps.filter((d) => /react-router|@tanstack\/react-query|^swr$|^axios$|^wouter$/.test(d))).toEqual([]);
  });
});
