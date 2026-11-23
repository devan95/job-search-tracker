import type { Location, LocationScope } from './api'

export const LOCATION_SCOPES: readonly LocationScope[] = [
  'city',
  'state',
  'country',
  'remoteCountry',
  'remoteWorldwide',
]

export const SCOPE_LABEL: Record<LocationScope, string> = {
  city: 'City',
  state: 'State or region',
  country: 'Country',
  remoteCountry: 'Remote within a country',
  remoteWorldwide: 'Remote worldwide',
}

const COUNTRY_CODE = /^[A-Za-z]{2}$/

/**
 * Client-side check that mirrors the API rules for a location; the API stays the authority.
 * `countryCode` is the raw text of the field.
 */
export function validateLocation(
  label: string,
  scope: LocationScope,
  countryCode: string,
): { label: string | null; countryCode: string | null } {
  const code = countryCode.trim()
  let labelProblem: string | null = null
  if (label.trim().length === 0) labelProblem = 'A label is required.'
  let codeProblem: string | null = null
  if (scope === 'remoteWorldwide') {
    if (code !== '') codeProblem = 'Remote worldwide has no country; leave the country code empty.'
  } else if (code === '') {
    if (scope === 'remoteCountry') codeProblem = 'Remote within a country needs a two-letter country code, such as IN.'
  } else if (!COUNTRY_CODE.test(code)) {
    codeProblem = 'A country code is two letters, such as IN.'
  }
  return { label: labelProblem, countryCode: codeProblem }
}

/** The body a form sends: trimmed label, upper-case code, null when there is no code. */
export function toLocationInput(label: string, scope: LocationScope, countryCode: string) {
  const code = countryCode.trim().toUpperCase()
  return { label: label.trim(), scope, countryCode: scope === 'remoteWorldwide' || code === '' ? null : code }
}

/** "Remote India · Remote within a country · IN": the scope is always spelled out, never implied by the label. */
export function describeLocation(location: Location): string {
  const scope = SCOPE_LABEL[location.scope] ?? location.scope
  return [location.label, scope, location.countryCode].filter(Boolean).join(' · ')
}
