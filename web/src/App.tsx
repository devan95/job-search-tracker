import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResult } from './api'
import CandidatesScreen from './screens/CandidatesScreen'
import CompanyListsScreen from './screens/CompanyListsScreen'
import SearchProfilesScreen from './screens/SearchProfilesScreen'

type ViewState = { kind: 'checking' } | HealthResult

type Section = 'candidates' | 'lists' | 'profiles'

// Setup order: a candidate, then company lists, then a search profile that ties them together.
const SECTIONS: { id: Section; label: string }[] = [
  { id: 'candidates', label: 'Candidates' },
  { id: 'lists', label: 'Company lists' },
  { id: 'profiles', label: 'Search profiles' },
]

const DATABASE_LABEL = {
  ok: 'Connected',
  unavailable: 'Unavailable',
  not_checked: 'Not checked',
} as const

export default function App() {
  const [state, setState] = useState<ViewState>({ kind: 'checking' })
  const [section, setSection] = useState<Section>('candidates')

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
        Lists and profiles. Job discovery, matching and application tracking are
        not built yet.
      </p>

      <nav aria-label="Sections" className="tabs">
        {SECTIONS.map((s) => (
          <button
            key={s.id}
            type="button"
            aria-current={section === s.id ? 'page' : undefined}
            onClick={() => setSection(s.id)}
          >
            {s.label}
          </button>
        ))}
      </nav>

      {section === 'candidates' && <CandidatesScreen />}
      {section === 'lists' && <CompanyListsScreen />}
      {section === 'profiles' && <SearchProfilesScreen />}

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
