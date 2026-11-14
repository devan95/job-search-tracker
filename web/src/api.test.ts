import { describe, expect, it, vi } from 'vitest'
import { fetchHealth } from './api'

const json = (status: number, body: unknown) =>
  vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    }),
  ) as unknown as typeof fetch

describe('fetchHealth', () => {
  it('reports ready when the API returns 200', async () => {
    const result = await fetchHealth(json(200, { status: 'ok', database: 'ok' }))
    expect(result.kind).toBe('ready')
  })

  it('reports degraded (not unreachable) when the API returns 503 with a body', async () => {
    const result = await fetchHealth(
      json(503, { status: 'degraded', database: 'unavailable' }),
    )
    expect(result).toMatchObject({
      kind: 'degraded',
      response: { database: 'unavailable' },
    })
  })

  it('reports unreachable when the request fails', async () => {
    const failing = vi.fn().mockRejectedValue(new Error('boom')) as unknown as typeof fetch
    expect(await fetchHealth(failing)).toEqual({
      kind: 'unreachable',
      reason: 'boom',
    })
  })

  it('reports unreachable on unexpected status codes', async () => {
    const result = await fetchHealth(json(500, {}))
    expect(result).toEqual({ kind: 'unreachable', reason: 'Unexpected HTTP 500' })
  })
})
