import { useState } from 'react'
import { listApplications, listCandidates, type ApplicationFilters } from '../api'
import { ApplicationPanel } from '../components/ApplicationPanel'
import { ProblemAlert } from '../components/ProblemAlert'
import { useLoad } from '../hooks'
import {
  APPLICATION_STATUS_LABEL,
  APPLICATION_STATUSES,
  formatUtc,
  labelFor,
} from '../jobs'

export default function ApplicationsScreen() {
  const candidates = useLoad(listCandidates)
  const [candidateId, setCandidateId] = useState('')
  const [status, setStatus] = useState('')
  // Bumped after a change so the list shows the new status.
  const [version, setVersion] = useState(0)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const names = new Map<string, string>(
    candidates.state.kind === 'ready'
      ? candidates.state.data.map((c): [string, string] => [c.id, c.name])
      : [],
  )

  return (
    <section aria-labelledby="applications-heading">
      <h2 id="applications-heading">Applications</h2>
      <p className="hint">
        One application per candidate and job. Start one from a job on the Jobs tab. An application
        is applied only when you confirm that you submitted it yourself.
      </p>

      {candidates.state.kind === 'error' && (
        <ProblemAlert error={candidates.state.error} onReload={candidates.reload} />
      )}

      <div className="stack">
        <label>
          Candidate filter
          <select value={candidateId} onChange={(e) => setCandidateId(e.target.value)}>
            <option value="">All candidates</option>
            {candidates.state.kind === 'ready' &&
              candidates.state.data.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
          </select>
        </label>
        <label>
          Status filter
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">Any status</option>
            {APPLICATION_STATUSES.map((s) => (
              <option key={s} value={s}>
                {APPLICATION_STATUS_LABEL[s]}
              </option>
            ))}
          </select>
        </label>
      </div>

      <ApplicationList
        key={`${version}|${candidateId}|${status}`}
        filters={{ candidateId, status }}
        names={names}
        selectedId={selectedId}
        onOpen={setSelectedId}
      />

      {selectedId && (
        <ApplicationPanel
          key={selectedId}
          applicationId={selectedId}
          onChanged={() => setVersion((v) => v + 1)}
        />
      )}
    </section>
  )
}

function ApplicationList({
  filters,
  names,
  selectedId,
  onOpen,
}: {
  filters: ApplicationFilters
  names: Map<string, string>
  selectedId: string | null
  onOpen: (applicationId: string) => void
}) {
  const { state, reload } = useLoad(() => listApplications(filters))

  if (state.kind === 'loading') return <p role="status">Loading applications…</p>
  if (state.kind === 'error') return <ProblemAlert error={state.error} onReload={reload} />
  if (state.data.length === 0) {
    return (
      <p>
        {filters.candidateId || filters.status
          ? 'No applications match these filters.'
          : 'No applications yet. Open a job and track an application for a candidate.'}
      </p>
    )
  }
  return (
    <ul className="rows cards" aria-label="Applications">
      {state.data.map((application) => (
        <li key={application.id}>
          <button
            type="button"
            className="row-button"
            aria-current={application.id === selectedId ? 'true' : undefined}
            aria-label={`Open application: ${application.job.title} at ${application.job.employer.name} for ${names.get(application.candidateId) ?? 'Unknown candidate'}`}
            onClick={() => onOpen(application.id)}
          >
            <span className="row-name">
              {application.job.title} at {application.job.employer.name}
            </span>
            <span className="meta">
              {names.get(application.candidateId) ?? 'Unknown candidate'} ·{' '}
              {labelFor(APPLICATION_STATUS_LABEL, application.status)} ·{' '}
              {application.isApplied
                ? `Applied (${application.method ?? 'unknown method'}) ${formatUtc(application.appliedAtUtc)}`
                : 'Not applied'}
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}
