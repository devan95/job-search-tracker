import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => vi.restoreAllMocks())

// Health answers as given; every other API call (the screens) answers an empty list.
function mockFetch(status: number, body: unknown) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) =>
    String(input).includes('/api/health/ready')
      ? new Response(JSON.stringify(body), { status })
      : new Response('[]', { status: 200 }),
  )
}

describe('App', () => {
  it('shows API running and database connected when ready', async () => {
    mockFetch(200, { status: 'ok', database: 'ok' })
    render(<App />)
    expect(await screen.findByText('Connected')).toBeInTheDocument()
    expect(screen.getByTestId('api-status')).toHaveTextContent('Running')
  })

  it('shows API running but database unavailable when degraded', async () => {
    mockFetch(503, { status: 'degraded', database: 'unavailable' })
    render(<App />)
    expect(await screen.findByText('Unavailable')).toBeInTheDocument()
    expect(screen.getByTestId('api-status')).toHaveTextContent('Running')
  })

  it('shows API unreachable and database not checked when the request fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<App />)
    expect(await screen.findByText(/Could not reach the API: offline/)).toBeInTheDocument()
    expect(screen.getByTestId('api-status')).toHaveTextContent('Unreachable')
    expect(screen.getByTestId('db-status')).toHaveTextContent('Not checked')
  })

  it('starts on the candidates section and switches between the setup sections', async () => {
    mockFetch(200, { status: 'ok', database: 'ok' })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Candidates' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Candidates' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel', { name: 'Candidates' })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Company lists' }))
    expect(await screen.findByRole('heading', { name: 'Company lists' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Candidates' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Search profiles' }))
    expect(await screen.findByRole('heading', { name: 'Search profiles' })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Jobs' }))
    expect(await screen.findByRole('heading', { name: 'Jobs' })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Applications' }))
    expect(await screen.findByRole('heading', { name: 'Applications' })).toBeInTheDocument()
  })

  it('moves between the tabs with the arrow, Home and End keys', async () => {
    mockFetch(200, { status: 'ok', database: 'ok' })
    const user = userEvent.setup()
    render(<App />)
    const candidates = screen.getByRole('tab', { name: 'Candidates' })
    candidates.focus()

    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Company lists' })).toHaveFocus()
    expect(screen.getByRole('tab', { name: 'Company lists' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Candidates' })).toHaveAttribute('tabindex', '-1')

    await user.keyboard('{End}')
    expect(screen.getByRole('tab', { name: 'Applications' })).toHaveFocus()

    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Candidates' })).toHaveFocus()

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('tab', { name: 'Applications' })).toHaveFocus()

    await user.keyboard('{Home}')
    expect(screen.getByRole('tab', { name: 'Candidates' })).toHaveFocus()
    expect(screen.getByRole('tabpanel', { name: 'Candidates' })).toBeInTheDocument()

    await user.keyboard('x')
    expect(screen.getByRole('tab', { name: 'Candidates' })).toHaveFocus()
  })
})
