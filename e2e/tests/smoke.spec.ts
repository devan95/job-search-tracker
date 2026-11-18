import { expect, test } from '@playwright/test'

// Smoke checks only: the stack is up and the app can talk to the API and the database.
// Workflow tests are separate tasks (see the Task Board).
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5080'

test('API health endpoints answer', async ({ request }) => {
  const live = await request.get(`${apiUrl}/api/health/live`)
  expect(live.status()).toBe(200)
  expect(await live.json()).toEqual({ status: 'ok' })

  const ready = await request.get(`${apiUrl}/api/health/ready`)
  expect(ready.status()).toBe(200)
  expect(await ready.json()).toEqual({ status: 'ok', database: 'ok' })
})

test('app loads and shows a running API and a connected database', async ({ page }) => {
  await page.goto('/')

  await expect(
    page.getByRole('heading', { level: 1, name: 'Job Search & Application Tracker' }),
  ).toBeVisible()
  await expect(page.getByTestId('api-status')).toHaveText('Running')
  await expect(page.getByTestId('db-status')).toHaveText('Connected')
  await expect(page.getByRole('alert')).toHaveCount(0)
})
