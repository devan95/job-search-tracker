import { useState, type FormEvent } from 'react'
import {
  changeJobStatus,
  createJob,
  deleteJob,
  getJob,
  listJobs,
  updateJob,
  type Job,
  type JobFilters,
  type UserJobStatus,
} from '../api'
import { JobApplications } from '../components/JobApplications'
import { JobForm } from '../components/JobForm'
import { ProblemAlert } from '../components/ProblemAlert'
import { useAction, useLoad } from '../hooks'
import {
  EMPLOYMENT_TYPE_LABEL,
  JOB_STATUS_ACTION_LABEL,
  JOB_STATUS_LABEL,
  JOB_STATUSES,
  allowedJobStatuses,
  emptyJobForm,
  formatUtc,
  isHttpUrl,
  jobFormToInput,
  jobToForm,
  labelFor,
  type JobFormValues,
} from '../jobs'
import { WORK_MODE_LABEL } from '../workModes'

const dash = (value: string | number | null | undefined): string =>
  value === null || value === undefined || value === '' ? '—' : String(value)

function describeExperience(job: Job): string {
  const { experienceMinYears: min, experienceMaxYears: max } = job
  if (min === null && max === null) return '—'
  if (min !== null && max !== null) return `${min} to ${max} years`
  return min !== null ? `${min}+ years` : `Up to ${max} years`
}

export default function JobsScreen() {
  const createAction = useAction()
  const [draftQuery, setDraftQuery] = useState('')
  const [draftStatus, setDraftStatus] = useState('')
  const [filters, setFilters] = useState<JobFilters>({})
  // Bumped after a save so the list is fetched again.
  const [version, setVersion] = useState(0)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const refreshList = () => setVersion((v) => v + 1)

  function startCreate() {
    createAction.clearError()
    setCreating(true)
  }

  async function onCreate(values: JobFormValues, confirmDifferentJob: boolean) {
    const result = await createAction.run(() => createJob(jobFormToInput(values, confirmDifferentJob)))
    if (result.ok) {
      setCreating(false)
      setSelectedId(result.value.id)
      refreshList()
    }
  }

  function onSearch(event: FormEvent) {
    event.preventDefault()
    setFilters({ q: draftQuery.trim(), status: draftStatus })
  }

  function openExisting(jobId: string) {
    createAction.clearError()
    setCreating(false)
    setSelectedId(jobId)
  }

  return (
    <section aria-labelledby="jobs-heading">
      <h2 id="jobs-heading">Jobs</h2>
      <p className="hint">
        Jobs you add by hand. A job is a global record: you can track an application for any
        candidate from its detail below.
      </p>

      <div className="buttons">
        <button type="button" onClick={startCreate} disabled={creating}>
          Add job
        </button>
      </div>
      {creating && (
        <JobForm
          initial={emptyJobForm}
          submitLabel="Save job"
          busy={createAction.busy}
          error={createAction.error}
          onSubmit={(values, confirm) => void onCreate(values, confirm)}
          onCancel={() => {
            createAction.clearError()
            setCreating(false)
          }}
          onOpenExisting={openExisting}
        />
      )}

      <form onSubmit={onSearch} className="stack" role="search" aria-label="Filter jobs">
        <label>
          Search jobs
          <input value={draftQuery} onChange={(e) => setDraftQuery(e.target.value)} />
        </label>
        <label>
          Job status
          <select value={draftStatus} onChange={(e) => setDraftStatus(e.target.value)}>
            <option value="">Any status</option>
            {JOB_STATUSES.map((status) => (
              <option key={status} value={status}>
                {JOB_STATUS_LABEL[status]}
              </option>
            ))}
          </select>
        </label>
        <div className="buttons">
          <button type="submit">Search</button>
        </div>
      </form>

      <JobList
        key={`${version}|${filters.q ?? ''}|${filters.status ?? ''}`}
        filters={filters}
        selectedId={selectedId}
        onOpen={(jobId) => {
          setCreating(false)
          setSelectedId(jobId)
        }}
      />

      {selectedId && (
        <JobDetail
          key={selectedId}
          jobId={selectedId}
          onChanged={refreshList}
          onDeleted={() => {
            setSelectedId(null)
            refreshList()
          }}
          onOpenJob={openExisting}
        />
      )}
    </section>
  )
}

