import type {
  ApplicationAuditItem,
  ApplicationStatus,
  EmploymentType,
  Job,
  JobInput,
  JobStatus,
  UserJobStatus,
  WorkMode,
} from './api'
import { MAX_NAME_LENGTH } from './validation'

// ---------------------------------------------------------------------------
// Labels
// ---------------------------------------------------------------------------

/** Label for an API value; an unknown value is shown as it came. */
export const labelFor = (labels: Readonly<Record<string, string>>, value: string): string =>
  labels[value] ?? value

export const JOB_STATUSES: readonly JobStatus[] = [
  'open',
  'reopened',
  'closed',
  'expired',
  'unavailable',
  'new',
  'verificationPending',
]

export const JOB_STATUS_LABEL: Record<JobStatus, string> = {
  new: 'New',
  open: 'Open',
  closed: 'Closed',
  expired: 'Expired',
  reopened: 'Open again',
  unavailable: 'Unavailable',
  verificationPending: 'Verification pending',
}

/** The button that requests each user-settable job status. */
export const JOB_STATUS_ACTION_LABEL: Record<UserJobStatus, string> = {
  closed: 'Mark closed',
  expired: 'Mark expired',
  unavailable: 'Mark unavailable',
  reopened: 'Open again',
}

export const EMPLOYMENT_TYPES: readonly EmploymentType[] = [
  'fullTime',
  'partTime',
  'contract',
  'internship',
  'temporary',
  'other',
]

export const EMPLOYMENT_TYPE_LABEL: Record<EmploymentType, string> = {
  fullTime: 'Full-time',
  partTime: 'Part-time',
  contract: 'Contract',
  internship: 'Internship',
  temporary: 'Temporary',
  other: 'Other',
}

export const APPLICATION_STATUSES: readonly ApplicationStatus[] = [
  'saved',
  'applied',
  'pendingVerification',
  'interview',
  'offer',
  'rejected',
  'withdrawn',
]

export const APPLICATION_STATUS_LABEL: Record<ApplicationStatus, string> = {
  saved: 'Saved',
  applied: 'Applied',
  pendingVerification: 'Pending verification',
  interview: 'Interview',
  offer: 'Offer',
  rejected: 'Rejected',
  withdrawn: 'Withdrawn',
}

export const AUDIT_ACTION_LABEL: Record<string, string> = {
  created: 'Saved for tracking',
  statusChanged: 'Status changed',
  appliedConfirmed: 'Confirmed as applied',
  unapplied: 'Applied removed',
  corrected: 'Corrected',
}

export const methodLabel = (method: string | null): string =>
  method === null ? '—' : method === 'manual' ? 'Manual' : method

// ---------------------------------------------------------------------------
// Status rules (mirror docs/api.md; the API stays the authority)
// ---------------------------------------------------------------------------

/** Job statuses a user may request from `current`. System-only statuses allow none. */
export function allowedJobStatuses(current: JobStatus): UserJobStatus[] {
  if (current === 'new' || current === 'verificationPending') return []
  if (current === 'open' || current === 'reopened') return ['closed', 'expired', 'unavailable']
  const others = (['expired', 'unavailable', 'closed'] as const).filter((s) => s !== current)
  return ['reopened', ...others]
}

/**
 * Statuses the ordinary status endpoint accepts from `current`. `applied` is never among them: it
 * comes only from the explicit confirmation, and `saved` only from un-applying or a correction.
 */
export function nextApplicationStatuses(current: ApplicationStatus): ApplicationStatus[] {
  const next: ApplicationStatus[] = []
  if (current === 'saved') next.push('pendingVerification')
  if (current === 'applied') next.push('interview')
  if (current === 'applied' || current === 'interview') next.push('offer')
  if (
    current === 'pendingVerification' ||
    current === 'applied' ||
    current === 'interview' ||
    current === 'offer'
  ) {
    next.push('rejected')
  }
  if (current !== 'withdrawn') next.push('withdrawn')
  return next
}

