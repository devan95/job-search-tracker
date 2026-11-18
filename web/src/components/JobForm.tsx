import { useId, useState, type FormEvent } from 'react'
import type { ApiError, EmploymentType, WorkMode } from '../api'
import {
  EMPLOYMENT_TYPE_LABEL,
  EMPLOYMENT_TYPES,
  validateJobForm,
  type JobFormErrors,
  type JobFormValues,
} from '../jobs'
import { fieldMessages } from '../problems'
import { WORK_MODE_LABEL, WORK_MODES } from '../workModes'
import { ProblemAlert } from './ProblemAlert'

interface TextFieldProps {
  label: string
  value: string
  onChange: (value: string) => void
  messages: string[]
  disabled: boolean
  type?: string
  multiline?: boolean
}

function TextField({ label, value, onChange, messages, disabled, type, multiline }: TextFieldProps) {
  const errorId = useId()
  const invalid = messages.length > 0
  const describedBy = invalid ? errorId : undefined
  return (
    <>
      <label>
        {label}
        {multiline ? (
          <textarea
            rows={6}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            aria-invalid={invalid}
            aria-describedby={describedBy}
            disabled={disabled}
          />
        ) : (
          <input
            type={type ?? 'text'}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            aria-invalid={invalid}
            aria-describedby={describedBy}
            disabled={disabled}
          />
        )}
      </label>
      {invalid && (
        <div id={errorId}>
          {messages.map((message, i) => (
            <p key={`${i}-${message}`} className="field-error">
              {message}
            </p>
          ))}
        </div>
      )}
    </>
  )
}

interface Props {
  initial: JobFormValues
  submitLabel: string
  busy: boolean
  /** The last server error of the save this form started. */
  error: ApiError | null
  onSubmit: (values: JobFormValues, confirmDifferentJob: boolean) => void
  onCancel: () => void
  /** Offered with a duplicate warning so the user can look at the job already saved. */
  onOpenExisting?: (jobId: string) => void
}

/** The manual job form: add and edit share it. The API's field errors are shown beside the fields. */
export function JobForm({
  initial,
  submitLabel,
  busy,
  error,
  onSubmit,
  onCancel,
  onOpenExisting,
}: Props) {
  const [values, setValues] = useState<JobFormValues>(initial)
  const [errors, setErrors] = useState<JobFormErrors>({})

  const set = <K extends keyof JobFormValues>(key: K, value: JobFormValues[K]) =>
    setValues((previous) => ({ ...previous, [key]: value }))

  const messages = (key: keyof JobFormValues): string[] => {
    const own = errors[key]
    return [...(own ? [own] : []), ...fieldMessages(error, key)]
  }

  function submit(confirmDifferentJob: boolean) {
    const found = validateJobForm(values)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    onSubmit(values, confirmDifferentJob)
  }

  function onFormSubmit(event: FormEvent) {
    event.preventDefault()
    submit(false)
  }

  const problem = error?.problem
  const existingId = problem?.existingId
  const openExisting = onOpenExisting
  const duplicateWarning = problem?.code === 'possible_duplicate'
  const isDuplicate = duplicateWarning || problem?.code === 'duplicate_requisition'
  const canOpenExisting = isDuplicate && existingId !== undefined && openExisting !== undefined

  return (
    <form onSubmit={onFormSubmit} noValidate className="stack" aria-label="Job form">
      <TextField
        label="Company"
        value={values.employer}
        onChange={(v) => set('employer', v)}
        messages={messages('employer')}
        disabled={busy}
      />
      <TextField
        label="Job title"
        value={values.title}
        onChange={(v) => set('title', v)}
        messages={messages('title')}
        disabled={busy}
      />
      <TextField
        label="Apply link"
        type="url"
        value={values.applyUrl}
        onChange={(v) => set('applyUrl', v)}
        messages={messages('applyUrl')}
        disabled={busy}
      />
      <TextField
        label="Location"
        value={values.location}
        onChange={(v) => set('location', v)}
        messages={messages('location')}
        disabled={busy}
      />
      <label>
        Work mode
        <select
          value={values.workMode}
          onChange={(e) => set('workMode', e.target.value as '' | WorkMode)}
          disabled={busy}
        >
          <option value="">Not stated</option>
          {WORK_MODES.map((mode) => (
            <option key={mode} value={mode}>
              {WORK_MODE_LABEL[mode]}
            </option>
          ))}
        </select>
      </label>
      {messages('workMode').map((message, i) => (
        <p key={`${i}-${message}`} className="field-error">
          {message}
        </p>
      ))}
      <label>
        Employment type
        <select
          value={values.employmentType}
          onChange={(e) => set('employmentType', e.target.value as '' | EmploymentType)}
          disabled={busy}
        >
          <option value="">Not stated</option>
          {EMPLOYMENT_TYPES.map((type) => (
            <option key={type} value={type}>
              {EMPLOYMENT_TYPE_LABEL[type]}
            </option>
          ))}
        </select>
      </label>
      {messages('employmentType').map((message, i) => (
        <p key={`${i}-${message}`} className="field-error">
          {message}
        </p>
      ))}
      <TextField
        label="Department"
        value={values.department}
        onChange={(v) => set('department', v)}
        messages={messages('department')}
        disabled={busy}
      />
      <TextField
        label="Minimum years of experience"
        type="number"
        value={values.experienceMinYears}
        onChange={(v) => set('experienceMinYears', v)}
        messages={messages('experienceMinYears')}
        disabled={busy}
      />
      <TextField
        label="Maximum years of experience"
        type="number"
        value={values.experienceMaxYears}
        onChange={(v) => set('experienceMaxYears', v)}
        messages={messages('experienceMaxYears')}
        disabled={busy}
      />
      <TextField
        label="Requisition id"
        value={values.requisitionId}
        onChange={(v) => set('requisitionId', v)}
        messages={messages('requisitionId')}
        disabled={busy}
      />
      <TextField
        label="Posted date"
        type="date"
        value={values.postedDate}
        onChange={(v) => set('postedDate', v)}
        messages={messages('postedDate')}
        disabled={busy}
      />
      <TextField
        label="Found on"
        value={values.foundOn}
        onChange={(v) => set('foundOn', v)}
        messages={messages('foundOn')}
        disabled={busy}
      />
      <TextField
        label="Description"
        multiline
        value={values.description}
        onChange={(v) => set('description', v)}
        messages={messages('description')}
        disabled={busy}
      />

      {error && <ProblemAlert error={error} subject="job" />}
      {(duplicateWarning || canOpenExisting) && (
        <div className="buttons">
          {duplicateWarning && (
            <button type="button" disabled={busy} onClick={() => submit(true)}>
              This is a different job: save anyway
            </button>
          )}
          {canOpenExisting && existingId && openExisting && (
            <button type="button" onClick={() => openExisting(existingId)}>
              Open the existing job
            </button>
          )}
        </div>
      )}

      <div className="buttons">
        <button type="submit" disabled={busy}>
          {submitLabel}
        </button>
        <button type="button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  )
}
