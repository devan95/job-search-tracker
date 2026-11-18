export type DatabaseState = 'ok' | 'unavailable' | 'not_checked'

export interface ReadyResponse {
  status: 'ok' | 'degraded'
  database: DatabaseState
}

export type HealthResult =
  | { kind: 'ready'; response: ReadyResponse }
  | { kind: 'degraded'; response: ReadyResponse }
  | { kind: 'unreachable'; reason: string }

/**
 * Reads API readiness. The API answers 503 with a body when it is running but
 * a dependency (the database) is down; that is "degraded", not "unreachable".
 */
export async function fetchHealth(
  fetchImpl: typeof fetch = fetch,
): Promise<HealthResult> {
  try {
    const res = await fetchImpl('/api/health/ready', {
      headers: { Accept: 'application/json' },
    })
    if (res.status !== 200 && res.status !== 503) {
      return { kind: 'unreachable', reason: `Unexpected HTTP ${res.status}` }
    }
    const body = (await res.json()) as ReadyResponse
    return res.status === 200
      ? { kind: 'ready', response: body }
      : { kind: 'degraded', response: body }
  } catch (err) {
    return {
      kind: 'unreachable',
      reason: err instanceof Error ? err.message : 'Request failed',
    }
  }
}

// ---------------------------------------------------------------------------
// Phase 1 management API (candidates, company lists, search profiles).
// The contract is docs/api.md.
// ---------------------------------------------------------------------------

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

export interface CompanyListSummary {
  id: string
  name: string
  locationLabel: string | null
  defaultWorkModes: WorkMode[]
  employerCount: number
}

export interface CompanyListDetail extends CompanyListSummary {
  employers: Employer[]
}

export interface CompanyListInput {
  name: string
  locationLabel: string | null
  defaultWorkModes: WorkMode[]
}

export interface SelectedCompanyList {
  id: string
  name: string
  defaultWorkModes: WorkMode[]
}

export interface SearchProfile {
  id: string
  candidateId: string
  name: string
  /** null = inherit the selected lists' defaults; [] = explicit empty set. */
  workModes: WorkMode[] | null
  companyMonitoringEnabled: boolean
  broadSearchEnabled: boolean
  profileVersion: number
  companyLists: SelectedCompanyList[]
}

export interface SearchProfileCreateInput {
  candidateId: string
  name: string
  companyListIds: string[]
  workModes: WorkMode[] | null
  companyMonitoringEnabled: boolean
  broadSearchEnabled: boolean
}

export interface SearchProfileUpdateInput {
  name: string
  workModes: WorkMode[] | null
  companyMonitoringEnabled: boolean
  broadSearchEnabled: boolean
}

/** An RFC 7807 problem document as the API returns it. */
export interface Problem {
  status: number
  code: string
  title: string
  detail?: string
  errors?: Record<string, string[]>
  existingId?: string
  searchProfileCount?: number
  companyListCount?: number
}

export type ApiErrorKind = 'problem' | 'network' | 'invalid_response'

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly problem: Problem | null

  constructor(kind: ApiErrorKind, message: string, problem: Problem | null = null) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.problem = problem
  }
}

type JsonRecord = Record<string, unknown>

const isRecord = (value: unknown): value is JsonRecord =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

function readFieldErrors(value: unknown): Record<string, string[]> | undefined {
  if (!isRecord(value)) return undefined
  const result: Record<string, string[]> = {}
  for (const [field, messages] of Object.entries(value)) {
    if (typeof messages === 'string') {
      result[field] = [messages]
    } else if (Array.isArray(messages)) {
      const strings = messages.filter((m): m is string => typeof m === 'string')
      if (strings.length > 0) result[field] = strings
    }
  }
  return Object.keys(result).length > 0 ? result : undefined
}

/** Parses a non-2xx response into a Problem; unreadable bodies become `unexpected`. */
export async function readProblem(res: Response): Promise<Problem> {
  let body: unknown
  try {
    body = await res.json()
  } catch {
    body = undefined
  }
  const rec: JsonRecord = isRecord(body) ? body : {}
  const problem: Problem = {
    status: res.status,
    code: typeof rec.code === 'string' && rec.code ? rec.code : 'unexpected',
    title:
      typeof rec.title === 'string' && rec.title
        ? rec.title
        : `Unexpected HTTP ${res.status}`,
  }
  if (typeof rec.detail === 'string') problem.detail = rec.detail
  const errors = readFieldErrors(rec.errors)
  if (errors) problem.errors = errors
  if (typeof rec.existingId === 'string') problem.existingId = rec.existingId
  if (typeof rec.searchProfileCount === 'number') {
    problem.searchProfileCount = rec.searchProfileCount
  }
  if (typeof rec.companyListCount === 'number') {
    problem.companyListCount = rec.companyListCount
  }
  return problem
}

