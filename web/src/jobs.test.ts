import { describe, expect, it } from 'vitest'
import { ApiError, type ApplicationAuditItem, type Job, type Problem } from './api'
import {
  allowedJobStatuses,
  describeAuditChange,
  emptyJobForm,
  jobFormToInput,
  jobToForm,
  nextApplicationStatuses,
  parseAppliedAt,
  validateJobForm,
} from './jobs'
import { describeError } from './problems'

const problemError = (problem: Partial<Problem> & { code: string; status: number }) =>
  new ApiError('problem', 'title', { title: 'title', ...problem })

describe('allowedJobStatuses', () => {
  it('lets an open job be closed, expired or marked unavailable', () => {
    expect(allowedJobStatuses('open')).toEqual(['closed', 'expired', 'unavailable'])
    expect(allowedJobStatuses('reopened')).toEqual(['closed', 'expired', 'unavailable'])
  })

  it('lets an ended job be opened again or moved to another ended status', () => {
    expect(allowedJobStatuses('closed')).toEqual(['reopened', 'expired', 'unavailable'])
    expect(allowedJobStatuses('expired')).toEqual(['reopened', 'unavailable', 'closed'])
  })

  it('offers nothing for system-only statuses', () => {
    expect(allowedJobStatuses('new')).toEqual([])
    expect(allowedJobStatuses('verificationPending')).toEqual([])
  })
})

describe('nextApplicationStatuses', () => {
  it('never offers applied: only the explicit confirmation sets it', () => {
    for (const status of [
      'saved',
      'applied',
      'pendingVerification',
      'interview',
      'offer',
      'rejected',
      'withdrawn',
    ] as const) {
      expect(nextApplicationStatuses(status)).not.toContain('applied')
    }
  })

  it('never offers saved: only un-applying or a correction returns to it', () => {
    expect(nextApplicationStatuses('applied')).not.toContain('saved')
  })

  it('moves an applied application on to interview, offer, rejected or withdrawn', () => {
    expect(nextApplicationStatuses('applied')).toEqual(['interview', 'offer', 'rejected', 'withdrawn'])
  })

  it('offers nothing once withdrawn', () => {
    expect(nextApplicationStatuses('withdrawn')).toEqual([])
  })
})

describe('parseAppliedAt', () => {
  const now = new Date('2026-11-18T10:00:00Z')

  it('treats a blank value as "now"', () => {
    expect(parseAppliedAt('  ', now)).toEqual({ iso: null, error: null })
  })

  it('rejects a time in the future', () => {
    const result = parseAppliedAt('2099-01-01T10:00', now)
    expect(result.iso).toBeNull()
    expect(result.error).toMatch(/future/)
  })

  it('returns a UTC instant for a past local time', () => {
    const result = parseAppliedAt('2026-11-17T09:30', now)
    expect(result.error).toBeNull()
    expect(result.iso).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/)
  })

  it('rejects an unreadable value', () => {
    expect(parseAppliedAt('not a date', now).error).toMatch(/valid date/)
  })
})

describe('validateJobForm', () => {
  const valid = { ...emptyJobForm, employer: 'Acme', title: 'Engineer' }

  it('accepts the two required fields alone', () => {
    expect(validateJobForm(valid)).toEqual({})
  })

  it('requires a company and a title', () => {
    expect(validateJobForm(emptyJobForm)).toEqual({
      employer: 'A company name is required.',
      title: 'A title is required.',
    })
  })

  it('requires an http or https apply link', () => {
    expect(validateJobForm({ ...valid, applyUrl: 'ftp://x.test/a' }).applyUrl).toBeDefined()
    expect(validateJobForm({ ...valid, applyUrl: 'not a link' }).applyUrl).toBeDefined()
    expect(validateJobForm({ ...valid, applyUrl: 'https://x.test/jobs/1' })).toEqual({})
  })

  it('checks the experience range', () => {
    expect(validateJobForm({ ...valid, experienceMinYears: '-1' }).experienceMinYears).toBeDefined()
    expect(validateJobForm({ ...valid, experienceMaxYears: '61' }).experienceMaxYears).toBeDefined()
    expect(
      validateJobForm({ ...valid, experienceMinYears: '5', experienceMaxYears: '2' })
        .experienceMaxYears,
    ).toMatch(/cannot be less/)
    expect(
      validateJobForm({ ...valid, experienceMinYears: '2', experienceMaxYears: '5' }),
    ).toEqual({})
  })

  it('checks the posted date format and the length limits', () => {
    expect(validateJobForm({ ...valid, postedDate: '18/11/2026' }).postedDate).toBeDefined()
    expect(validateJobForm({ ...valid, title: 'x'.repeat(201) }).title).toBeDefined()
    expect(validateJobForm({ ...valid, requisitionId: 'x'.repeat(101) }).requisitionId).toBeDefined()
  })
})

