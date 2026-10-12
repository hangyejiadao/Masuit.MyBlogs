import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "tests/browser", fullyParallel: true, retries: 0,
  use: { baseURL: "http://127.0.0.1:4174", headless: true, trace: "retain-on-failure" },
  webServer: { command: "npm run dev -- --port 4174 --strictPort", url: "http://127.0.0.1:4174/vedio/", reuseExistingServer: false },
  projects: [{ name: "desktop", use: { viewport: { width: 1440, height: 900 } } },
    { name: "mobile", use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } }],
});
