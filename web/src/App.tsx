import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResult } from './api'

type ViewState = { kind: 'checking' } | HealthResult

const DATABASE_LABEL = {
  ok: 'Connected',
  unavailable: 'Unavailable',
  not_checked: 'Not checked',
} as const

export default function App() {
  const [state, setState] = useState<ViewState>({ kind: 'checking' })

  useEffect(() => {
    let cancelled = false
    void fetchHealth().then((result) => {
      if (!cancelled) setState(result)
    })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className="app">
      <h1>Job Search &amp; Application Tracker</h1>
      <p className="subtitle">
        Phase 0 foundation. Job discovery, profiles and application tracking are
        not built yet.
      </p>

      <section aria-labelledby="system-status">
        <h2 id="system-status">System status</h2>
        <dl>
          <dt>API</dt>
          <dd data-testid="api-status">{apiLabel(state)}</dd>
          <dt>Database</dt>
          <dd data-testid="db-status">{databaseLabel(state)}</dd>
        </dl>
        {state.kind === 'unreachable' && (
          <p role="alert">Could not reach the API: {state.reason}</p>
        )}
      </section>
    </main>
  )
}

function apiLabel(state: ViewState): string {
  switch (state.kind) {
    case 'checking':
      return 'Checking…'
    case 'ready':
    case 'degraded':
      return 'Running'
    case 'unreachable':
      return 'Unreachable'
  }
}

function databaseLabel(state: ViewState): string {
  switch (state.kind) {
    case 'checking':
      return 'Checking…'
    case 'unreachable':
      return DATABASE_LABEL.not_checked
    default:
      return DATABASE_LABEL[state.response.database]
  }
}
