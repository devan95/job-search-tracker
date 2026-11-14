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
