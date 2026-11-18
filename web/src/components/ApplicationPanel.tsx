import { useId, useState, type FormEvent } from 'react'
import {
  changeApplicationStatus,
  confirmApplied,
  correctApplication,
  getApplication,
  unapplyApplication,
  type Application,
  type ApplicationStatus,
} from '../api'
import { useAction, useLoad } from '../hooks'
import {
  APPLICATION_STATUS_LABEL,
  APPLICATION_STATUSES,
  AUDIT_ACTION_LABEL,
  JOB_STATUS_LABEL,
  describeAuditChange,
  formatUtc,
  labelFor,
  methodLabel,
  nextApplicationStatuses,
  parseAppliedAt,
} from '../jobs'
import { ProblemAlert } from './ProblemAlert'

const MAX_REASON_LENGTH = 500

type Mode = 'none' | 'confirm' | 'unapply' | 'correct'

function validateReason(value: string, required: boolean): string | null {
  const trimmed = value.trim()
  if (required && trimmed === '') return 'A short reason is required.'
  if (trimmed.length > MAX_REASON_LENGTH) {
    return `The reason can be at most ${MAX_REASON_LENGTH} characters.`
  }
  return null
}

interface Props {
  applicationId: string
  /** Called after a change was saved, so a list that shows this application can refresh. */
  onChanged?: () => void
}

/**
 * One application with its history and every manual action. Give it a `key` of the application id.
 * An application is never marked applied unless the user ticks the confirmation and submits it.
 */
export function ApplicationPanel({ applicationId, onChanged }: Props) {
  const { state, reload } = useLoad(() => getApplication(applicationId))
  const action = useAction()
  const [mode, setMode] = useState<Mode>('none')

  /** Runs a write; on success closes the open form, reloads the application and tells the parent. */
  async function perform(write: () => Promise<unknown>): Promise<boolean> {
    const result = await action.run(write)
    if (!result.ok) return false
    setMode('none')
    await reload()
    onChanged?.()
    return true
  }

  function openMode(next: Mode) {
    action.clearError()
    setMode(next)
  }

  if (state.kind === 'loading') return <p role="status">Loading application…</p>
  if (state.kind === 'error') return <ProblemAlert error={state.error} subject="application" onReload={reload} />

  const application = state.data
  const busy = action.busy

  return (
    <section className="detail" aria-labelledby={`application-heading-${application.id}`}>
      <h3 id={`application-heading-${application.id}`}>
        Application: {application.job.title} at {application.job.employer.name}
      </h3>
      <dl>
        <dt>Status</dt>
        <dd data-testid="application-status">
          {labelFor(APPLICATION_STATUS_LABEL, application.status)}
        </dd>
        <dt>Applied</dt>
        <dd data-testid="application-applied">{application.isApplied ? 'Yes' : 'No'}</dd>
        <dt>Method</dt>
        <dd>{methodLabel(application.method)}</dd>
        <dt>Date and time applied</dt>
        <dd>{formatUtc(application.appliedAtUtc)}</dd>
        <dt>Confirmed on</dt>
        <dd>{formatUtc(application.appliedConfirmedAtUtc)}</dd>
        <dt>Job status</dt>
        <dd>{labelFor(JOB_STATUS_LABEL, application.job.status)}</dd>
      </dl>
      {!application.isApplied && (
        <p className="hint">
          Not applied. Saving, preparing or filling in an application never marks it as applied;
          only your own confirmation does.
        </p>
      )}

      {action.error && (
        <ProblemAlert error={action.error} subject="application" onReload={() => void reload()} />
      )}

      <div className="buttons" role="group" aria-label="Application actions">
        {!application.isApplied && (
          <button type="button" onClick={() => openMode('confirm')} disabled={busy}>
            Confirm as applied
          </button>
        )}
        {application.isApplied && (
          <button type="button" onClick={() => openMode('unapply')} disabled={busy}>
            Remove applied
          </button>
        )}
        <button type="button" onClick={() => openMode('correct')} disabled={busy}>
          Correct
        </button>
      </div>

      {mode === 'confirm' && (
        <ConfirmForm
          busy={busy}
          onCancel={() => openMode('none')}
          onSubmit={(appliedAtUtc) => perform(() => confirmApplied(application.id, appliedAtUtc))}
        />
      )}
      {mode === 'unapply' && (
        <UnapplyForm
          busy={busy}
          onCancel={() => openMode('none')}
          onSubmit={(reason) => perform(() => unapplyApplication(application.id, reason))}
        />
      )}
      {mode === 'correct' && (
        <CorrectForm
          application={application}
          busy={busy}
          onCancel={() => openMode('none')}
          onSubmit={(correction) => perform(() => correctApplication(application.id, correction))}
        />
      )}

      <StatusForm
        application={application}
        busy={busy}
        onSubmit={(status, reason) =>
          perform(() => changeApplicationStatus(application.id, status, reason))
        }
      />

      <AuditTrail application={application} />
    </section>
  )
}

