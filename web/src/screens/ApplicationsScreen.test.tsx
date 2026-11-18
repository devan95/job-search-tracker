import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, problem } from '../test/mockApi'
import ApplicationsScreen from './ApplicationsScreen'
import JobsScreen from './JobsScreen'

afterEach(() => vi.restoreAllMocks())

const jobRef = { id: 'j1', title: 'Backend Engineer', employer: { id: 'e1', name: 'Acme' }, status: 'open' }

const savedAudit = {
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

const application = (over: Record<string, unknown> = {}) => ({
  id: 'a1',
  candidateId: 'c1',
  job: jobRef,
  status: 'saved',
  isApplied: false,
  method: null,
  appliedAtUtc: null,
  appliedConfirmedAtUtc: null,
  createdAtUtc: '2026-11-18T05:00:00Z',
  updatedAtUtc: '2026-11-18T05:00:00Z',
  auditTrail: [savedAudit],
  ...over,
})

const candidates = { status: 200, body: [{ id: 'c1', name: 'Asha Rao' }] }

/** Routes for one application whose state the test advances through the closure. */
function applicationRoutes(initial = application()) {
  let current = initial
  const routes = {
    'GET /api/candidates': candidates,
    'GET /api/applications': () => ({ status: 200, body: [current] }),
    'GET /api/applications/a1': () => ({ status: 200, body: current }),
  }
  return {
    routes,
    set: (next: ReturnType<typeof application>) => {
      current = next
    },
  }
}

describe('ApplicationsScreen: list', () => {
  it('shows an empty state', async () => {
    mockApi({ 'GET /api/candidates': candidates, 'GET /api/applications': { status: 200, body: [] } })
    render(<ApplicationsScreen />)
    expect(await screen.findByText(/No applications yet/)).toBeInTheDocument()
  })

  it('lists applications with the candidate, status and applied state', async () => {
    const { routes } = applicationRoutes()
    mockApi(routes)
    render(<ApplicationsScreen />)
    const row = await screen.findByRole('button', { name: /Open application: Backend Engineer at Acme/ })
    expect(row).toHaveTextContent('Asha Rao')
    expect(row).toHaveTextContent('Saved')
    expect(row).toHaveTextContent('Not applied')
  })

  it('passes the filters to the API', async () => {
    const { routes } = applicationRoutes()
    const calls = mockApi(routes)
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await screen.findByRole('button', { name: /Open application/ })

    await user.selectOptions(await screen.findByLabelText('Candidate filter'), 'c1')
    await user.selectOptions(screen.getByLabelText('Status filter'), 'applied')

    await screen.findByText('Loading applications…').catch(() => undefined)
    const last = calls.filter((c) => c.path === '/api/applications').at(-1)
    expect(last?.search).toContain('candidateId=c1')
    expect(last?.search).toContain('status=applied')
  })
})

describe('ApplicationsScreen: confirming an application', () => {
  it('never marks a saved application as applied until the user confirms', async () => {
    const { routes } = applicationRoutes()
    const calls = mockApi(routes)
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))

    expect(await screen.findByTestId('application-applied')).toHaveTextContent('No')
    expect(screen.getByText(/never marks it as applied/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Confirm as applied' }))
    // Opening the form is not a confirmation, and the button stays off until the box is ticked.
    expect(screen.getByRole('button', { name: 'Confirm applied' })).toBeDisabled()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
    expect(screen.getByTestId('application-applied')).toHaveTextContent('No')
  })

  it('confirms with the tick, shows the manual method and the date, and records history', async () => {
    const state = applicationRoutes()
    const calls = mockApi({
      ...state.routes,
      'POST /api/applications/a1/confirm-applied': () => {
        state.set(
          application({
            status: 'applied',
            isApplied: true,
            method: 'manual',
            appliedAtUtc: '2026-11-18T06:00:00Z',
            appliedConfirmedAtUtc: '2026-11-18T06:01:00Z',
            auditTrail: [
              savedAudit,
              {
                ...savedAudit,
                action: 'appliedConfirmed',
                oldStatus: 'saved',
                oldIsApplied: false,
                newStatus: 'applied',
                newIsApplied: true,
                newAppliedAtUtc: '2026-11-18T06:00:00Z',
              },
            ],
          }),
        )
        return { status: 200, body: application() }
      },
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Confirm as applied' }))

    await user.click(screen.getByLabelText('I submitted this application myself.'))
    await user.click(screen.getByRole('button', { name: 'Confirm applied' }))

    expect(await screen.findByText('Manual')).toBeInTheDocument()
    expect(screen.getByTestId('application-status')).toHaveTextContent('Applied')
    expect(screen.getByTestId('application-applied')).toHaveTextContent('Yes')
    const history = screen.getByRole('list', { name: 'Application history' })
    expect(within(history).getByText('Confirmed as applied')).toBeInTheDocument()
    expect(calls.find((c) => c.path.endsWith('/confirm-applied'))?.body).toEqual({ confirmed: true })
  })

  it('sends the chosen date and time as a UTC instant', async () => {
    const state = applicationRoutes()
    const calls = mockApi({
      ...state.routes,
      'POST /api/applications/a1/confirm-applied': { status: 200, body: application() },
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Confirm as applied' }))
    await user.click(screen.getByLabelText('I submitted this application myself.'))
    await user.type(screen.getByLabelText(/Date and time applied/), '2026-11-17T09:30')
    await user.click(screen.getByRole('button', { name: 'Confirm applied' }))

    await screen.findByRole('heading', { name: /Application:/ })
    const body = calls.find((c) => c.path.endsWith('/confirm-applied'))?.body as {
      confirmed: boolean
      appliedAtUtc: string
    }
    expect(body.confirmed).toBe(true)
    expect(body.appliedAtUtc).toMatch(/Z$/)
  })

  it('rejects a date in the future without calling the API', async () => {
    const state = applicationRoutes()
    const calls = mockApi(state.routes)
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Confirm as applied' }))
    await user.click(screen.getByLabelText('I submitted this application myself.'))
    await user.type(screen.getByLabelText(/Date and time applied/), '2099-01-01T10:00')
    await user.click(screen.getByRole('button', { name: 'Confirm applied' }))

    expect(await screen.findByText(/cannot be in the future/)).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('reports an application that was already confirmed elsewhere', async () => {
    const state = applicationRoutes()
    mockApi({
      ...state.routes,
      'POST /api/applications/a1/confirm-applied': problem(409, 'already_applied'),
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Confirm as applied' }))
    await user.click(screen.getByLabelText('I submitted this application myself.'))
    await user.click(screen.getByRole('button', { name: 'Confirm applied' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Already applied')
  })
})

describe('ApplicationsScreen: later statuses', () => {
  const applied = () =>
    application({
      status: 'applied',
      isApplied: true,
      method: 'manual',
      appliedAtUtc: '2026-11-18T06:00:00Z',
      appliedConfirmedAtUtc: '2026-11-18T06:01:00Z',
    })

  it('offers only valid next statuses, never Applied or Saved', async () => {
    mockApi(applicationRoutes(applied()).routes)
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))

    const select = await screen.findByLabelText('Change status to')
    const options = within(select).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Choose a status', 'Interview', 'Offer', 'Rejected', 'Withdrawn'])
  })

  it('changes the status with an optional reason and shows it in the history', async () => {
    const state = applicationRoutes(applied())
    const calls = mockApi({
      ...state.routes,
      'POST /api/applications/a1/status': () => {
        state.set(
          application({
            status: 'interview',
            isApplied: true,
            method: 'manual',
            appliedAtUtc: '2026-11-18T06:00:00Z',
            auditTrail: [
              savedAudit,
              {
                ...savedAudit,
                action: 'statusChanged',
                oldStatus: 'applied',
                oldIsApplied: true,
                newStatus: 'interview',
                newIsApplied: true,
                reason: 'Recruiter call booked',
              },
            ],
          }),
        )
        return { status: 200, body: application() }
      },
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))

    await user.selectOptions(await screen.findByLabelText('Change status to'), 'interview')
    await user.type(screen.getByLabelText('Reason (optional)'), 'Recruiter call booked')
    await user.click(screen.getByRole('button', { name: 'Update status' }))

    expect(await screen.findByText('Reason: Recruiter call booked')).toBeInTheDocument()
    expect(screen.getByTestId('application-status')).toHaveTextContent('Interview')
    expect(calls.find((c) => c.path.endsWith('/status'))?.body).toEqual({
      status: 'interview',
      reason: 'Recruiter call booked',
    })
  })

  it('requires a status to be chosen', async () => {
    const calls = mockApi(applicationRoutes(applied()).routes)
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Update status' }))

    expect(await screen.findByText('Choose a status.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows an invalid transition as a clear error', async () => {
    mockApi({
      ...applicationRoutes(applied()).routes,
      'POST /api/applications/a1/status': problem(409, 'invalid_application_transition', {
        currentStatus: 'applied',
        requestedStatus: 'rejected',
      }),
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.selectOptions(await screen.findByLabelText('Change status to'), 'rejected')
    await user.click(screen.getByRole('button', { name: 'Update status' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Status change not allowed')
  })

  it('removes applied only with a reason', async () => {
    const state = applicationRoutes(applied())
    const calls = mockApi({
      ...state.routes,
      'POST /api/applications/a1/unapply': () => {
        state.set(application())
        return { status: 200, body: application() }
      },
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Remove applied' }))

    const form = screen.getByRole('form', { name: 'Remove applied' })
    await user.click(within(form).getByRole('button', { name: 'Remove applied' }))
    expect(screen.getByText('A short reason is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await user.type(within(form).getByLabelText('Reason'), 'Clicked by mistake')
    await user.click(within(form).getByRole('button', { name: 'Remove applied' }))

    expect(await screen.findByTestId('application-applied')).toHaveTextContent('No')
    expect(calls.find((c) => c.path.endsWith('/unapply'))?.body).toEqual({ reason: 'Clicked by mistake' })
  })

  it('records a correction with a reason', async () => {
    const calls = mockApi({
      ...applicationRoutes(applied()).routes,
      'POST /api/applications/a1/correct': { status: 200, body: applied() },
    })
    const user = userEvent.setup()
    render(<ApplicationsScreen />)
    await user.click(await screen.findByRole('button', { name: /Open application/ }))
    await user.click(await screen.findByRole('button', { name: 'Correct' }))

    const form = screen.getByRole('form', { name: 'Correct application' })
    await user.click(within(form).getByRole('button', { name: 'Save correction' }))
    expect(screen.getByText(/Choose a new status or a new date/)).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText('New status'), 'withdrawn')
    await user.type(within(form).getByLabelText('Reason for the correction'), 'Entered wrong status')
    await user.click(within(form).getByRole('button', { name: 'Save correction' }))

    await screen.findByRole('heading', { name: /Application:/ })
    expect(calls.find((c) => c.path.endsWith('/correct'))?.body).toEqual({
      status: 'withdrawn',
      reason: 'Entered wrong status',
    })
  })
})

describe('JobsScreen: applications for a job', () => {
  const jobDetail = {
    id: 'j1',
    employer: { id: 'e1', name: 'Acme' },
    title: 'Backend Engineer',
    applyUrl: null,
    location: null,
    workMode: null,
    employmentType: null,
    status: 'open',
    createdVia: 'manual',
    createdAtUtc: '2026-11-18T05:00:00Z',
    updatedAtUtc: '2026-11-18T05:00:00Z',
    canonicalUrl: null,
    department: null,
    experienceMinYears: null,
    experienceMaxYears: null,
    requisitionId: null,
    postedDate: null,
    description: null,
    foundOn: null,
    statusHistory: [],
  }

  const base = {
    'GET /api/jobs': { status: 200, body: [jobDetail] },
    'GET /api/jobs/j1': { status: 200, body: jobDetail },
    'GET /api/candidates': candidates,
  }

  async function openJobAndChooseCandidate() {
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.selectOptions(await screen.findByLabelText('Candidate'), 'c1')
    return user
  }

  it('saves an application as Saved, not applied', async () => {
    let created = false
    const calls = mockApi({
      ...base,
      'GET /api/applications': () => ({ status: 200, body: created ? [application()] : [] }),
      'POST /api/applications': () => {
        created = true
        return { status: 201, body: application() }
      },
      'GET /api/applications/a1': { status: 200, body: application() },
    })
    const user = await openJobAndChooseCandidate()

    expect(await screen.findByText(/No application recorded for Asha Rao/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Track application' }))

    expect(await screen.findByTestId('application-status')).toHaveTextContent('Saved')
    expect(screen.getByTestId('application-applied')).toHaveTextContent('No')
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ candidateId: 'c1', jobId: 'j1' })
  })

  it('shows an existing application instead of offering a second one', async () => {
    mockApi({
      ...base,
      'GET /api/applications': { status: 200, body: [application()] },
      'GET /api/applications/a1': { status: 200, body: application() },
    })
    await openJobAndChooseCandidate()

    expect(await screen.findByText(/already has an application for this job/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Track application' })).not.toBeInTheDocument()
  })

  it('shows the existing application when a duplicate is rejected by the API', async () => {
    let existing = false
    mockApi({
      ...base,
      'GET /api/applications': () => ({ status: 200, body: existing ? [application()] : [] }),
      'POST /api/applications': () => {
        existing = true
        return problem(409, 'application_exists', { currentStatus: 'saved', isApplied: false })
      },
      'GET /api/applications/a1': { status: 200, body: application() },
    })
    const user = await openJobAndChooseCandidate()
    await user.click(await screen.findByRole('button', { name: 'Track application' }))

    expect(await screen.findByText(/already has an application for this job/)).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('Application already exists')
  })

  it('asks for a candidate first when there are none', async () => {
    mockApi({ ...base, 'GET /api/candidates': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    expect(await screen.findByText(/Add a candidate first/)).toBeInTheDocument()
  })
})
