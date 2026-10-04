import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  globalTeardown: './global-teardown.cjs',
  use: {
    baseURL: 'http://127.0.0.1:5142',
    viewport: { width: 1280, height: 800 },
  },
  webServer: {
    command: 'powershell -NoProfile -ExecutionPolicy Bypass -File start-api.ps1',
    url: 'http://127.0.0.1:5142/admin/login',
    timeout: 180_000,
    reuseExistingServer: false,
  },
});
