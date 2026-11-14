import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => vi.restoreAllMocks())

function mockFetch(status: number, body: unknown) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify(body), { status }),
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
    expect(await screen.findByRole('alert')).toHaveTextContent('offline')
    expect(screen.getByTestId('api-status')).toHaveTextContent('Unreachable')
    expect(screen.getByTestId('db-status')).toHaveTextContent('Not checked')
  })
})
