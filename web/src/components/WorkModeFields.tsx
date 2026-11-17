import { useId } from 'react'
import type { WorkMode } from '../api'
import { toggleMode, WORK_MODES, WORK_MODE_LABEL } from '../workModes'

interface ChecksProps {
  legend: string
  value: readonly WorkMode[]
  onChange: (next: WorkMode[]) => void
  disabled?: boolean
  invalid?: boolean
}

/** A set of work-mode checkboxes. Nothing checked is the empty set. */
export function WorkModeChecks({ legend, value, onChange, disabled, invalid }: ChecksProps) {
  return (
    <fieldset disabled={disabled} aria-invalid={invalid || undefined}>
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
    </fieldset>
  )
}

interface FieldProps {
  /** null = inherit the selected lists' defaults; [] = explicit empty set. */
  value: WorkMode[] | null
  onChange: (next: WorkMode[] | null) => void
  disabled?: boolean
  invalid?: boolean
}

/**
 * Work modes of a search profile. "Inherit" (null) and an explicit set are two
 * different choices, and an explicit set may be empty.
 */
export function WorkModeField({ value, onChange, disabled, invalid }: FieldProps) {
  const name = useId()
  const inherit = value === null
  return (
    <fieldset disabled={disabled} aria-invalid={invalid || undefined}>
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
    </fieldset>
  )
}
