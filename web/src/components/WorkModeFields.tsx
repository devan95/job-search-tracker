import { useId } from 'react'
import type { WorkMode } from '../api'
import { toggleMode, WORK_MODES, WORK_MODE_LABEL } from '../workModes'

interface ChecksProps {
  legend: string
  value: readonly WorkMode[]
  onChange: (next: WorkMode[]) => void
  disabled?: boolean
  /** Messages the API reported for this field; shown in the group and linked to it. */
  errors?: readonly string[]
}

/** The error text of a field group, linked to it with aria-describedby. */
function GroupError({ id, errors }: { id: string; errors: readonly string[] }) {
  return (
    <p className="field-error" id={id}>
      {errors.join(' ')}
    </p>
  )
}

/** A set of work-mode checkboxes. Nothing checked is the empty set. */
export function WorkModeChecks({ legend, value, onChange, disabled, errors = [] }: ChecksProps) {
  const errorId = useId()
  const invalid = errors.length > 0
  return (
    <fieldset
      disabled={disabled}
      aria-invalid={invalid || undefined}
      aria-describedby={invalid ? errorId : undefined}
    >
      <legend>{legend}</legend>
      {WORK_MODES.map((mode) => (
        <label key={mode} className="inline">
          <input
            type="checkbox"
            checked={value.includes(mode)}
            onChange={(e) => onChange(toggleMode(value, mode, e.target.checked))}
          />{' '}
          {WORK_MODE_LABEL[mode]}
        </label>
      ))}
      {invalid && <GroupError id={errorId} errors={errors} />}
    </fieldset>
  )
}

interface FieldProps {
  /** null = inherit the selected lists' defaults; [] = explicit empty set. */
  value: WorkMode[] | null
  onChange: (next: WorkMode[] | null) => void
  disabled?: boolean
  /** Messages the API reported for this field; shown in the group and linked to it. */
  errors?: readonly string[]
}

/**
 * Work modes of a search profile. "Inherit" (null) and an explicit set are two
 * different choices, and an explicit set may be empty.
 */
export function WorkModeField({ value, onChange, disabled, errors = [] }: FieldProps) {
  const name = useId()
  const errorId = useId()
  const inherit = value === null
  const invalid = errors.length > 0
  return (
    <fieldset
      disabled={disabled}
      aria-invalid={invalid || undefined}
      aria-describedby={invalid ? errorId : undefined}
    >
      <legend>Work modes</legend>
      <label className="inline">
        <input
          type="radio"
          name={name}
          checked={inherit}
          onChange={() => onChange(null)}
        />{' '}
        Inherit from the selected lists
      </label>
      <label className="inline">
        <input
          type="radio"
          name={name}
          checked={!inherit}
          onChange={() => onChange(value ?? [])}
        />{' '}
        Choose work modes for this profile
      </label>
      {value !== null && (
        <>
          <WorkModeChecks legend="Allowed work modes" value={value} onChange={onChange} />
          {value.length === 0 && (
            <p className="hint" data-testid="explicit-empty-note">
              Explicit empty set: this overrides the list defaults and allows no work mode.
            </p>
          )}
        </>
      )}
      {invalid && <GroupError id={errorId} errors={errors} />}
    </fieldset>
  )
}
