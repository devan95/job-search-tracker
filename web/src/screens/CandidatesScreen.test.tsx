import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { deferred, mockApi, problem, type MockReply } from '../test/mockApi'
import CandidatesScreen from './CandidatesScreen'

afterEach(() => vi.restoreAllMocks())

describe('CandidatesScreen', () => {
  it('shows an empty state when there are no candidates', async () => {
    mockApi({ 'GET /api/candidates': { status: 200, body: [] } })
    render(<CandidatesScreen />)
    expect(await screen.findByText(/No candidates yet/)).toBeInTheDocument()
  })

  it('adds a candidate with a trimmed name and lists it', async () => {
    const candidates: { id: string; name: string }[] = []
    const calls = mockApi({
      'GET /api/candidates': () => ({ status: 200, body: [...candidates] }),
      'POST /api/candidates': (req) => {
        const created = { id: 'c1', name: (req.body as { name: string }).name }
        candidates.push(created)
        return { status: 201, body: created }
      },
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)
    await screen.findByText(/No candidates yet/)

    await user.type(screen.getByLabelText('Candidate name'), '  Asha Rao ')
    await user.click(screen.getByRole('button', { name: 'Add candidate' }))

    expect(await screen.findByText('Asha Rao')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Asha Rao' })
    expect(screen.getByLabelText('Candidate name')).toHaveValue('')
  })

  it('validates a blank name without calling the API', async () => {
    const calls = mockApi({ 'GET /api/candidates': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<CandidatesScreen />)
    await screen.findByText(/No candidates yet/)

    await user.type(screen.getByLabelText('Candidate name'), '   ')
    await user.click(screen.getByRole('button', { name: 'Add candidate' }))

    expect(screen.getByText('A name is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows the API validation errors when the server rejects the request', async () => {
    mockApi({
      'GET /api/candidates': { status: 200, body: [] },
      'POST /api/candidates': problem(400, 'validation', {
        errors: { name: ['A name can be at most 200 characters.'] },
      }),
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)
    await screen.findByText(/No candidates yet/)

    await user.type(screen.getByLabelText('Candidate name'), 'Someone')
    await user.click(screen.getByRole('button', { name: 'Add candidate' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Check the form')
    expect(alert).toHaveTextContent('Name: A name can be at most 200 characters.')
  })

  it('renames a candidate', async () => {
    const candidates = [{ id: 'c1', name: 'Asha Rao' }]
    const calls = mockApi({
      'GET /api/candidates': () => ({ status: 200, body: [...candidates] }),
      'PUT /api/candidates/c1': (req) => {
        candidates[0] = { id: 'c1', name: (req.body as { name: string }).name }
        return { status: 200, body: candidates[0] }
      },
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Rename Asha Rao' }))
    const input = screen.getByLabelText('New name for Asha Rao')
    await user.clear(input)
    await user.type(input, 'Asha R. Rao')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Asha R. Rao')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: 'Asha R. Rao' })
  })

  it('asks for confirmation before deleting and removes the candidate', async () => {
    let candidates = [{ id: 'c1', name: 'Asha Rao' }]
    const calls = mockApi({
      'GET /api/candidates': () => ({ status: 200, body: [...candidates] }),
      'DELETE /api/candidates/c1': () => {
        candidates = []
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Delete Asha Rao' }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)
    await user.click(screen.getByRole('button', { name: 'Confirm delete Asha Rao' }))

    expect(await screen.findByText(/No candidates yet/)).toBeInTheDocument()
  })

  it('explains why a candidate with search profiles cannot be deleted', async () => {
    mockApi({
      'GET /api/candidates': { status: 200, body: [{ id: 'c1', name: 'Asha Rao' }] },
      'DELETE /api/candidates/c1': problem(409, 'candidate_has_profiles', { searchProfileCount: 2 }),
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Delete Asha Rao' }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete Asha Rao' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Candidate still has search profiles')
    expect(alert).toHaveTextContent('2 search profiles')
    expect(screen.getByText('Asha Rao')).toBeInTheDocument()
  })

  it('shows a server error on load and recovers on reload', async () => {
    let failing = true
    mockApi({
      'GET /api/candidates': () =>
        failing ? problem(500, 'unexpected') : { status: 200, body: [] },
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Server error')
    failing = false
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    expect(await screen.findByText(/No candidates yet/)).toBeInTheDocument()
  })

  it('shows a network error when the API cannot be reached', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<CandidatesScreen />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Cannot reach the API')
  })

  it('links a validation message to its input', async () => {
    mockApi({ 'GET /api/candidates': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<CandidatesScreen />)
    await screen.findByText(/No candidates yet/)

    await user.click(screen.getByRole('button', { name: 'Add candidate' }))
    const input = screen.getByLabelText('Candidate name')
    const message = screen.getByText(/name/i, { selector: '.field-error' })
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAttribute('aria-describedby', message.id)
  })

  it('shows a server name error against the rename input, not the add input', async () => {
    mockApi({
      'GET /api/candidates': { status: 200, body: [{ id: 'c1', name: 'Asha Rao' }] },
      'PUT /api/candidates/c1': problem(400, 'validation', { errors: { name: ['Too long.'] } }),
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Rename Asha Rao' }))
    await user.type(screen.getByLabelText('New name for Asha Rao'), 'x')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Too long.')
    expect(screen.getByLabelText('New name for Asha Rao')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Candidate name')).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('keeps another candidate editor open when an earlier save finishes', async () => {
    const candidates = [
      { id: 'c1', name: 'Asha Rao' },
      { id: 'c2', name: 'Ben Cole' },
    ]
    const release = deferred<MockReply>()
    const calls = mockApi({
      'GET /api/candidates': () => ({ status: 200, body: [...candidates] }),
      'PUT /api/candidates/c1': () => release.promise,
    })
    const user = userEvent.setup()
    render(<CandidatesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Rename Asha Rao' }))
    await user.type(screen.getByLabelText('New name for Asha Rao'), '!')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await user.click(screen.getByRole('button', { name: 'Rename Ben Cole' }))
    expect(screen.getByLabelText('New name for Ben Cole')).toBeInTheDocument()

    release.resolve({ status: 200, body: { id: 'c1', name: 'Asha Rao!' } })
    // The save succeeded: the list is reloaded, and Ben's editor must still be open.
    await waitFor(() => expect(calls.filter((c) => c.method === 'GET')).toHaveLength(2))
    expect(screen.getByLabelText('New name for Ben Cole')).toBeInTheDocument()
  })
})
