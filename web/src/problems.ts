import type { ApiError, Problem } from './api'

export interface ProblemView {
  code: string
  heading: string
  message: string
  /** Field errors of a validation problem, one line each. */
  details: string[]
  /** True when reloading the data is a sensible next step. */
  reloadable: boolean
}

const count = (n: number | undefined, one: string, many: string): string => {
  if (n === undefined) return many
  return n === 1 ? `1 ${one}` : `${n} ${many}`
}

/** A status code such as `pendingVerification` as a sentence start: "Pending verification". */
const statusName = (status: string | undefined): string =>
  status ? humanizeField(status) : 'The current status'

function duplicateReason(matchedOn: string | undefined): string {
  switch (matchedOn) {
    case 'canonicalUrl':
      return 'A job with the same apply link already exists.'
    case 'employerTitleLocation':
      return 'A job with the same company, title and location already exists.'
    default:
      return 'A very similar job already exists.'
  }
}

/** "defaultWorkModes" -> "Default work modes". */
export function humanizeField(field: string): string {
  const spaced = field.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase().trim()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

/** Messages the API reported for one field (field names match ignoring case). */
export function fieldMessages(error: ApiError | null, field: string): string[] {
  const errors = error?.problem?.errors
  if (!errors) return []
  const wanted = field.toLowerCase()
  return Object.entries(errors)
    .filter(([key]) => key.toLowerCase() === wanted)
    .flatMap(([, messages]) => messages)
}

function validationDetails(problem: Problem): string[] {
  return Object.entries(problem.errors ?? {}).flatMap(([field, messages]) =>
    messages.map((message) => (field ? `${humanizeField(field)}: ${message}` : message)),
  )
}

/**
 * Turns an API error into a user-facing state. `subject` names what the form
 * is about ("company list", "search profile") for the duplicate-name message.
 */
export function describeError(error: ApiError, subject = 'item'): ProblemView {
  if (error.kind === 'network') {
    return {
      code: 'network',
      heading: 'Cannot reach the API',
      // A failed fetch does not prove the request never arrived, so a write may have been applied.
      message: `No answer came back from the API (${error.message}). If you were saving a change, it may or may not have been applied: reload to see the current data before trying again.`,
      details: [],
      reloadable: true,
    }
  }
  if (error.kind === 'invalid_response' || !error.problem) {
    return {
      code: 'invalid_response',
      heading: 'Unreadable response',
      message: 'The API answered with something this page cannot read. Try again.',
      details: [],
      reloadable: true,
    }
  }

  const p = error.problem
  const view = (
    heading: string,
    message: string,
    extra: Partial<ProblemView> = {},
  ): ProblemView => ({
    code: p.code,
    heading,
    message,
    details: [],
    reloadable: false,
    ...extra,
  })

  switch (p.code) {
    case 'validation':
      return view('Check the form', 'Some fields need attention. Nothing was saved.', {
        details: validationDetails(p),
      })
    case 'not_found':
      return view(
        'No longer exists',
        'This item does not exist any more; it may have been deleted elsewhere. Reload to see the current data.',
        { reloadable: true },
      )
    case 'not_a_member':
      return view(
        'Already removed',
        'That association did not exist, so nothing was changed. Reload to see the current data.',
        { reloadable: true },
      )
    case 'duplicate_name':
      return view(
        'Name already in use',
        `Another ${subject} already uses this name. Names are compared ignoring case and extra spaces; choose a different one.`,
      )
    case 'candidate_has_profiles':
      return view(
        'Candidate still has search profiles',
        `${count(p.searchProfileCount, 'search profile', 'search profiles')} still belong to this candidate. Delete them first, then delete the candidate.`,
      )
    case 'company_list_in_use':
      return view(
        'List is in use',
        `${count(p.searchProfileCount, 'search profile', 'search profiles')} still select this list. Unselect it in those profiles first, then delete it.`,
      )
    case 'role_list_in_use':
      return view(
        'Role list is in use',
        `${count(p.searchProfileCount, 'search profile', 'search profiles')} still select this role list. Unselect it in those profiles first, then delete it.`,
      )
    case 'location_set_in_use':
      return view(
        'Location set is in use',
        `${count(p.searchProfileCount, 'search profile', 'search profiles')} still select this location set. Unselect it in those profiles first, then delete it.`,
      )
    case 'duplicate_title':
      return view(
        'Title already in this list',
        p.detail ??
          'This title or one of its aliases is already used in this role list. Titles and aliases share one namespace per list, compared ignoring case and extra spaces. Nothing was saved.',
      )
    case 'duplicate_location':
      return view(
        'Location already in this set',
        p.detail ??
          'This set already holds a location with the same label, scope and country. Nothing was saved.',
      )
    case 'employer_in_company_list':
      return view(
        'Company is still in a list',
        `This company is still in ${count(p.companyListCount, 'company list', 'company lists')}. Remove it from them first.`,
      )
    case 'employer_has_jobs':
      return view(
        'Company still has jobs',
        `${count(p.jobCount, 'job', 'jobs')} still belong to this company. Close or delete them first.`,
      )
    case 'duplicate_requisition':
      return view(
        'Same requisition id',
        'This company already has a job with the same requisition id, so this cannot be saved as a separate job. Open the existing job instead.',
      )
    case 'possible_duplicate':
      return view('Possible duplicate job', `${duplicateReason(p.matchedOn)} If this is a different job, confirm it to save anyway.`)
    case 'invalid_status_transition':
    case 'invalid_application_transition':
      return view(
        'Status change not allowed',
        `${statusName(p.currentStatus)} cannot be changed to ${statusName(p.requestedStatus).toLowerCase()}. Reload to see the current status.`,
        { reloadable: true },
      )
    case 'job_in_use':
      return view(
        'Job is in use',
        'An application or source listing still refers to this job, so it cannot be deleted. Mark it closed or expired to archive it.',
      )
    case 'candidate_has_applications':
      return view(
        'Candidate still has applications',
        `${count(p.applicationCount, 'application', 'applications')} still belong to this candidate, so the candidate cannot be deleted.`,
      )
    case 'application_exists':
      return view(
        'Application already exists',
        `This candidate already has an application for this job (${statusName(p.currentStatus).toLowerCase()}${p.isApplied ? ', applied' : ', not applied'}). Open it instead of creating another.`,
        { reloadable: true },
      )
    case 'already_applied':
      return view(
        'Already applied',
        'This application is already confirmed as applied. Reload to see its current state; use a correction to change the date.',
        { reloadable: true },
      )
    case 'not_applied':
      return view(
        'Not applied',
        'This application is not confirmed as applied, so there is nothing to remove. Reload to see its current state.',
        { reloadable: true },
      )
    case 'invalid_correction':
      return view(
        'Correction not possible',
        p.detail ?? 'The correction would leave an inconsistent state or change nothing.',
      )
    case 'concurrent_modification':
      return view(
        'Changed by someone else',
        'This record was changed or deleted after it was loaded or while your request ran. Reload to see its current state, then try again.',
        { reloadable: true },
      )
    default:
      if (p.status >= 500) {
        return view(
          'Server error',
          `The API reported an error (HTTP ${p.status}). Nothing may have been saved; try again.`,
          { reloadable: true },
        )
      }
      return view(
        p.status === 409 ? 'Conflict' : 'Request failed',
        p.detail ?? p.title,
        { reloadable: true },
      )
  }
}
