import { useState, type FormEvent } from 'react'
import type { ApiError } from '../api'
import { fieldMessages } from '../problems'
import { validateName } from '../validation'

interface Props {
  title: string
  /** Label of the name field, e.g. "Role list name". */
  nameLabel: string
  initialName: string
  submitLabel: string
  busy: boolean
  error: ApiError | null
  clearOnSuccess?: boolean
  onSubmit: (name: string) => Promise<boolean>
}

/** A form with one name field, used to create and rename lists and sets. */
export function NameForm({
  title,
  nameLabel,
  initialName,
  submitLabel,
  busy,
  error,
  clearOnSuccess,
  onSubmit,
}: Props) {
  const [name, setName] = useState(initialName)
  const [nameError, setNameError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const problem = validateName(name)
    setNameError(problem)
    if (problem) return
    const ok = await onSubmit(name.trim())
    if (ok && clearOnSuccess) setName('')
  }

  return (
    <form aria-label={title} onSubmit={(e) => void submit(e)} noValidate className="stack">
      <h3>{title}</h3>
      <label>
        {nameLabel}
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-invalid={nameError !== null || fieldMessages(error, 'name').length > 0}
          disabled={busy}
        />
      </label>
      {nameError && <p className="field-error">{nameError}</p>}
      <button type="submit" disabled={busy}>
        {submitLabel}
      </button>
    </form>
  )
}
