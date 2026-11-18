import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createCandidate,
  createSearchProfile,
  deleteCandidate,
  fetchHealth,
  listCandidates,
  readProblem,
  resolveEmployer,
  updateSearchProfile,
} from './api'

afterEach(() => vi.restoreAllMocks())

const json = (status: number, body: unknown) =>
  vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    }),
  ) as unknown as typeof fetch

describe('fetchHealth', () => {
  it('reports ready when the API returns 200', async () => {
    const result = await fetchHealth(json(200, { status: 'ok', database: 'ok' }))
    expect(result.kind).toBe('ready')
  })

  it('reports degraded (not unreachable) when the API returns 503 with a body', async () => {
    const result = await fetchHealth(
      json(503, { status: 'degraded', database: 'unavailable' }),
    )
    expect(result).toMatchObject({
      kind: 'degraded',
      response: { database: 'unavailable' },
    })
  })

  it('reports unreachable when the request fails', async () => {
    const failing = vi.fn().mockRejectedValue(new Error('boom')) as unknown as typeof fetch
    expect(await fetchHealth(failing)).toEqual({
      kind: 'unreachable',
      reason: 'boom',
    })
  })

  it('reports unreachable on unexpected status codes', async () => {
    const result = await fetchHealth(json(500, {}))
    expect(result).toEqual({ kind: 'unreachable', reason: 'Unexpected HTTP 500' })
  })
})

describe('readProblem', () => {
  const reply = (status: number, body: unknown) =>
    new Response(typeof body === 'string' ? body : JSON.stringify(body), { status })

  it('reads a validation problem with its field errors', async () => {
    const problem = await readProblem(
      reply(400, {
        title: 'Validation failed',
        status: 400,
        code: 'validation',
        errors: { name: ['A name is required.'], defaultWorkModes: ['Unknown work mode.'] },
      }),
    )
    expect(problem).toEqual({
      status: 400,
      code: 'validation',
      title: 'Validation failed',
      errors: { name: ['A name is required.'], defaultWorkModes: ['Unknown work mode.'] },
    })
  })

  it('reads the counts and ids of conflict problems', async () => {
    expect(
      await readProblem(
        reply(409, { title: 'x', code: 'candidate_has_profiles', searchProfileCount: 2 }),
      ),
    ).toMatchObject({ status: 409, code: 'candidate_has_profiles', searchProfileCount: 2 })
    expect(
      await readProblem(reply(409, { title: 'x', code: 'duplicate_name', existingId: 'e1' })),
    ).toMatchObject({ code: 'duplicate_name', existingId: 'e1' })
    expect(
      await readProblem(
        reply(409, { title: 'x', code: 'employer_in_company_list', companyListCount: 3 }),
      ),
    ).toMatchObject({ code: 'employer_in_company_list', companyListCount: 3 })
  })

  it('falls back to an unexpected problem when the body is not a problem document', async () => {
    expect(await readProblem(reply(502, '<html>bad gateway</html>'))).toEqual({
      status: 502,
      code: 'unexpected',
      title: 'Unexpected HTTP 502',
    })
  })

  it('ignores malformed field errors', async () => {
    const problem = await readProblem(
      reply(400, { title: 't', code: 'validation', errors: 'nope' }),
    )
    expect(problem.errors).toBeUndefined()
  })
})

