import type { WorkMode } from './api'

export const WORK_MODES: readonly WorkMode[] = ['remote', 'onSite', 'hybrid']

export const WORK_MODE_LABEL: Record<WorkMode, string> = {
  remote: 'Remote',
  onSite: 'On-site',
  hybrid: 'Hybrid',
}

function labels(modes: readonly WorkMode[]): string {
  return WORK_MODES.filter((m) => modes.includes(m))
    .map((m) => WORK_MODE_LABEL[m])
    .join(', ')
}

/** A company list's default modes; the empty set means no default is stated. */
export function describeDefaultModes(modes: readonly WorkMode[]): string {
  return modes.length === 0 ? 'No default stated' : labels(modes)
}

/** A profile's work modes, keeping null (inherit), [] (explicit empty) and a set apart. */
export function describeProfileModes(modes: readonly WorkMode[] | null): string {
  if (modes === null) return "Inherits each selected list's default"
  if (modes.length === 0) return 'Explicit empty set (overrides list defaults; no work mode accepted)'
  return `${labels(modes)} (overrides list defaults)`
}

export function toggleMode(modes: readonly WorkMode[], mode: WorkMode, on: boolean): WorkMode[] {
  const without = modes.filter((m) => m !== mode)
  return on ? WORK_MODES.filter((m) => m === mode || without.includes(m)) : without
}
