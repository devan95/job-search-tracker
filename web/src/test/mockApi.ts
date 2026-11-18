import { vi } from 'vitest'

export interface MockRequest {
  method: string
  path: string
  search: string
  body: unknown
}

export interface MockReply {
  status: number
  body?: unknown
}

export type MockHandler =
  | MockReply
  | ((req: MockRequest) => MockReply | Promise<MockReply>)

/** A problem document like the API's, for error-state tests. */
export function problem(
  status: number,
  code: string,
  extra: Record<string, unknown> = {},
): MockReply {
  return { status, body: { title: `Problem ${code}`, status, code, ...extra } }
}

/** A promise the test resolves by hand, to hold an API reply back. */
export function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { promise, resolve }
}

function toResponse(reply: MockReply): Response {
  if (reply.status === 204 || reply.body === undefined) {
    return new Response(null, { status: reply.status })
  }
  return new Response(JSON.stringify(reply.body), {
    status: reply.status,
    headers: {
      'Content-Type': reply.status >= 400 ? 'application/problem+json' : 'application/json',
    },
  })
}

/**
 * Replaces fetch with a router. Keys are "METHOD /api/path" (no query string).
 * A handler may be a fixed reply or a function (sync or async), so tests can keep state in a
 * closure or hold a reply back until the test releases it.
 * Unknown routes answer 404. Returns the list of requests made.
 */
export function mockApi(handlers: Record<string, MockHandler>): MockRequest[] {
  const calls: MockRequest[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost')
    const method = init?.method ?? 'GET'
    const req: MockRequest = {
      method,
      path: url.pathname,
      search: url.search,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    }
    calls.push(req)
    const handler = handlers[`${method} ${url.pathname}`]
    const reply =
      typeof handler === 'function' ? await handler(req) : (handler ?? problem(404, 'not_found'))
    return toResponse(reply)
  })
  return calls
}