/**
 * Statuses a correction can set, by the application's applied state (docs/api.md, "Correct"):
 * applied, interview and offer need a confirmed application; saved and pendingVerification are
 * not allowed on an applied one. The API stays the authority.
 */
export function correctionStatuses(isApplied: boolean): ApplicationStatus[] {
  return APPLICATION_STATUSES.filter((status) =>
    isApplied
      ? status !== 'saved' && status !== 'pendingVerification'
      : status !== 'applied' && status !== 'interview' && status !== 'offer',
  )
}

// ---------------------------------------------------------------------------
// Dates
// ---------------------------------------------------------------------------

/** A UTC timestamp from the API in the viewer's local time; unreadable values are shown as they came. */
export function formatUtc(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

/**
 * Reads a `datetime-local` value ("2026-11-18T10:30", the viewer's local time) as the UTC instant
 * the API expects. Blank means "now" (iso null). A future time is rejected, as the API does.
 */
export function parseAppliedAt(
  value: string,
  now: Date = new Date(),
): { iso: string | null; error: string | null } {
  if (value.trim() === '') return { iso: null, error: null }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return { iso: null, error: 'Enter a valid date and time.' }
  }
  if (date.getTime() > now.getTime()) {
    return { iso: null, error: 'The date and time applied cannot be in the future.' }
  }
  return { iso: date.toISOString(), error: null }
}

// ---------------------------------------------------------------------------
// Application audit trail
// ---------------------------------------------------------------------------

const statusText = (status: string): string => labelFor(APPLICATION_STATUS_LABEL, status)
const yesNo = (value: boolean): string => (value ? 'Yes' : 'No')

/** One line saying what an audit row changed. */
export function describeAuditChange(entry: ApplicationAuditItem): string {
  const parts: string[] = []
  parts.push(
    entry.oldStatus === null || entry.oldStatus === entry.newStatus
      ? `Status: ${statusText(entry.newStatus)}`
      : `Status: ${statusText(entry.oldStatus)} → ${statusText(entry.newStatus)}`,
  )
  if (entry.oldIsApplied === null) {
    parts.push(`Applied: ${yesNo(entry.newIsApplied)}`)
  } else if (entry.oldIsApplied !== entry.newIsApplied) {
    parts.push(`Applied: ${yesNo(entry.oldIsApplied)} → ${yesNo(entry.newIsApplied)}`)
  }
  if (entry.oldAppliedAtUtc !== entry.newAppliedAtUtc) {
    parts.push(
      `Date applied: ${formatUtc(entry.oldAppliedAtUtc)} → ${formatUtc(entry.newAppliedAtUtc)}`,
    )
  }
  return parts.join('; ')
}

// ---------------------------------------------------------------------------
// Job form
// ---------------------------------------------------------------------------

export interface JobFormValues {
  employer: string
  title: string
  applyUrl: string
  location: string
  workMode: '' | WorkMode
  employmentType: '' | EmploymentType
  department: string
  experienceMinYears: string
  experienceMaxYears: string
  requisitionId: string
  postedDate: string
  description: string
  foundOn: string
}

export const emptyJobForm: JobFormValues = {
  employer: '',
  title: '',
  applyUrl: '',
  location: '',
  workMode: '',
  employmentType: '',
  department: '',
  experienceMinYears: '',
  experienceMaxYears: '',
  requisitionId: '',
  postedDate: '',
  description: '',
  foundOn: '',
}

export function jobToForm(job: Job): JobFormValues {
  return {
    employer: job.employer.name,
    title: job.title,
    applyUrl: job.applyUrl ?? '',
    location: job.location ?? '',
    workMode: job.workMode ?? '',
    employmentType: job.employmentType ?? '',
    department: job.department ?? '',
    experienceMinYears: job.experienceMinYears === null ? '' : String(job.experienceMinYears),
    experienceMaxYears: job.experienceMaxYears === null ? '' : String(job.experienceMaxYears),
    requisitionId: job.requisitionId ?? '',
    postedDate: job.postedDate ?? '',
    description: job.description ?? '',
    foundOn: job.foundOn ?? '',
  }
}