function ConfirmForm({
  busy,
  onCancel,
  onSubmit,
}: {
  busy: boolean
  onCancel: () => void
  onSubmit: (appliedAtUtc: string | undefined) => Promise<boolean>
}) {
  const errorId = useId()
  const [confirmed, setConfirmed] = useState(false)
  const [appliedAt, setAppliedAt] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!confirmed) {
      setError('Tick the box to confirm that you submitted this application yourself.')
      return
    }
    const parsed = parseAppliedAt(appliedAt)
    setError(parsed.error)
    if (parsed.error) return
    await onSubmit(parsed.iso ?? undefined)
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="stack" aria-label="Confirm application">
      <label className="inline">
        <input
          type="checkbox"
          checked={confirmed}
          onChange={(e) => setConfirmed(e.target.checked)}
          disabled={busy}
        />
        I submitted this application myself.
      </label>
      <label>
        Date and time applied (optional, defaults to now)
        <input
          type="datetime-local"
          value={appliedAt}
          onChange={(e) => setAppliedAt(e.target.value)}
          aria-invalid={error !== null}
          aria-describedby={error ? errorId : undefined}
          disabled={busy}
        />
      </label>
      {error && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      <div className="buttons">
        <button type="submit" disabled={busy || !confirmed}>
          Confirm applied
        </button>
        <button type="button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function UnapplyForm({
  busy,
  onCancel,
  onSubmit,
}: {
  busy: boolean
  onCancel: () => void
  onSubmit: (reason: string) => Promise<boolean>
}) {
  const errorId = useId()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const problem = validateReason(reason, true)
    setError(problem)
    if (problem) return
    await onSubmit(reason.trim())
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="stack" aria-label="Remove applied">
      <p className="hint">
        This clears the applied flag, method and times. The old values stay in the history.
      </p>
      <label>
        Reason
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          aria-invalid={error !== null}
          aria-describedby={error ? errorId : undefined}
          disabled={busy}
        />
      </label>
      {error && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      <div className="buttons">
        <button type="submit" disabled={busy}>
          Remove applied
        </button>
        <button type="button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function CorrectForm({
  application,
  busy,
  onCancel,
  onSubmit,
}: {
  application: Application
  busy: boolean
  onCancel: () => void
  onSubmit: (correction: {
    status?: ApplicationStatus
    appliedAtUtc?: string
    reason: string
  }) => Promise<boolean>
}) {
  const errorId = useId()
  const [status, setStatus] = useState<'' | ApplicationStatus>('')
  const [appliedAt, setAppliedAt] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const parsed = parseAppliedAt(appliedAt)
    if (parsed.error) return setError(parsed.error)
    if (status === '' && parsed.iso === null) {
      return setError('Choose a new status or a new date and time.')
    }
    const reasonProblem = validateReason(reason, true)
    if (reasonProblem) return setError(reasonProblem)
    setError(null)
    await onSubmit({
      status: status === '' ? undefined : status,
      appliedAtUtc: parsed.iso ?? undefined,
      reason: reason.trim(),
    })
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="stack" aria-label="Correct application">
      <p className="hint">
        A correction fixes a mistake and is recorded in the history. It cannot make an application
        applied: use Confirm as applied for that.
      </p>
      <label>
        New status
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value as '' | ApplicationStatus)}
          disabled={busy}
        >
          <option value="">Keep the current status</option>
          {APPLICATION_STATUSES.map((s) => (
            <option key={s} value={s}>
              {APPLICATION_STATUS_LABEL[s]}
            </option>
          ))}
        </select>
      </label>
      {application.isApplied && (
        <label>
          Corrected date and time applied
          <input
            type="datetime-local"
            value={appliedAt}
            onChange={(e) => setAppliedAt(e.target.value)}
            disabled={busy}
          />
        </label>
      )}
      <label>
        Reason for the correction
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          aria-invalid={error !== null}
          aria-describedby={error ? errorId : undefined}
          disabled={busy}
        />
      </label>
      {error && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      <div className="buttons">
        <button type="submit" disabled={busy}>
          Save correction
        </button>
        <button type="button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function StatusForm({
  application,
  busy,
  onSubmit,
}: {
  application: Application
  busy: boolean
  onSubmit: (status: ApplicationStatus, reason: string | undefined) => Promise<boolean>
}) {
  const errorId = useId()
  const options = nextApplicationStatuses(application.status)
  const [status, setStatus] = useState<'' | ApplicationStatus>('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  if (options.length === 0) {
    return (
      <p className="hint">
        No further status changes are possible. A correction can reopen this application.
      </p>
    )
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (status === '') return setError('Choose a status.')
    const reasonProblem = validateReason(reason, false)
    if (reasonProblem) return setError(reasonProblem)
    setError(null)
    const saved = await onSubmit(status, reason.trim() === '' ? undefined : reason.trim())
    if (saved) {
      setStatus('')
      setReason('')
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="stack" aria-label="Change status">
      <label>
        Change status to
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value as '' | ApplicationStatus)}
          disabled={busy}
        >
          <option value="">Choose a status</option>
          {options.map((s) => (
            <option key={s} value={s}>
              {APPLICATION_STATUS_LABEL[s]}
            </option>
          ))}
        </select>
      </label>
      <label>
        Reason (optional)
        <input value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} />
      </label>
      {error && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      <div className="buttons">
        <button type="submit" disabled={busy}>
          Update status
        </button>
      </div>
    </form>
  )
}

function AuditTrail({ application }: { application: Application }) {
  return (
    <>
      <h4>History</h4>
      <ol className="history" aria-label="Application history">
        {application.auditTrail.map((entry, i) => (
          <li key={`${i}-${entry.changedAtUtc}`}>
            <strong>{labelFor(AUDIT_ACTION_LABEL, entry.action)}</strong>
            <span className="meta block">
              {formatUtc(entry.changedAtUtc)} · {entry.actor}
            </span>
            <span className="meta block">{describeAuditChange(entry)}</span>
            {entry.reason && <span className="meta block">Reason: {entry.reason}</span>}
          </li>
        ))}
      </ol>
    </>
  )
}
