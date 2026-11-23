import { describe, expect, it } from 'vitest'
import { describeLocation, toLocationInput, validateLocation } from './locations'

describe('validateLocation', () => {
  it('requires a label', () => {
    expect(validateLocation('  ', 'city', '').label).toBe('A label is required.')
    expect(validateLocation('Pune', 'city', '').label).toBeNull()
  })

  it('requires a code for remote within a country, and only two letters', () => {
    expect(validateLocation('Remote India', 'remoteCountry', '').countryCode).toMatch(/needs a two-letter/)
    expect(validateLocation('Remote India', 'remoteCountry', 'IN').countryCode).toBeNull()
    expect(validateLocation('Remote India', 'remoteCountry', 'IND').countryCode).toMatch(/two letters/)
    expect(validateLocation('Pune', 'city', '1N').countryCode).toMatch(/two letters/)
  })

  it('allows no code for other scopes and forbids one for remote worldwide', () => {
    expect(validateLocation('Pune', 'city', '').countryCode).toBeNull()
    expect(validateLocation('Gujarat', 'state', ' in ').countryCode).toBeNull()
    expect(validateLocation('Anywhere', 'remoteWorldwide', '').countryCode).toBeNull()
    expect(validateLocation('Anywhere', 'remoteWorldwide', 'IN').countryCode).toMatch(/no country/)
  })
})

describe('toLocationInput', () => {
  it('trims, upper-cases and turns an empty code into null', () => {
    expect(toLocationInput(' Pune ', 'city', ' in ')).toEqual({ label: 'Pune', scope: 'city', countryCode: 'IN' })
    expect(toLocationInput('Pune', 'city', '')).toEqual({ label: 'Pune', scope: 'city', countryCode: null })
  })

  it('never sends a code for remote worldwide', () => {
    expect(toLocationInput('Anywhere', 'remoteWorldwide', 'IN').countryCode).toBeNull()
  })
})

describe('describeLocation', () => {
  it('always spells out the scope', () => {
    expect(describeLocation({ id: '1', label: 'Remote India', scope: 'remoteCountry', countryCode: 'IN' })).toBe(
      'Remote India · Remote within a country · IN',
    )
    expect(describeLocation({ id: '2', label: 'Remote', scope: 'remoteWorldwide', countryCode: null })).toBe(
      'Remote · Remote worldwide',
    )
  })
})