function JobList({
  filters,
  selectedId,
  onOpen,
}: {
  filters: JobFilters
  selectedId: string | null
  onOpen: (jobId: string) => void
}) {
  const { state, reload } = useLoad(() => listJobs(filters))
  const filtered = Boolean(filters.q) || Boolean(filters.status)

  if (state.kind === 'loading') return <p role="status">Loading jobs…</p>
  if (state.kind === 'error') return <ProblemAlert error={state.error} onReload={reload} />
  if (state.data.length === 0) {
    return (
      <p>
        {filtered
          ? 'No jobs match this search.'
          : 'No jobs yet. Add one to start tracking applications.'}
      </p>
    )
  }
  return (
    <ul className="rows cards" aria-label="Jobs">
      {state.data.map((job) => (
        <li key={job.id}>
          <button
            type="button"
            className="row-button"
            aria-current={job.id === selectedId ? 'true' : undefined}
            aria-label={`Open ${job.title} at ${job.employer.name}`}
            onClick={() => onOpen(job.id)}
          >
            <span className="row-name">{job.title}</span>
            <span className="meta">
              {[
                job.employer.name,
                labelFor(JOB_STATUS_LABEL, job.status),
                job.location,
                job.workMode ? labelFor(WORK_MODE_LABEL, job.workMode) : null,
              ]
                .filter((part) => part)
                .join(' · ')}
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

function JobDetail({
  jobId,
  onChanged,
  onDeleted,
  onOpenJob,
}: {
  jobId: string
  onChanged: () => void
  onDeleted: () => void
  onOpenJob: (jobId: string) => void
}) {
  const { state, reload } = useLoad(() => getJob(jobId))
  // Separate actions so an error is shown against the form or control it came from.
  const editAction = useAction()
  const rowAction = useAction()
  const busy = editAction.busy || rowAction.busy
  const [editing, setEditing] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  async function onSave(values: JobFormValues, confirmDifferentJob: boolean) {
    rowAction.clearError()
    const result = await editAction.run(() =>
      updateJob(jobId, jobFormToInput(values, confirmDifferentJob)),
    )
    if (result.ok) {
      setEditing(false)
      void reload()
      onChanged()
    }
  }

  async function onStatus(status: UserJobStatus) {
    editAction.clearError()
    const result = await rowAction.run(() => changeJobStatus(jobId, status))
    if (result.ok) {
      void reload()
      onChanged()
    }
  }

  async function onDelete() {
    editAction.clearError()
    const result = await rowAction.run(() => deleteJob(jobId))
    setConfirmingDelete(false)
    if (result.ok) onDeleted()
  }

  if (state.kind === 'loading') return <p role="status">Loading job…</p>
  if (state.kind === 'error') return <ProblemAlert error={state.error} subject="job" onReload={reload} />

  const job = state.data
  const allowed = allowedJobStatuses(job.status)

  return (
    <section className="detail" aria-labelledby="job-detail-heading">
      <h3 id="job-detail-heading">
        {job.title} at {job.employer.name}
      </h3>

      {editing ? (
        <JobForm
          initial={jobToForm(job)}
          submitLabel="Save changes"
          busy={busy}
          error={editAction.error}
          onSubmit={(values, confirm) => void onSave(values, confirm)}
          onCancel={() => {
            editAction.clearError()
            setEditing(false)
          }}
          onOpenExisting={onOpenJob}
        />
      ) : (
        <>
          <dl>
            <dt>Company</dt>
            <dd>{job.employer.name}</dd>
            <dt>Status</dt>
            <dd data-testid="job-status">{labelFor(JOB_STATUS_LABEL, job.status)}</dd>
            <dt>Location</dt>
            <dd>{dash(job.location)}</dd>
            <dt>Work mode</dt>
            <dd>{job.workMode ? labelFor(WORK_MODE_LABEL, job.workMode) : '—'}</dd>
            <dt>Employment type</dt>
            <dd>
              {job.employmentType ? labelFor(EMPLOYMENT_TYPE_LABEL, job.employmentType) : '—'}
            </dd>
            <dt>Department</dt>
            <dd>{dash(job.department)}</dd>
            <dt>Experience</dt>
            <dd>{describeExperience(job)}</dd>
            <dt>Requisition id</dt>
            <dd>{dash(job.requisitionId)}</dd>
            <dt>Posted</dt>
            <dd>{dash(job.postedDate)}</dd>
            <dt>Apply link</dt>
            <dd>
              {job.applyUrl && isHttpUrl(job.applyUrl) ? (
                <a href={job.applyUrl} target="_blank" rel="noreferrer noopener">
                  {job.applyUrl}
                </a>
              ) : (
                dash(job.applyUrl)
              )}
            </dd>
            <dt>Found on</dt>
            <dd>{dash(job.foundOn)}</dd>
            <dt>Added</dt>
            <dd>
              {job.createdVia === 'manual' ? 'By hand' : job.createdVia} on{' '}
              {formatUtc(job.createdAtUtc)}
            </dd>
          </dl>
          {job.description && <p className="description">{job.description}</p>}

          {rowAction.error && (
            <ProblemAlert error={rowAction.error} subject="job" onReload={() => void reload()} />
          )}

          <div className="buttons">
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                rowAction.clearError()
                setConfirmingDelete(false)
                setEditing(true)
              }}
            >
              Edit job
            </button>
            {confirmingDelete ? (
              <>
                <button type="button" disabled={busy} onClick={() => void onDelete()}>
                  Yes, delete this job
                </button>
                <button type="button" onClick={() => setConfirmingDelete(false)}>
                  Keep
                </button>
              </>
            ) : (
              <button
                type="button"
                disabled={busy}
                onClick={() => {
                  rowAction.clearError()
                  setConfirmingDelete(true)
                }}
              >
                Delete job
              </button>
            )}
          </div>

          {allowed.length > 0 ? (
            <div className="buttons" role="group" aria-label="Change job status">
              {allowed.map((status) => (
                <button
                  key={status}
                  type="button"
                  disabled={busy}
                  onClick={() => void onStatus(status)}
                >
                  {JOB_STATUS_ACTION_LABEL[status]}
                </button>
              ))}
            </div>
          ) : (
            <p className="hint">This status is set by the system and cannot be changed here.</p>
          )}
        </>
      )}

      <h4>Status history</h4>
      <ol className="history" aria-label="Job status history">
        {job.statusHistory.map((change, i) => (
          <li key={`${i}-${change.changedAtUtc}`}>
            {change.fromStatus ? `${labelFor(JOB_STATUS_LABEL, change.fromStatus)} → ` : ''}
            {labelFor(JOB_STATUS_LABEL, change.toStatus)}
            <span className="meta block">{formatUtc(change.changedAtUtc)}</span>
          </li>
        ))}
      </ol>

      <JobApplications jobId={job.id} />
    </section>
  )
}
