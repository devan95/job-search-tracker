import { randomBytes } from 'node:crypto'
import { expect, type APIRequestContext, type Page, type PlaywrightWorkerArgs } from '@playwright/test'

// Helpers for the workflow tests. Everything is synthetic: names carry a random suffix so tests can run in
// parallel, and a re-run against the same database never collides with earlier runs.
export const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5080'

/** A short lower-case hex suffix. Lower case keeps it identical after the API's name normalization. */
export const uid = (): string => randomBytes(4).toString('hex')

export type WorkMode = 'remote' | 'onSite' | 'hybrid'

export interface Candidate {
  id: string
  name: string
}
export interface Employer {
  id: string
  name: string
  normalizedName: string
}
export interface CompanyListDetail {
  id: string
  name: string
  defaultWorkModes: WorkMode[]
  employers: Employer[]
}
export interface SearchProfile {
  id: string
  candidateId: string
  name: string
  workModes: WorkMode[] | null
  companyMonitoringEnabled: boolean
  broadSearchEnabled: boolean
  companyLists: { id: string; name: string }[]
}

/**
 * Fails the whole file, visibly, when the API or PostgreSQL is not running, instead of
 * letting every test time out one by one.
 */
export async function requireStack({ playwright }: Pick<PlaywrightWorkerArgs, 'playwright'>): Promise<void> {
  const api = await playwright.request.newContext()
  try {
    const ready = await api.get(`${apiUrl}/api/health/ready`)
    expect(
      ready.status(),
      'The API and PostgreSQL must be running (docs/development.md, "End-to-end tests")',
    ).toBe(200)
    expect(await ready.json()).toEqual({ status: 'ok', database: 'ok' })
  } finally {
    await api.dispose()
  }
}

// ---- Seeding and reading through the API (the UI is what the tests exercise; this is set-up and evidence) ----

export async function apiCreateCandidate(request: APIRequestContext, name: string): Promise<Candidate> {
  const response = await request.post(`${apiUrl}/api/candidates`, { data: { name } })
  expect(response.status()).toBe(201)
  return (await response.json()) as Candidate
}

export async function apiCreateList(
  request: APIRequestContext,
  name: string,
  defaultWorkModes: WorkMode[] = [],
): Promise<CompanyListDetail> {
  const response = await request.post(`${apiUrl}/api/company-lists`, { data: { name, defaultWorkModes } })
  expect(response.status()).toBe(201)
  return (await response.json()) as CompanyListDetail
}

export async function apiImport(request: APIRequestContext, listId: string, text: string): Promise<void> {
  const response = await request.post(`${apiUrl}/api/company-lists/${listId}/import`, { data: { text } })
  expect(response.status()).toBe(200)
}

export async function apiCreateProfile(
  request: APIRequestContext,
  body: { candidateId: string; name: string; companyListIds?: string[]; workModes?: WorkMode[] | null },
): Promise<SearchProfile> {
  const response = await request.post(`${apiUrl}/api/search-profiles`, { data: body })
  expect(response.status()).toBe(201)
  return (await response.json()) as SearchProfile
}

