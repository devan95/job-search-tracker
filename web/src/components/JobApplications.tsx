import { useId, useState } from 'react'
import { ApiError, createApplication, listApplications, listCandidates } from '../api'
import { useAction, useLoad } from '../hooks'
import { ApplicationPanel } from './ApplicationPanel'
import { ProblemAlert } from './ProblemAlert'

/**
 * The application of one candidate for one job. One application is allowed per candidate and job,
 * so an existing one is shown and the create button is offered only when there is none.
 * Give it a `key` of the candidate id.
 */
function CandidateApplication({
  jobId,
  candidateId,
  candidateName,
}: {
  jobId: string
  candidateId: string
  candidateName: string
}) {
  const { state, reload } = useLoad(() => listApplications({ candidateId, jobId }))
  const action = useAction()

  async function onTrack() {
    const result = await action.run(async () => {
      try {
        return await createApplication(candidateId, jobId)
      } catch (err) {
        // A duplicate means one exists already (another tab, or a race): show it instead.
        if (err instanceof ApiError && err.problem?.code === 'application_exists') void reload()
        throw err
      }
    })
    if (result.ok) void reload()
  }

  if (state.kind === 'loading') return <p role="status">Loading applications…</p>
  if (state.kind === 'error') return <ProblemAlert error={state.error} onReload={reload} />

  const existing = state.data[0]
  return (
    <>
      {action.error && (
        <ProblemAlert error={action.error} subject="application" onReload={() => void reload()} />
      )}
      {existing ? (
        <>
          <p className="hint">
            {candidateName} already has an application for this job. Only one is allowed per
            candidate and job.
          </p>
          <ApplicationPanel key={existing.id} applicationId={existing.id} />
        </>
      ) : (
        <>
          <p>No application recorded for {candidateName} and this job yet.</p>
          <p className="hint">
            Tracking an application saves it as Saved. It is not applied until you confirm that you
            submitted it yourself.
          </p>
          <div className="buttons">
            <button type="button" onClick={() => void onTrack()} disabled={action.busy}>
              Track application
            </button>
          </div>
        </>
      )}
    </>
  )
}

/** The applications part of a job's detail: pick a candidate, see or start their application. */
export function JobApplications({ jobId }: { jobId: string }) {
  const { state, reload } = useLoad(listCandidates)
  const [candidateId, setCandidateId] = useState('')
  const headingId = useId()

  const candidate = state.kind === 'ready' ? state.data.find((c) => c.id === candidateId) : undefined

  return (
    <section aria-labelledby={headingId}>
      <h4 id={headingId}>Applications for this job</h4>
      <p className="hint">
        A job status never changes an application&apos;s status.
      </p>
      {state.kind === 'loading' && <p role="status">Loading candidates…</p>}
      {state.kind === 'error' && <ProblemAlert error={state.error} onReload={reload} />}
      {state.kind === 'ready' && state.data.length === 0 && (
        <p>Add a candidate first, then track an application for this job.</p>
      )}
      {state.kind === 'ready' && state.data.length > 0 && (
        <div className="stack">
          <label>
            Candidate
            <select value={candidateId} onChange={(e) => setCandidateId(e.target.value)}>
              <option value="">Choose a candidate</option>
              {state.data.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      )}
      {candidate && (
        <CandidateApplication
          key={candidate.id}
          jobId={jobId}
          candidateId={candidate.id}
          candidateName={candidate.name}
        />
      )}
    </section>
  )
}