export type JobFormErrors = Partial<Record<keyof JobFormValues, string>>

const MAX_REQUISITION_LENGTH = 100
const MAX_URL_LENGTH = 2000
const MAX_DESCRIPTION_LENGTH = 50_000
const MAX_YEARS = 60

export function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

/** A whole number 0 to 60, null when blank, 'invalid' otherwise. */
function parseYears(value: string): number | 'invalid' | null {
  const trimmed = value.trim()
  if (trimmed === '') return null
  if (!/^\d+$/.test(trimmed)) return 'invalid'
  const years = Number(trimmed)
  return years <= MAX_YEARS ? years : 'invalid'
}

/** Client-side check that mirrors the API rules; the API stays the authority. */
export function validateJobForm(values: JobFormValues): JobFormErrors {
  const errors: JobFormErrors = {}
  const tooLong = (value: string, max: number, key: keyof JobFormValues, what: string) => {
    if (value.trim().length > max) errors[key] = `${what} can be at most ${max} characters.`
  }

  if (values.employer.trim() === '') errors.employer = 'A company name is required.'
  else tooLong(values.employer, MAX_NAME_LENGTH, 'employer', 'A company name')

  if (values.title.trim() === '') errors.title = 'A title is required.'
  else tooLong(values.title, MAX_NAME_LENGTH, 'title', 'A title')

  const url = values.applyUrl.trim()
  if (url !== '') {
    if (url.length > MAX_URL_LENGTH) {
      errors.applyUrl = `A link can be at most ${MAX_URL_LENGTH} characters.`
    } else if (!isHttpUrl(url)) {
      errors.applyUrl = 'Enter a full link starting with http:// or https://.'
    }
  }

  tooLong(values.location, MAX_NAME_LENGTH, 'location', 'A location')
  tooLong(values.department, MAX_NAME_LENGTH, 'department', 'A department')
  tooLong(values.foundOn, MAX_NAME_LENGTH, 'foundOn', 'Found on')
  tooLong(values.requisitionId, MAX_REQUISITION_LENGTH, 'requisitionId', 'A requisition id')
  tooLong(values.description, MAX_DESCRIPTION_LENGTH, 'description', 'A description')

  const min = parseYears(values.experienceMinYears)
  const max = parseYears(values.experienceMaxYears)
  if (min === 'invalid') errors.experienceMinYears = `Enter a whole number from 0 to ${MAX_YEARS}.`
  if (max === 'invalid') errors.experienceMaxYears = `Enter a whole number from 0 to ${MAX_YEARS}.`
  if (typeof min === 'number' && typeof max === 'number' && min > max) {
    errors.experienceMaxYears = 'The maximum cannot be less than the minimum.'
  }

  const posted = values.postedDate.trim()
  if (posted !== '' && !/^\d{4}-\d{2}-\d{2}$/.test(posted)) {
    errors.postedDate = 'Use the format yyyy-mm-dd.'
  }
  return errors
}

const textOrNull = (value: string): string | null => {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

const yearsOrNull = (value: string): number | null => {
  const trimmed = value.trim()
  return trimmed === '' ? null : Number(trimmed)
}

/** The request body for create and PUT. Blank optional fields become null (cleared). */
export function jobFormToInput(values: JobFormValues, confirmDifferentJob: boolean): JobInput {
  return {
    employer: values.employer.trim(),
    title: values.title.trim(),
    applyUrl: textOrNull(values.applyUrl),
    location: textOrNull(values.location),
    workMode: values.workMode === '' ? null : values.workMode,
    employmentType: values.employmentType === '' ? null : values.employmentType,
    department: textOrNull(values.department),
    experienceMinYears: yearsOrNull(values.experienceMinYears),
    experienceMaxYears: yearsOrNull(values.experienceMaxYears),
    requisitionId: textOrNull(values.requisitionId),
    postedDate: textOrNull(values.postedDate),
    description: textOrNull(values.description),
    foundOn: textOrNull(values.foundOn),
    confirmDifferentJob,
  }
}
