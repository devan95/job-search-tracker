import { describe, expect, it } from 'vitest'
import { ApiError, type Problem } from './api'
import { describeError, fieldMessages, humanizeField } from './problems'

const problemError = (problem: Partial<Problem> & { code: string; status: number }) =>
  new ApiError('problem', 'title', { title: 'title', ...problem })

describe('describeError', () => {
  it('lists every field error of a validation problem', () => {
    const view = describeError(
      problemError({
        status: 400,
        code: 'validation',
        errors: { name: ['A name is required.'], defaultWorkModes: ['Unknown work mode.'] },
      }),
    )
    expect(view.heading).toBe('Check the form')
    expect(view.details).toEqual([
      'Name: A name is required.',
      'Default work modes: Unknown work mode.',
    ])
  })

  it('explains each documented 404 and 409 code', () => {
    const cases: [Partial<Problem> & { code: string; status: number }, string][] = [
      [{ status: 404, code: 'not_found' }, 'No longer exists'],
      [{ status: 404, code: 'not_a_member' }, 'Already removed'],
      [{ status: 409, code: 'duplicate_name' }, 'Name already in use'],
      [{ status: 409, code: 'candidate_has_profiles', searchProfileCount: 2 }, 'Candidate still has search profiles'],
      [{ status: 409, code: 'company_list_in_use', searchProfileCount: 1 }, 'List is in use'],
      [{ status: 409, code: 'employer_in_company_list', companyListCount: 2 }, 'Company is still in a list'],
    ]
    for (const [problem, heading] of cases) {
      expect(describeError(problemError(problem)).heading).toBe(heading)
    }
  })

  it('puts the counts in the conflict messages', () => {
    expect(
      describeError(problemError({ status: 409, code: 'candidate_has_profiles', searchProfileCount: 2 })).message,
    ).toContain('2 search profiles')
    expect(
      describeError(problemError({ status: 409, code: 'company_list_in_use', searchProfileCount: 1 })).message,
    ).toContain('1 search profile ')
    expect(
      describeError(problemError({ status: 409, code: 'employer_in_company_list', companyListCount: 1 })).message,
    ).toContain('1 company list.')
  })

  it('names the subject in the duplicate-name message', () => {
    const view = describeError(problemError({ status: 409, code: 'duplicate_name' }), 'company list')
    expect(view.message).toContain('Another company list')
  })

  it('treats 5xx as a server error and unknown 4xx as a failed request', () => {
    expect(describeError(problemError({ status: 500, code: 'unexpected' })).heading).toBe('Server error')
    expect(describeError(problemError({ status: 418, code: 'teapot', detail: 'Short and stout' }))).toMatchObject({
      heading: 'Request failed',
      message: 'Short and stout',
    })
  })

  it('describes network and unreadable-response errors', () => {
    expect(describeError(new ApiError('network', 'offline'))).toMatchObject({
      code: 'network',
      heading: 'Cannot reach the API',
      reloadable: true,
    })
    const message = describeError(new ApiError('network', 'offline')).message
    expect(message).toContain('offline')
    expect(message).toContain('may or may not have been applied')
    expect(message).not.toContain('did not reach')
    expect(describeError(new ApiError('invalid_response', 'bad')).heading).toBe('Unreadable response')
  })
})

describe('fieldMessages', () => {
  it('matches field names ignoring case', () => {
    const error = problemError({ status: 400, code: 'validation', errors: { Name: ['Required.'] } })
    expect(fieldMessages(error, 'name')).toEqual(['Required.'])
    expect(fieldMessages(error, 'workModes')).toEqual([])
    expect(fieldMessages(null, 'name')).toEqual([])
  })
})

describe('humanizeField', () => {
  it('turns camelCase into a sentence', () => {
    expect(humanizeField('defaultWorkModes')).toBe('Default work modes')
    expect(humanizeField('name')).toBe('Name')
  })
})