describe('jobFormToInput', () => {
  it('trims text and turns blank optional fields into null so a PUT clears them', () => {
    const input = jobFormToInput(
      { ...emptyJobForm, employer: ' Acme ', title: ' Engineer ', location: '   ' },
      false,
    )
    expect(input).toEqual({
      employer: 'Acme',
      title: 'Engineer',
      applyUrl: null,
      location: null,
      workMode: null,
      employmentType: null,
      department: null,
      experienceMinYears: null,
      experienceMaxYears: null,
      requisitionId: null,
      postedDate: null,
      description: null,
      foundOn: null,
      confirmDifferentJob: false,
    })
  })

  it('sends numbers for experience and carries the duplicate confirmation', () => {
    const input = jobFormToInput(
      { ...emptyJobForm, employer: 'A', title: 'T', experienceMinYears: '2', experienceMaxYears: '5' },
      true,
    )
    expect(input).toMatchObject({ experienceMinYears: 2, experienceMaxYears: 5, confirmDifferentJob: true })
  })

  it('round-trips a job through the form', () => {
    const job = {
      id: 'j1',
      employer: { id: 'e1', name: 'Acme' },
      title: 'Engineer',
      applyUrl: 'https://x.test/1',
      location: 'Pune',
      workMode: 'hybrid',
      employmentType: 'fullTime',
      status: 'open',
      createdVia: 'manual',
      createdAtUtc: '2026-11-18T05:00:00Z',
      updatedAtUtc: '2026-11-18T05:00:00Z',
      canonicalUrl: null,
      department: 'Platform',
      experienceMinYears: 2,
      experienceMaxYears: null,
      requisitionId: 'R-1',
      postedDate: '2026-11-01',
      description: 'Build things',
      foundOn: 'Company site',
      statusHistory: [],
    } as Job
    const input = jobFormToInput(jobToForm(job), false)
    expect(input).toMatchObject({
      employer: 'Acme',
      title: 'Engineer',
      applyUrl: 'https://x.test/1',
      workMode: 'hybrid',
      employmentType: 'fullTime',
      experienceMinYears: 2,
      experienceMaxYears: null,
      requisitionId: 'R-1',
    })
  })
})

describe('describeAuditChange', () => {
  const base: ApplicationAuditItem = {
    action: 'created',
    actor: 'user',
    changedAtUtc: '2026-11-18T05:00:00Z',
    oldStatus: null,
    oldIsApplied: null,
    oldAppliedAtUtc: null,
    newStatus: 'saved',
    newIsApplied: false,
    newAppliedAtUtc: null,
    reason: null,
  }

  it('describes a newly saved application as not applied', () => {
    expect(describeAuditChange(base)).toBe('Status: Saved; Applied: No')
  })

  it('shows the status and applied flag that changed', () => {
    const text = describeAuditChange({
      ...base,
      action: 'appliedConfirmed',
      oldStatus: 'saved',
      oldIsApplied: false,
      newStatus: 'applied',
      newIsApplied: true,
      newAppliedAtUtc: '2026-11-18T04:00:00Z',
    })
    expect(text).toContain('Status: Saved → Applied')
    expect(text).toContain('Applied: No → Yes')
    expect(text).toContain('Date applied:')
  })
})

describe('describeError for jobs and applications', () => {
  it('explains a possible duplicate and what it matched on', () => {
    const view = describeError(
      problemError({ status: 409, code: 'possible_duplicate', matchedOn: 'canonicalUrl' }),
    )
    expect(view.heading).toBe('Possible duplicate job')
    expect(JSON.stringify(view)).toContain('same apply link')
  })

  it('explains the other documented 409 codes', () => {
    const cases: [Partial<Problem> & { code: string; status: number }, string][] = [
      [{ status: 409, code: 'duplicate_requisition' }, 'Same requisition id'],
      [{ status: 409, code: 'employer_has_jobs', jobCount: 2 }, 'Company still has jobs'],
      [{ status: 409, code: 'job_in_use' }, 'Job is in use'],
      [{ status: 409, code: 'candidate_has_applications', applicationCount: 1 }, 'Candidate still has applications'],
      [{ status: 409, code: 'application_exists', currentStatus: 'saved', isApplied: false }, 'Application already exists'],
      [{ status: 409, code: 'already_applied' }, 'Already applied'],
      [{ status: 409, code: 'not_applied' }, 'Not applied'],
      [{ status: 409, code: 'invalid_correction' }, 'Correction not possible'],
      [{ status: 409, code: 'concurrent_modification' }, 'Changed by someone else'],
      [{ status: 409, code: 'invalid_status_transition', currentStatus: 'open', requestedStatus: 'open' }, 'Status change not allowed'],
    ]
    for (const [problem, heading] of cases) {
      expect(describeError(problemError(problem)).heading).toBe(heading)
    }
  })
})
