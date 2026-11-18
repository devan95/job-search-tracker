import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { fetchHealth, type HealthResult } from './api'
import CandidatesScreen from './screens/CandidatesScreen'
import ApplicationsScreen from './screens/ApplicationsScreen'
import CompanyListsScreen from './screens/CompanyListsScreen'
import JobsScreen from './screens/JobsScreen'
import SearchProfilesScreen from './screens/SearchProfilesScreen'

type ViewState = { kind: 'checking' } | HealthResult

type Section = 'candidates' | 'lists' | 'profiles' | 'jobs' | 'applications'

// Setup order: a candidate, then company lists, then a search profile that ties them together;
// then the jobs entered by hand and the applications tracked for them.
const SECTIONS: { id: Section; label: string }[] = [
  { id: 'candidates', label: 'Candidates' },
  { id: 'lists', label: 'Company lists' },
  { id: 'profiles', label: 'Search profiles' },
  { id: 'jobs', label: 'Jobs' },
  { id: 'applications', label: 'Applications' },
]

const DATABASE_LABEL = {
  ok: 'Connected',
  unavailable: 'Unavailable',
  not_checked: 'Not checked',
} as const

export default function App() {
  const [state, setState] = useState<ViewState>({ kind: 'checking' })
  const [section, setSection] = useState<Section>('candidates')
  const tabRefs = useRef<Partial<Record<Section, HTMLButtonElement | null>>>({})

  useEffect(() => {
    let cancelled = false
    void fetchHealth().then((result) => {
      if (!cancelled) setState(result)
    })
    return () => {
      cancelled = true
    }
  }, [])

  // Arrow keys, Home and End move between tabs and select the focused one.
  function onTabKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = SECTIONS.length - 1
    let next: number
    switch (event.key) {
      case 'ArrowRight':
        next = index === last ? 0 : index + 1
        break
      case 'ArrowLeft':
        next = index === 0 ? last : index - 1
        break
      case 'Home':
        next = 0
        break
      case 'End':
        next = last
        break
      default:
        return
    }
    event.preventDefault()
    const target = SECTIONS[next].id
    setSection(target)
    tabRefs.current[target]?.focus()
  }

  return (
    <main className="app">
      <h1>Job Search &amp; Application Tracker</h1>
      <p className="subtitle">
        Lists, profiles, and jobs and applications you enter by hand. Job
        discovery and matching are not built yet.
      </p>

      <div role="tablist" aria-label="Sections" className="tabs">
        {SECTIONS.map((s, index) => (
          <button
            key={s.id}
            ref={(el) => {
              tabRefs.current[s.id] = el
            }}
            type="button"
            role="tab"
            id={`tab-${s.id}`}
            aria-selected={section === s.id}
            aria-controls={section === s.id ? `panel-${s.id}` : undefined}
            tabIndex={section === s.id ? 0 : -1}
            onClick={() => setSection(s.id)}
            onKeyDown={(e) => onTabKeyDown(e, index)}
          >
            {s.label}
          </button>
        ))}
      </div>

      <div role="tabpanel" id={`panel-${section}`} aria-labelledby={`tab-${section}`}>
        {section === 'candidates' && <CandidatesScreen />}
        {section === 'lists' && <CompanyListsScreen />}
        {section === 'profiles' && <SearchProfilesScreen />}
        {section === 'jobs' && <JobsScreen />}
        {section === 'applications' && <ApplicationsScreen />}
      </div>

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
