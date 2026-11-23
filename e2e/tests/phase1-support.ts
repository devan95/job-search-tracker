import { expect, type APIRequestContext, type Page } from '@playwright/test'
import { apiUrl, type Candidate } from './support.js'

// Helpers for the Phase 1 acceptance spec. Same rules as support.ts: everything is synthetic, set-up and evidence go
// through the API, and the behaviour under test is driven through the UI.

export interface JobSummary {
  id: string
  employer: { id: string; name: string }
  title: string
  status: string
  location: string | null
  workMode: string | null
  createdVia: string
}
export interface JobDetail extends JobSummary {
  requisitionId: string | null
  canonicalUrl: string | null
  statusHistory: { fromStatus: string | null; toStatus: string }[]
}
export interface AuditEntry {
  action: string
  actor: string
  oldStatus: string | null
  newStatus: string
  oldIsApplied: boolean | null
  newIsApplied: boolean
  newAppliedAtUtc: string | null
  reason: string | null
}
export interface ApplicationDetail {
  id: string
  candidateId: string
  status: string
  isApplied: boolean
  method: string | null
  appliedAtUtc: string | null
  appliedConfirmedAtUtc: string | null
  job: { id: string; title: string; employer: { id: string; name: string } }
  auditTrail: AuditEntry[]
}
export interface ProfileWithCriteria {
  id: string
  name: string
  workModes: string[] | null
  companyLists: { id: string; name: string }[]
  roleLists: { id: string; name: string }[]
  locationSets: { id: string; name: string }[]
}
export interface LocationSetDetail {
  id: string
  name: string
  locations: { label: string; scope: string; countryCode: string | null }[]
}
export interface RoleListDetail {
  id: string
  name: string
  titles: { title: string; aliases: { alias: string }[] }[]
}

// ---- API: seeding and evidence ----

export interface NewJob {
  employer: string
  title: string
  applyUrl?: string | null
  location?: string | null
  workMode?: string | null
  requisitionId?: string | null
}

export async function apiCreateJob(request: APIRequestContext, job: NewJob): Promise<JobDetail> {
  const response = await request.post(`${apiUrl}/api/jobs`, { data: job })
  expect(response.status(), await response.text()).toBe(201)
  return (await response.json()) as JobDetail
}