export async function apiGetList(request: APIRequestContext, id: string): Promise<CompanyListDetail> {
  const response = await request.get(`${apiUrl}/api/company-lists/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as CompanyListDetail
}

export async function apiGetProfile(request: APIRequestContext, id: string): Promise<SearchProfile> {
  const response = await request.get(`${apiUrl}/api/search-profiles/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as SearchProfile
}

/** Canonical employers whose normalized name contains `text` (the API's `q` filter). */
export async function apiFindEmployers(request: APIRequestContext, text: string): Promise<Employer[]> {
  const response = await request.get(`${apiUrl}/api/employers`, { params: { q: text, take: '500' } })
  expect(response.status()).toBe(200)
  return (await response.json()) as Employer[]
}

/** Reads every page of a list endpoint (`skip`/`take`, at most 500 per page), so a busy database cannot hide a row. */
export async function apiListAll<T>(request: APIRequestContext, path: string): Promise<T[]> {
  const pageSize = 500
  const all: T[] = []
  for (let skip = 0; ; skip += pageSize) {
    const response = await request.get(`${apiUrl}${path}`, { params: { skip: String(skip), take: String(pageSize) } })
    expect(response.status()).toBe(200)
    const page = (await response.json()) as T[]
    all.push(...page)
    if (page.length < pageSize) return all
  }
}

// ---- Navigation and UI building blocks ----

export type Section = 'Candidates' | 'Company lists' | 'Search profiles'

/** Opens the app fresh (so every list is loaded from the API) and switches to one section. */
export async function gotoSection(page: Page, section: Section): Promise<void> {
  await page.goto('/')
  await page.getByRole('tab', { name: section, exact: true }).click()
  await expect(page.getByRole('heading', { level: 2, name: section, exact: true })).toBeVisible()
}

export const candidateRow = (page: Page, name: string) =>
  page
    .getByRole('list', { name: 'Candidates', exact: true })
    .getByRole('listitem')
    .filter({ hasText: name })

/** The detail panel of the selected company list. */
export const listDetail = (page: Page, name: string) =>
  page.getByRole('region', { name: `List ${name}`, exact: true })

export async function addCandidateViaUi(page: Page, name: string): Promise<void> {
  await page.getByLabel('Candidate name', { exact: true }).fill(name)
  await page.getByRole('button', { name: 'Add candidate', exact: true }).click()
  await expect(candidateRow(page, name)).toBeVisible()
}

export async function createListViaUi(
  page: Page,
  name: string,
  options: { location?: string; modes?: string[] } = {},
): Promise<void> {
  const form = page.getByRole('form', { name: 'New company list', exact: true })
  await form.getByLabel('List name', { exact: true }).fill(name)
  if (options.location) await form.getByLabel('Location label (optional)', { exact: true }).fill(options.location)
  for (const mode of options.modes ?? []) {
    await form.getByRole('checkbox', { name: mode, exact: true }).check()
  }
  await form.getByRole('button', { name: 'Create list', exact: true }).click()
}

export async function selectListViaUi(page: Page, name: string): Promise<void> {
  await page
    .getByRole('list', { name: 'Company lists', exact: true })
    .getByRole('button')
    .filter({ hasText: name })
    .click()
  await expect(listDetail(page, name)).toBeVisible()
}

/** Pastes text into the import box of the open list and asks for the preview. */
export async function previewImport(page: Page, listName: string, text: string): Promise<void> {
  const detail = listDetail(page, listName)
  await detail.getByLabel('Companies to import').fill(text)
  await detail.getByRole('button', { name: 'Preview import', exact: true }).click()
}

export const importPreview = (page: Page, listName: string) =>
  listDetail(page, listName).getByRole('region', { name: 'Import preview', exact: true })

/** The live region that announces the preview summary (outside the preview section). */
export const previewSummary = (page: Page, listName: string) =>
  listDetail(page, listName).getByRole('status', { name: 'Preview summary', exact: true })

export const previewRow = (page: Page, listName: string, rowNumber: number) =>
  importPreview(page, listName)
    .getByRole('list', { name: 'Preview rows', exact: true })
    .getByRole('listitem')
    .filter({ hasText: new RegExp(`^Row ${rowNumber}:`) })

export async function confirmImport(page: Page, listName: string): Promise<void> {
  await listDetail(page, listName).getByRole('button', { name: 'Confirm import', exact: true }).click()
}

/** The one-line preview summary the screen shows for these counts. */
export function summaryText(c: {
  total: number
  new: number
  existing: number
  alreadyInList: number
  duplicate: number
  invalid: number
}): string {
  const rows = c.total === 1 ? '1 row' : `${c.total} rows`
  return `${rows}: ${c.new} new, ${c.existing} existing, ${c.alreadyInList} already in this list, ${c.duplicate} duplicate in pasted text, ${c.invalid} invalid.`
}

/** The one-line result the screen shows after a confirmed save. */
export const resultText = (r: { created: number; reused: number; skipped: number; added: number }): string =>
  `Import saved: ${r.created} created, ${r.reused} reused, ${r.skipped} skipped (${r.added} added to this list).`

export const profileCard = (page: Page, name: string) =>
  page
    .getByRole('list', { name: 'Search profiles', exact: true })
    .getByRole('listitem')
    .filter({ hasText: name })

export async function openProfileEditor(page: Page, profileName: string) {
  await page.getByRole('button', { name: `Edit ${profileName}`, exact: true }).click()
  const form = page.getByRole('form', { name: 'Edit search profile', exact: true })
  await expect(form).toBeVisible()
  return form
}
