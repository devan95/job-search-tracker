import { useCallback, useEffect, useState } from 'react'
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
 * data already shown, except after an error, where it returns to loading.
 * Give the component a `key` to load something different.
 */
export function useLoad<T>(loader: () => Promise<T>): {
  state: LoadState<T>
  reload: () => void
} {
  const [state, setState] = useState<LoadState<T>>({ kind: 'loading' })
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let cancelled = false
    loader().then(
      (data) => {
        if (!cancelled) setState({ kind: 'ready', data })
      },
      (err: unknown) => {
        if (!cancelled) setState({ kind: 'error', error: toApiError(err) })
      },
    )
    return () => {
      cancelled = true
    }
    // The loader is fixed for the life of the component; tick triggers reloads.
  }, [tick])

  const reload = useCallback(() => {
    setState((prev) => (prev.kind === 'error' ? { kind: 'loading' } : prev))
    setTick((t) => t + 1)
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
