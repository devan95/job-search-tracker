import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { deferred, mockApi, problem, type MockHandler, type MockReply } from '../test/mockApi'
import JobsScreen from './JobsScreen'

afterEach(() => vi.restoreAllMocks())

const job = (over: Record<string, unknown> = {}) => ({
  id: 'j1',
  employer: { id: 'e1', name: 'Acme' },
  title: 'Backend Engineer',
  applyUrl: null,
  location: 'Pune',
  workMode: 'hybrid',
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
  statusHistory: [{ fromStatus: null, toStatus: 'open', changedAtUtc: '2026-11-18T05:00:00Z' }],
  ...over,
})

const candidates = { status: 200, body: [{ id: 'c1', name: 'Asha Rao' }] }

describe('JobsScreen: browsing', () => {
  it('shows an empty state', async () => {
    mockApi({ 'GET /api/jobs': { status: 200, body: [] } })
    render(<JobsScreen />)
    expect(await screen.findByText(/No jobs yet/)).toBeInTheDocument()
  })

  it('shows a server error with a way to reload', async () => {
    mockApi({ 'GET /api/jobs': problem(500, 'server_error') })
    render(<JobsScreen />)
    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('lists jobs and opens one with its details and status history', async () => {
    mockApi({
      'GET /api/jobs': { status: 200, body: [job()] },
      'GET /api/jobs/j1': { status: 200, body: job() },
      'GET /api/candidates': candidates,
    })
    const user = userEvent.setup()
    render(<JobsScreen />)

    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))

    expect(await screen.findByRole('heading', { name: 'Backend Engineer at Acme' })).toBeInTheDocument()
    expect(screen.getByTestId('job-status')).toHaveTextContent('Open')
    expect(within(screen.getByRole('list', { name: 'Job status history' })).getByText(/Open/)).toBeInTheDocument()
  })

  it('sends the search text and status filter to the API', async () => {
    const calls = mockApi({ 'GET /api/jobs': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.type(screen.getByLabelText('Search jobs'), 'engineer')
    await user.selectOptions(screen.getByLabelText('Job status'), 'closed')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    expect(await screen.findByText('No jobs match this search.')).toBeInTheDocument()
    const last = calls.filter((c) => c.method === 'GET' && c.path === '/api/jobs').at(-1)
    expect(last?.search).toContain('q=engineer')
    expect(last?.search).toContain('status=closed')
  })
})

describe('JobsScreen: manual entry', () => {
  it('validates the required fields without calling the API', async () => {
    const calls = mockApi({ 'GET /api/jobs': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.click(screen.getByRole('button', { name: 'Save job' }))

    expect(screen.getByText('A company name is required.')).toBeInTheDocument()
    expect(screen.getByText('A title is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('creates a job and opens it', async () => {
    const jobs: unknown[] = []
    const calls = mockApi({
      'GET /api/jobs': () => ({ status: 200, body: [...jobs] }),
      'POST /api/jobs': () => {
        jobs.push(job())
        return { status: 201, body: job() }
      },
      'GET /api/jobs/j1': { status: 200, body: job() },
      'GET /api/candidates': candidates,
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.type(screen.getByLabelText('Company'), ' Acme ')
    await user.type(screen.getByLabelText('Job title'), 'Backend Engineer')
    await user.selectOptions(screen.getByLabelText('Work mode'), 'hybrid')
    await user.click(screen.getByRole('button', { name: 'Save job' }))

    expect(await screen.findByRole('heading', { name: 'Backend Engineer at Acme' })).toBeInTheDocument()
    const body = calls.find((c) => c.method === 'POST')?.body as Record<string, unknown>
    expect(body).toMatchObject({
      employer: 'Acme',
      title: 'Backend Engineer',
      workMode: 'hybrid',
      applyUrl: null,
      confirmDifferentJob: false,
    })
  })

  it('shows the API field errors next to the form', async () => {
    mockApi({
      'GET /api/jobs': { status: 200, body: [] },
      'POST /api/jobs': problem(400, 'validation', { errors: { applyUrl: ['The link is not valid.'] } }),
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.type(screen.getByLabelText('Company'), 'Acme')
    await user.type(screen.getByLabelText('Job title'), 'Engineer')
    await user.click(screen.getByRole('button', { name: 'Save job' }))

    expect((await screen.findAllByText('The link is not valid.')).length).toBeGreaterThan(0)
  })

  it('warns about a possible duplicate and saves only after the user confirms it is different', async () => {
    let attempt = 0
    const calls = mockApi({
      'GET /api/jobs': { status: 200, body: [] },
      'POST /api/jobs': (): MockReply => {
        attempt += 1
        return attempt === 1
          ? problem(409, 'possible_duplicate', { matchedOn: 'employerTitleLocation', existingId: 'j0' })
          : { status: 201, body: job() }
      },
      'GET /api/jobs/j1': { status: 200, body: job() },
      'GET /api/candidates': candidates,
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.type(screen.getByLabelText('Company'), 'Acme')
    await user.type(screen.getByLabelText('Job title'), 'Backend Engineer')
    await user.click(screen.getByRole('button', { name: 'Save job' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Possible duplicate job')
    expect(screen.getByRole('button', { name: 'Open the existing job' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'This is a different job: save anyway' }))

    expect(await screen.findByRole('heading', { name: 'Backend Engineer at Acme' })).toBeInTheDocument()
    const posts = calls.filter((c) => c.method === 'POST')
    expect((posts[0].body as { confirmDifferentJob: boolean }).confirmDifferentJob).toBe(false)
    expect((posts[1].body as { confirmDifferentJob: boolean }).confirmDifferentJob).toBe(true)
  })

  it('offers no override for a duplicate requisition id', async () => {
    mockApi({
      'GET /api/jobs': { status: 200, body: [] },
      'POST /api/jobs': problem(409, 'duplicate_requisition', { existingId: 'j0' }),
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.type(screen.getByLabelText('Company'), 'Acme')
    await user.type(screen.getByLabelText('Job title'), 'Engineer')
    await user.type(screen.getByLabelText('Requisition id'), 'R-1')
    await user.click(screen.getByRole('button', { name: 'Save job' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Same requisition id')
    expect(screen.queryByRole('button', { name: /save anyway/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open the existing job' })).toBeInTheDocument()
  })
})

describe('JobsScreen: duplicate override and stale edits', () => {
  it('withdraws the override once the draft changes after a duplicate warning', async () => {
    const calls = mockApi({
      'GET /api/jobs': { status: 200, body: [] },
      'POST /api/jobs': problem(409, 'possible_duplicate', { matchedOn: 'canonicalUrl', existingId: 'j0' }),
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await screen.findByText(/No jobs yet/)

    await user.click(screen.getByRole('button', { name: 'Add job' }))
    await user.type(screen.getByLabelText('Company'), 'Acme')
    await user.type(screen.getByLabelText('Job title'), 'Engineer')
    await user.type(screen.getByLabelText('Apply link'), 'https://x.test/a')
    await user.click(screen.getByRole('button', { name: 'Save job' }))
    expect(await screen.findByRole('button', { name: 'This is a different job: save anyway' })).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Apply link'))
    await user.type(screen.getByLabelText('Apply link'), 'https://x.test/b')

    expect(screen.queryByRole('button', { name: 'This is a different job: save anyway' })).not.toBeInTheDocument()
    expect(screen.getByText(/changed after the duplicate warning/)).toBeInTheDocument()
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1)
  })

  it('does not undo a newer change: an edit from an old copy is refused', async () => {
    let current = job()
    let reads = 0
    const calls = mockApi({
      'GET /api/jobs': () => ({ status: 200, body: [current] }),
      'GET /api/jobs/j1': () => {
        reads += 1
        // The first read is the detail; later reads see a change saved elsewhere.
        if (reads > 1) current = job({ location: 'Mumbai', updatedAtUtc: '2026-11-18T07:00:00Z' })
        return { status: 200, body: current }
      },
      'GET /api/candidates': candidates,
      'PUT /api/jobs/j1': { status: 200, body: job() },
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Edit job' }))
    const title = screen.getByLabelText('Job title')
    await user.clear(title)
    await user.type(title, 'Staff Engineer')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Changed by someone else')
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
    expect(await screen.findByText('Mumbai')).toBeInTheDocument()
  })

  it('keeps Edit job off until the fresh job has loaded after a save', async () => {
    let current = job()
    const gate = deferred<MockReply>()
    let detailReads = 0
    mockApi({
      'GET /api/jobs': () => ({ status: 200, body: [current] }),
      'GET /api/jobs/j1': async () => {
        detailReads += 1
        // read 1: detail load, read 2: the stale check, read 3: the reload after the save
        if (detailReads === 3) await gate.promise
        return { status: 200, body: current }
      },
      'GET /api/candidates': candidates,
      'PUT /api/jobs/j1': (req) => {
        current = job({ ...(req.body as object), employer: { id: 'e1', name: 'Acme' }, updatedAtUtc: '2026-11-18T08:00:00Z' })
        return { status: 200, body: current }
      },
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Edit job' }))
    const title = screen.getByLabelText('Job title')
    await user.clear(title)
    await user.type(title, 'Staff Engineer')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    // While the reload is held back the form is still shown and disabled: no stale reopen.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled())
    gate.resolve({ status: 200 })
    expect(await screen.findByRole('heading', { name: 'Staff Engineer at Acme' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit job' })).toBeEnabled()
  })
})

describe('JobsScreen: editing and status', () => {
  const setup = (extra: Record<string, MockHandler> = {}) => {
    let current = job()
    const calls = mockApi({
      'GET /api/jobs': () => ({ status: 200, body: [current] }),
      'GET /api/jobs/j1': () => ({ status: 200, body: current }),
      'GET /api/candidates': candidates,
      'PUT /api/jobs/j1': (req) => {
        current = job({ ...(req.body as object), employer: { id: 'e1', name: 'Acme' } })
        return { status: 200, body: current }
      },
      'POST /api/jobs/j1/status': (req) => {
        current = job({ status: (req.body as { status: string }).status === 'reopened' ? 'reopened' : (req.body as { status: string }).status })
        return { status: 200, body: current }
      },
      ...extra,
    })
    return calls
  }

  it('edits a job and sends every field, clearing the blank ones', async () => {
    const calls = setup()
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Edit job' }))

    const title = screen.getByLabelText('Job title')
    await user.clear(title)
    await user.type(title, 'Staff Engineer')
    await user.clear(screen.getByLabelText('Location'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByRole('heading', { name: 'Staff Engineer at Acme' })).toBeInTheDocument()
    const put = calls.find((c) => c.method === 'PUT')?.body as Record<string, unknown>
    expect(put).toMatchObject({ title: 'Staff Engineer', location: null, workMode: 'hybrid' })
  })

  it('offers only the allowed status changes and applies one', async () => {
    const calls = setup()
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))

    const group = await screen.findByRole('group', { name: 'Change job status' })
    expect(within(group).getAllByRole('button').map((b) => b.textContent)).toEqual([
      'Mark closed',
      'Mark expired',
      'Mark unavailable',
    ])
    await user.click(within(group).getByRole('button', { name: 'Mark closed' }))

    await waitFor(() => expect(screen.getByTestId('job-status')).toHaveTextContent('Closed'))
    expect(calls.find((c) => c.path === '/api/jobs/j1/status')?.body).toEqual({ status: 'closed' })
    expect(
      within(screen.getByRole('group', { name: 'Change job status' })).getByRole('button', { name: 'Open again' }),
    ).toBeInTheDocument()
  })

  it('explains that a system-only status cannot be changed', async () => {
    setup({ 'GET /api/jobs/j1': { status: 200, body: job({ status: 'new' }) } })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    expect(await screen.findByText(/set by the system/)).toBeInTheDocument()
  })

  it('shows a conflict when the job changed under the user', async () => {
    setup({ 'POST /api/jobs/j1/status': problem(409, 'concurrent_modification') })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Mark closed' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Changed by someone else')
  })

  it('asks before deleting and reports a job that is still in use', async () => {
    const calls = setup({ 'DELETE /api/jobs/j1': problem(409, 'job_in_use') })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Delete job' }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Yes, delete this job' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Job is in use')
  })

  it('removes a deleted job from the list', async () => {
    let jobs = [job()]
    setup({
      'GET /api/jobs': () => ({ status: 200, body: jobs }),
      'DELETE /api/jobs/j1': () => {
        jobs = []
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<JobsScreen />)
    await user.click(await screen.findByRole('button', { name: 'Open Backend Engineer at Acme' }))
    await user.click(await screen.findByRole('button', { name: 'Delete job' }))
    await user.click(screen.getByRole('button', { name: 'Yes, delete this job' }))

    expect(await screen.findByText(/No jobs yet/)).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Backend Engineer at Acme' })).not.toBeInTheDocument()
  })
})
