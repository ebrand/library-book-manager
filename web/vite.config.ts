import { fileURLToPath, URL } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Stand-in for the platform component library, router and data client until the real
      // package is identified (QUESTIONS.md Q-STD-01). Swap this alias for the real dependency.
      "@platform-standin": fileURLToPath(new URL("./platform-standin", import.meta.url)),
    },
  },
  server: {
    proxy: { "/v1": { target: "https://localhost:7043", secure: false } },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./tests/setup.ts"],
    testTimeout: 120_000,
  },
});
