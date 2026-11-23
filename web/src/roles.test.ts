import { describe, expect, it } from 'vitest'
import { normalizeText, parseAliases, validateTitle } from './roles'

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

  it('counts aliases after removing repeats, as the server keeps a repeated alias once', () => {
    const same = Array.from({ length: 51 }, () => 'SDE')
    expect(validateTitle('Analyst', same).aliases).toBeNull()
    const spacedAndCased = ['SDE', ' sde ', 'S  D  E'.replace(/ +/g, ''), 'Sde']
    expect(validateTitle('Analyst', spacedAndCased).aliases).toBeNull()
    const distinct = Array.from({ length: 51 }, (_, i) => `A${i}`)
    expect(validateTitle('Analyst', [...distinct, 'a0']).aliases).toMatch(/at most 50/)
  })

  it('allows at most 50 aliases', () => {
    const aliases = (n: number) => Array.from({ length: n }, (_, i) => `A${i}`)
    expect(validateTitle('Analyst', aliases(50)).aliases).toBeNull()
    expect(validateTitle('Analyst', aliases(51)).aliases).toMatch(/at most 50/)
  })
})

describe('normalizeText', () => {
  it('folds case, collapses whitespace runs and applies NFKC like the server', () => {
    expect(normalizeText('  Software \t Engineer ')).toBe('software engineer')
    expect(normalizeText('ＳＤＥ')).toBe('sde')
  })
})