describe('management API client', () => {
  function stub(reply: () => Response) {
    return vi.spyOn(globalThis, 'fetch').mockImplementation(async () => reply())
  }
  const lastCall = (spy: ReturnType<typeof stub>) => {
    const [url, init] = spy.mock.calls[0]
    return { url: String(url), init: (init ?? {}) as RequestInit }
  }

  it('lists candidates with a plain GET', async () => {
    const spy = stub(() => new Response(JSON.stringify([{ id: 'c1', name: 'Asha' }]), { status: 200 }))
    expect(await listCandidates()).toEqual([{ id: 'c1', name: 'Asha' }])
    const { url, init } = lastCall(spy)
    expect(url).toBe('/api/candidates?take=500')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
  })

  it('sends JSON bodies for writes', async () => {
    const spy = stub(() => new Response(JSON.stringify({ id: 'c1', name: 'Asha' }), { status: 201 }))
    await createCandidate('Asha')
    const { url, init } = lastCall(spy)
    expect(url).toBe('/api/candidates')
    expect(init.method).toBe('POST')
    expect(init.body).toBe('{"name":"Asha"}')
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json' })
  })

  it('treats 204 as success with no body', async () => {
    const spy = stub(() => new Response(null, { status: 204 }))
    await expect(deleteCandidate('c 1')).resolves.toBeUndefined()
    const { url, init } = lastCall(spy)
    expect(url).toBe('/api/candidates/c%201')
    expect(init.method).toBe('DELETE')
  })

  it('treats a null or scalar success body as an invalid response', async () => {
    stub(() => new Response('null', { status: 201 }))
    await expect(createCandidate('Asha')).rejects.toMatchObject({ kind: 'invalid_response' })
    vi.restoreAllMocks()
    stub(() => new Response('"ok"', { status: 200 }))
    await expect(createCandidate('Asha')).rejects.toMatchObject({ kind: 'invalid_response' })
  })

  it('treats a list endpoint answering anything but an array as an invalid response', async () => {
    stub(() => new Response('{}', { status: 200 }))
    await expect(listCandidates()).rejects.toMatchObject({ kind: 'invalid_response' })
    vi.restoreAllMocks()
    stub(() => new Response('[]', { status: 200 }))
    await expect(listCandidates()).resolves.toEqual([])
  })

  it('throws a problem error carrying the parsed problem document', async () => {
    stub(
      () =>
        new Response(
          JSON.stringify({
            title: 'Validation failed',
            status: 400,
            code: 'validation',
            errors: { name: ['A name is required.'] },
          }),
          { status: 400 },
        ),
    )
    await expect(createCandidate('x')).rejects.toMatchObject({
      kind: 'problem',
      problem: { code: 'validation', errors: { name: ['A name is required.'] } },
    })
  })

  it('throws a network error when the request fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    await expect(listCandidates()).rejects.toMatchObject({ kind: 'network', message: 'offline' })
  })

  it('throws an invalid-response error when a success body is not JSON', async () => {
    stub(() => new Response('not json', { status: 200 }))
    await expect(listCandidates()).rejects.toMatchObject({ kind: 'invalid_response' })
  })

  it('tells a new employer (201) from a reused one (200)', async () => {
    const employer = { id: 'e1', name: 'Acme', normalizedName: 'acme' }
    stub(() => new Response(JSON.stringify(employer), { status: 201 }))
    expect(await resolveEmployer('Acme')).toEqual({ employer, created: true })
    vi.restoreAllMocks()
    stub(() => new Response(JSON.stringify(employer), { status: 200 }))
    expect(await resolveEmployer('ACME')).toEqual({ employer, created: false })
  })

  it('keeps null (inherit) and [] (explicit empty set) apart when saving profiles', async () => {
    const profile = {
      candidateId: 'c1',
      name: 'Backend',
      companyListIds: [],
      companyMonitoringEnabled: false,
      broadSearchEnabled: false,
    }
    const created = (workModes: null | []) => {
      const spy = stub(() => new Response('{}', { status: 201 }))
      return createSearchProfile({ ...profile, workModes }).then(() => {
        const body = JSON.parse(String(lastCall(spy).init.body)) as Record<string, unknown>
        vi.restoreAllMocks()
        return body
      })
    }
    expect('workModes' in (await created(null))).toBe(false)
    expect((await created([])).workModes).toEqual([])

    const spy = stub(() => new Response('{}', { status: 200 }))
    await updateSearchProfile('p1', {
      name: 'Backend',
      workModes: null,
      companyMonitoringEnabled: true,
      broadSearchEnabled: false,
    })
    const body = JSON.parse(String(lastCall(spy).init.body)) as Record<string, unknown>
    expect(body.workModes).toBeNull()
  })
})
