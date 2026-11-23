import { describe, expect, it } from 'vitest'
import { parseAliases, validateTitle } from './roles'

describe('parseAliases', () => {
  it('splits on lines, trims and drops blank lines', () => {
    expect(parseAliases('  SDE \r\n\nSoftware Developer\n')).toEqual(['SDE', 'Software Developer'])
    expect(parseAliases('')).toEqual([])
  })
})

describe('validateTitle', () => {
  it('requires a title', () => {
    expect(validateTitle('  ', []).title).toBe('A title is required.')
    expect(validateTitle('Analyst', []).title).toBeNull()
  })

  it('refuses an alias equal to its own title, ignoring case and spaces', () => {
    expect(validateTitle('Analyst', [' analyst ']).aliases).toMatch(/same as its own title/)
    expect(validateTitle('Analyst', ['Data Analyst']).aliases).toBeNull()
  })

  it('allows at most 50 aliases', () => {
    const aliases = (n: number) => Array.from({ length: n }, (_, i) => `A${i}`)
    expect(validateTitle('Analyst', aliases(50)).aliases).toBeNull()
    expect(validateTitle('Analyst', aliases(51)).aliases).toMatch(/at most 50/)
  })
})
