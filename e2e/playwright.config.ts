import { defineConfig, devices } from '@playwright/test'

// Browser tests run against the real stack: the API and PostgreSQL from Docker Compose
// (see docs/development.md, "End-to-end tests") and the Vite dev server for the web app,
// which this config starts itself. Nothing here uses real data or credentials.
const webUrl = process.env.E2E_WEB_URL ?? 'http://127.0.0.1:5173'
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5080'

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['list'], ['html', { open: 'never' }]]
    : [['list']],
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev',
    cwd: '../web',
    // Point the dev-server proxy at the same API the tests call directly.
    env: { API_PROXY_TARGET: apiUrl },
    url: webUrl,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
})
