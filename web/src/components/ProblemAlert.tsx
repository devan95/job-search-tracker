import type { ApiError } from '../api'
import { describeError } from '../problems'

interface Props {
  error: ApiError
  /** What the form is about, e.g. "company list"; used in the duplicate-name message. */
  subject?: string
  onReload?: () => void
}

export function ProblemAlert({ error, subject, onReload }: Props) {
  const view = describeError(error, subject)
  return (
    <div role="alert" className="problem" data-code={view.code}>
      <strong>{view.heading}</strong>
      <p>{view.message}</p>
      {view.details.length > 0 && (
        <ul>
          {view.details.map((detail, i) => (
            <li key={`${i}-${detail}`}>{detail}</li>
          ))}
        </ul>
      )}
      {onReload && view.reloadable && (
        <button type="button" onClick={onReload}>
          Reload
        </button>
      )}
    </div>
  )
}