interface ApiResult<T> {
  status: number
  data: T
}

async function send<T>(
  method: string,
  path: string,
  body?: unknown,
): Promise<ApiResult<T>> {
  let res: Response
  try {
    res = await fetch(`/api${path}`, {
      method,
      headers:
        body === undefined
          ? { Accept: 'application/json' }
          : { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (err) {
    throw new ApiError('network', err instanceof Error ? err.message : 'Request failed')
  }
  if (!res.ok) {
    const problem = await readProblem(res)
    throw new ApiError('problem', problem.title, problem)
  }
  if (res.status === 204) return { status: 204, data: undefined as T }
  let data: unknown
  try {
    data = await res.json()
  } catch {
    throw new ApiError('invalid_response', 'The API returned an unreadable response.')
  }
  // Every success body in the contract is a JSON object or array; null or a scalar would crash the screens.
  if (typeof data !== 'object' || data === null) {
    throw new ApiError('invalid_response', 'The API returned an unexpected response.')
  }
  return { status: res.status, data: data as T }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  return (await send<T>(method, path, body)).data
}

/** A list endpoint: anything but an array is an invalid response, not an empty list. */
async function requestList<T>(path: string): Promise<T[]> {
  const data = await request<unknown>('GET', path)
  if (!Array.isArray(data)) {
    throw new ApiError('invalid_response', 'The API returned an unexpected response.')
  }
  return data as T[]
}

const id = encodeURIComponent
const PAGE = 'take=500'

// Candidates
export const listCandidates = (): Promise<Candidate[]> =>
  requestList(`/candidates?${PAGE}`)

export const createCandidate = (name: string): Promise<Candidate> =>
  request('POST', '/candidates', { name })

export const updateCandidate = (candidateId: string, name: string): Promise<Candidate> =>
  request('PUT', `/candidates/${id(candidateId)}`, { name })

export const deleteCandidate = (candidateId: string): Promise<void> =>
  request('DELETE', `/candidates/${id(candidateId)}`)

// Employers (canonical, name only)
export interface ResolvedEmployer {
  employer: Employer
  /** true when the name was new (201); false when an existing employer was reused (200). */
  created: boolean
}

export async function resolveEmployer(name: string): Promise<ResolvedEmployer> {
  const result = await send<Employer>('POST', '/employers/resolve', { name })
  return { employer: result.data, created: result.status === 201 }
}

// Company lists
export const listCompanyLists = (): Promise<CompanyListSummary[]> =>
  requestList(`/company-lists?${PAGE}`)

export const getCompanyList = (listId: string): Promise<CompanyListDetail> =>
  request('GET', `/company-lists/${id(listId)}`)

export const createCompanyList = (input: CompanyListInput): Promise<CompanyListDetail> =>
  request('POST', '/company-lists', input)

export const updateCompanyList = (
  listId: string,
  input: CompanyListInput,
): Promise<CompanyListDetail> => request('PUT', `/company-lists/${id(listId)}`, input)

export const deleteCompanyList = (listId: string): Promise<void> =>
  request('DELETE', `/company-lists/${id(listId)}`)

export const addEmployerToList = (listId: string, employerId: string): Promise<void> =>
  request('PUT', `/company-lists/${id(listId)}/employers/${id(employerId)}`)

export const removeEmployerFromList = (
  listId: string,
  employerId: string,
): Promise<void> =>
  request('DELETE', `/company-lists/${id(listId)}/employers/${id(employerId)}`)

// Company-list import: paste preview and confirmed save (docs/api.md, "Company-list import").
export type ImportRowStatus =
  | 'new'
  | 'existing_employer'
  | 'already_in_list'
  | 'duplicate_in_input'
  | 'invalid'

export interface ImportPreviewRow {
  /** 1-based line number in the pasted text; blank lines still count. */
  rowNumber: number
  input: string
  name: string | null
  normalizedName: string | null
  status: ImportRowStatus
  existingEmployerId: string | null
  duplicateOfRow: number | null
  message: string
}

export interface ImportPreviewSummary {
  total: number
  new: number
  existingEmployer: number
  alreadyInList: number
  duplicateInInput: number
  invalid: number
}

export interface ImportPreview {
  rows: ImportPreviewRow[]
  summary: ImportPreviewSummary
}

export interface ImportResult {
  created: number
  reused: number
  addedToList: number
  skipped: number
}

/** A count from the API: a whole number, never negative. */
const isCount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0

/** A 1-based row number. */
const isRowNumber = (value: unknown): value is number => isCount(value) && value >= 1

const IMPORT_ROW_STATUSES: readonly string[] = [
  'new',
  'existing_employer',
  'already_in_list',
  'duplicate_in_input',
  'invalid',
]

const isImportRowStatus = (value: unknown): value is ImportRowStatus =>
  typeof value === 'string' && IMPORT_ROW_STATUSES.includes(value)

const unexpectedResponse = (): ApiError =>
  new ApiError('invalid_response', 'The API returned an unexpected response.')

const textOrNull = (value: unknown): string | null =>
  typeof value === 'string' ? value : null

function readImportPreview(data: unknown): ImportPreview {
  if (!isRecord(data) || !Array.isArray(data.rows) || !isRecord(data.summary)) {
    throw unexpectedResponse()
  }
  const s = data.summary
  if (
    !isCount(s.total) ||
    !isCount(s.new) ||
    !isCount(s.existingEmployer) ||
    !isCount(s.alreadyInList) ||
    !isCount(s.duplicateInInput) ||
    !isCount(s.invalid)
  ) {
    throw unexpectedResponse()
  }
  const rows = data.rows.map((row: unknown): ImportPreviewRow => {
    if (
      !isRecord(row) ||
      !isRowNumber(row.rowNumber) ||
      typeof row.input !== 'string' ||
      !isImportRowStatus(row.status)
    ) {
      throw unexpectedResponse()
    }
    return {
      rowNumber: row.rowNumber,
      input: row.input,
      name: textOrNull(row.name),
      normalizedName: textOrNull(row.normalizedName),
      status: row.status,
      existingEmployerId: textOrNull(row.existingEmployerId),
      duplicateOfRow: isRowNumber(row.duplicateOfRow) ? row.duplicateOfRow : null,
      message: typeof row.message === 'string' ? row.message : '',
    }
  })
  return {
    rows,
    summary: {
      total: s.total,
      new: s.new,
      existingEmployer: s.existingEmployer,
      alreadyInList: s.alreadyInList,
      duplicateInInput: s.duplicateInInput,
      invalid: s.invalid,
    },
  }
}

function readImportResult(data: unknown): ImportResult {
  if (
    !isRecord(data) ||
    !isCount(data.created) ||
    !isCount(data.reused) ||
    !isCount(data.addedToList) ||
    !isCount(data.skipped)
  ) {
    throw unexpectedResponse()
  }
  return {
    created: data.created,
    reused: data.reused,
    addedToList: data.addedToList,
    skipped: data.skipped,
  }
}

/** Parses the pasted text on the server and reports each row; writes nothing. */
export async function previewCompanyImport(
  listId: string,
  text: string,
): Promise<ImportPreview> {
  return readImportPreview(
    await request<unknown>('POST', `/company-lists/${id(listId)}/import/preview`, { text }),
  )
}

/** The confirmed save. The server parses the text again; any invalid row saves nothing (400). */
export async function importCompanies(listId: string, text: string): Promise<ImportResult> {
  return readImportResult(
    await request<unknown>('POST', `/company-lists/${id(listId)}/import`, { text }),
  )
}

// Search profiles
export const listSearchProfiles =(): Promise<SearchProfile[]> =>
  requestList(`/search-profiles?${PAGE}`)

export const createSearchProfile = (
  input: SearchProfileCreateInput,
): Promise<SearchProfile> =>
  request('POST', '/search-profiles', {
    ...input,
    // Omitted work modes mean "inherit"; an empty array is an explicit empty set.
    workModes: input.workModes ?? undefined,
  })

export const updateSearchProfile = (
  profileId: string,
  input: SearchProfileUpdateInput,
): Promise<SearchProfile> => request('PUT', `/search-profiles/${id(profileId)}`, input)

export const deleteSearchProfile = (profileId: string): Promise<void> =>
  request('DELETE', `/search-profiles/${id(profileId)}`)

export const selectCompanyList = (profileId: string, listId: string): Promise<void> =>
  request('PUT', `/search-profiles/${id(profileId)}/company-lists/${id(listId)}`)

export const unselectCompanyList = (profileId: string, listId: string): Promise<void> =>
  request('DELETE', `/search-profiles/${id(profileId)}/company-lists/${id(listId)}`)
