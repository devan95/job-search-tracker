import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from './api'

export function toApiError(err: unknown): ApiError {
  if (err instanceof ApiError) return err
  return new ApiError('network', err instanceof Error ? err.message : 'Request failed')
}

export type LoadState<T> =
  | { kind: 'loading' }
  | { kind: 'ready'; data: T }
  | { kind: 'error'; error: ApiError }

/**
 * Loads data when the component mounts. `reload` refetches without clearing
 * data already shown, except after an error, where it returns to loading. Its
 * promise resolves once that refetch has settled (loaded or failed) and the new
 * state is set, or when the component unmounts; it never rejects, so callers
 * that do not care can ignore it.
 * Give the component a `key` to load something different.
 */
export function useLoad<T>(loader: () => Promise<T>): {
  state: LoadState<T>
  reload: () => Promise<void>
} {
  const [state, setState] = useState<LoadState<T>>({ kind: 'loading' })
  const [tick, setTick] = useState(0)
  const waiting = useRef<Array<() => void>>([])

  const settle = useCallback(() => {
    const done = waiting.current
    waiting.current = []
    done.forEach((resolve) => resolve())
  }, [])

  useEffect(() => {
    let cancelled = false
    loader().then(
      (data) => {
        if (cancelled) return
        setState({ kind: 'ready', data })
        settle()
      },
      (err: unknown) => {
        if (cancelled) return
        setState({ kind: 'error', error: toApiError(err) })
        settle()
      },
    )
    return () => {
      cancelled = true
    }
    // The loader is fixed for the life of the component; tick triggers reloads.
  }, [tick])

  // Anyone still waiting when the component goes away must not hang.
  useEffect(() => settle, [settle])

  const reload = useCallback(() => {
    const settled = new Promise<void>((resolve) => waiting.current.push(resolve))
    setState((prev) => (prev.kind === 'error' ? { kind: 'loading' } : prev))
    setTick((t) => t + 1)
    return settled
  }, [])

  return { state, reload }
}

export type ActionResult<T> = { ok: true; value: T } | { ok: false }

/** Runs one write at a time, tracking whether it is busy and what went wrong. */
export function useAction(): {
  busy: boolean
  error: ApiError | null
  run: <T>(fn: () => Promise<T>) => Promise<ActionResult<T>>
  clearError: () => void
} {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)

  const run = useCallback(async <T,>(fn: () => Promise<T>): Promise<ActionResult<T>> => {
    setBusy(true)
    setError(null)
    try {
      return { ok: true, value: await fn() }
    } catch (err) {
      setError(toApiError(err))
      return { ok: false }
    } finally {
      setBusy(false)
    }
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { busy, error, run, clearError }
}