export async function apiGetJob(request: APIRequestContext, id: string): Promise<JobDetail> {
  const response = await request.get(`${apiUrl}/api/jobs/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as JobDetail
}

/** Jobs whose title or company contains `text` (the API's `q` filter). */
export async function apiFindJobs(request: APIRequestContext, text: string): Promise<JobSummary[]> {
  const response = await request.get(`${apiUrl}/api/jobs`, { params: { q: text, take: '500' } })
  expect(response.status()).toBe(200)
  return (await response.json()) as JobSummary[]
}

export async function apiListApplications(
  request: APIRequestContext,
  filter: { candidateId?: string; jobId?: string },
): Promise<{ id: string; status: string; isApplied: boolean }[]> {
  const params: Record<string, string> = { take: '500' }
  if (filter.candidateId) params.candidateId = filter.candidateId
  if (filter.jobId) params.jobId = filter.jobId
  const response = await request.get(`${apiUrl}/api/applications`, { params })
  expect(response.status()).toBe(200)
  return (await response.json()) as { id: string; status: string; isApplied: boolean }[]
}

export async function apiCreateApplication(
  request: APIRequestContext,
  candidateId: string,
  jobId: string,
): Promise<ApplicationDetail> {
  const response = await request.post(`${apiUrl}/api/applications`, { data: { candidateId, jobId } })
  expect(response.status(), await response.text()).toBe(201)
  return (await response.json()) as ApplicationDetail
}

export async function apiGetApplication(request: APIRequestContext, id: string): Promise<ApplicationDetail> {
  const response = await request.get(`${apiUrl}/api/applications/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as ApplicationDetail
}

export async function apiGetProfile(request: APIRequestContext, id: string): Promise<ProfileWithCriteria> {
  const response = await request.get(`${apiUrl}/api/search-profiles/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as ProfileWithCriteria
}

/** The one search profile of this candidate with this name. */
export async function apiFindProfile(
  request: APIRequestContext,
  candidateId: string,
  name: string,
): Promise<ProfileWithCriteria> {
  const response = await request.get(`${apiUrl}/api/search-profiles`, { params: { candidateId } })
  expect(response.status()).toBe(200)
  const found = ((await response.json()) as ProfileWithCriteria[]).find((p) => p.name === name)
  if (!found) throw new Error(`Search profile ${name} was not stored`)
  return found
}

export async function apiFindCandidate(request: APIRequestContext, name: string): Promise<Candidate> {
  const response = await request.get(`${apiUrl}/api/candidates`, { params: { take: '500' } })
  expect(response.status()).toBe(200)
  const found = ((await response.json()) as Candidate[]).find((c) => c.name === name)
  if (!found) throw new Error(`Candidate ${name} was not stored`)
  return found
}

export async function apiFindLocationSet(request: APIRequestContext, name: string): Promise<LocationSetDetail> {
  const list = await request.get(`${apiUrl}/api/location-sets`, { params: { take: '500' } })
  expect(list.status()).toBe(200)
  const summary = ((await list.json()) as { id: string; name: string }[]).find((s) => s.name === name)
  if (!summary) throw new Error(`Location set ${name} was not stored`)
  const detail = await request.get(`${apiUrl}/api/location-sets/${summary.id}`)
  expect(detail.status()).toBe(200)
  return (await detail.json()) as LocationSetDetail
}

export async function apiFindRoleList(request: APIRequestContext, name: string): Promise<RoleListDetail> {
  const list = await request.get(`${apiUrl}/api/role-lists`, { params: { take: '500' } })
  expect(list.status()).toBe(200)
  const summary = ((await list.json()) as { id: string; name: string }[]).find((s) => s.name === name)
  if (!summary) throw new Error(`Role list ${name} was not stored`)
  const detail = await request.get(`${apiUrl}/api/role-lists/${summary.id}`)
  expect(detail.status()).toBe(200)
  return (await detail.json()) as RoleListDetail
}

// ---- Navigation ----

export type Tab = 'Candidates' | 'Company lists' | 'Role lists' | 'Location sets' | 'Search profiles' | 'Jobs' | 'Applications'

/** Opens the app fresh (so every list is loaded from the API) and switches to one tab. */
export async function gotoTab(page: Page, tab: Tab): Promise<void> {
  await page.goto('/')
  await page.getByRole('tab', { name: tab, exact: true }).click()
  await expect(page.getByRole('heading', { level: 2, name: tab, exact: true })).toBeVisible()
}

// ---- Role lists and location sets ----

export async function createRoleListViaUi(page: Page, name: string): Promise<void> {
  const form = page.getByRole('form', { name: 'New role list', exact: true })
  await form.getByLabel('Role list name', { exact: true }).fill(name)
  await form.getByRole('button', { name: 'Create role list', exact: true }).click()
  await expect(page.getByRole('region', { name: `Role list ${name}`, exact: true })).toBeVisible()
}

export async function addRoleTitleViaUi(page: Page, listName: string, title: string, aliases: string[]): Promise<void> {
  const detail = page.getByRole('region', { name: `Role list ${listName}`, exact: true })
  const form = detail.getByRole('form', { name: 'Add title', exact: true })
  await form.getByLabel('Primary title', { exact: true }).fill(title)
  await form.getByLabel(/^Equivalent titles/).fill(aliases.join('\n'))
  await form.getByRole('button', { name: 'Add title', exact: true }).click()
  await expect(detail.getByRole('list', { name: 'Titles in this role list', exact: true })).toContainText(title)
}

export async function createLocationSetViaUi(page: Page, name: string): Promise<void> {
  const form = page.getByRole('form', { name: 'New location set', exact: true })
  await form.getByLabel('Location set name', { exact: true }).fill(name)
  await form.getByRole('button', { name: 'Create location set', exact: true }).click()
  await expect(page.getByRole('region', { name: `Location set ${name}`, exact: true })).toBeVisible()
}

/** Adds one location; `scope` is the visible option text of the "Covers" select. */
export async function addLocationViaUi(
  page: Page,
  setName: string,
  location: { label: string; scope: string; countryCode?: string },
  shown: string,
): Promise<void> {
  const detail = page.getByRole('region', { name: `Location set ${setName}`, exact: true })
  const form = detail.getByRole('form', { name: 'Add location', exact: true })
  await form.getByLabel('Label', { exact: true }).fill(location.label)
  await form.getByRole('combobox', { name: /^Covers/ }).selectOption({ label: location.scope })
  if (location.countryCode) await form.getByLabel(/^Country code(?: \(optional\))?$/).fill(location.countryCode)
  await form.getByRole('button', { name: 'Add location', exact: true }).click()
  await expect(detail.getByRole('list', { name: 'Locations in this set', exact: true })).toContainText(shown)
}

// ---- Jobs ----

export const jobForm = (page: Page) => page.getByRole('form', { name: 'Job form', exact: true })

export interface JobFormFill {
  company?: string
  title?: string
  applyUrl?: string
  location?: string
  workMode?: string
  requisitionId?: string
  foundOn?: string
}

/** Fills the fields that are given; a field that is left out keeps what the form already shows. */
export async function fillJobForm(page: Page, values: JobFormFill): Promise<void> {
  const form = jobForm(page)
  if (values.company !== undefined) await form.getByLabel('Company', { exact: true }).fill(values.company)
  if (values.title !== undefined) await form.getByLabel('Job title', { exact: true }).fill(values.title)
  if (values.applyUrl !== undefined) await form.getByLabel('Apply link', { exact: true }).fill(values.applyUrl)
  if (values.location !== undefined) await form.getByLabel('Location', { exact: true }).fill(values.location)
  if (values.workMode !== undefined) await form.getByRole('combobox', { name: /^Work mode/ }).selectOption({ label: values.workMode })
  if (values.requisitionId !== undefined) {
    await form.getByLabel('Requisition id', { exact: true }).fill(values.requisitionId)
  }
  if (values.foundOn !== undefined) await form.getByLabel('Found on', { exact: true }).fill(values.foundOn)
}

export async function openAddJobForm(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Add job', exact: true }).click()
  await expect(jobForm(page)).toBeVisible()
}

/** The detail panel of the open job. */
export const jobDetail = (page: Page, title: string, company: string) =>
  page.getByRole('region', { name: `${title} at ${company}`, exact: true })

/** Opens a job from the list on the Jobs tab (searching first, so a busy database cannot hide it). */
export async function openJobViaUi(page: Page, search: string, title: string, company: string): Promise<void> {
  await page.getByLabel('Search jobs', { exact: true }).fill(search)
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await page.getByRole('button', { name: `Open ${title} at ${company}`, exact: true }).click()
  await expect(jobDetail(page, title, company)).toBeVisible()
}

/** Chooses the candidate in "Applications for this job". A select's accessible name includes its chosen option, so match by prefix. */
export async function chooseCandidateViaUi(page: Page, candidate: string): Promise<void> {
  await page.getByRole('combobox', { name: /^Candidate/ }).selectOption({ label: candidate })
}

/** The open application's detail panel (the one whose heading starts with "Application:"). */
export const applicationPanel = (page: Page) => page.getByRole('region', { name: /^Application: / })

export const applicationHistory = (page: Page) => page.getByRole('list', { name: 'Application history', exact: true })

/** A `datetime-local` value (viewer-local, minute precision) for a moment `deltaMs` from now. */
export function localDateTime(deltaMs: number): string {
  const d = new Date(Date.now() + deltaMs)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
