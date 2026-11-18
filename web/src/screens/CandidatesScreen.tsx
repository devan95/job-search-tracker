import { useId, useState, type FormEvent } from 'react'
import {
  createCandidate,
  deleteCandidate,
  listCandidates,
  updateCandidate,
  type Candidate,
} from '../api'
import { ProblemAlert } from '../components/ProblemAlert'
import { useAction, useLoad } from '../hooks'
import { fieldMessages } from '../problems'
import { validateName } from '../validation'

export default function CandidatesScreen() {
  const { state, reload } = useLoad(listCandidates)
  // Separate actions so a server error is shown against the form it came from.
  const createAction = useAction()
  const rowAction = useAction()
  const busy = createAction.busy || rowAction.busy
  const createErrorId = useId()
  const editErrorId = useId()
  const [name, setName] = useState('')
  const [nameError, setNameError] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editError, setEditError] = useState<string | null>(null)
  const [confirmingId, setConfirmingId] = useState<string | null>(null)
  // The candidate a rename was sent for; its server error belongs to that editor only.
  const [renamingId, setRenamingId] = useState<string | null>(null)

  function clearErrors() {
    createAction.clearError()
    rowAction.clearError()
  }

  async function onAdd(event: FormEvent) {
    event.preventDefault()
    const problem = validateName(name)
    setNameError(problem)
    if (problem) return
    rowAction.clearError()
    const result = await createAction.run(() => createCandidate(name.trim()))
    if (result.ok) {
      setName('')
      void reload()
    }
  }

  function startEdit(candidate: Candidate) {
    clearErrors()
    setConfirmingId(null)
    setEditingId(candidate.id)
    setEditName(candidate.name)
    setEditError(null)
  }

  async function onSave(event: FormEvent, candidate: Candidate) {
    event.preventDefault()
    const problem = validateName(editName)
    setEditError(problem)
    if (problem) return
    createAction.clearError()
    setRenamingId(candidate.id)
    const result = await rowAction.run(() => updateCandidate(candidate.id, editName.trim()))
    if (result.ok) {
      // The user may have moved on to another candidate's editor while this was saving.
      setEditingId((current) => (current === candidate.id ? null : current))
      void reload()
    }
  }

  async function onDelete(candidate: Candidate) {
    createAction.clearError()
    setRenamingId(null)
    const result = await rowAction.run(() => deleteCandidate(candidate.id))
    // A conflict or error is shown above the list; the row stays as it is.
    setConfirmingId(null)
    if (result.ok) void reload()
  }

  return (
    <section aria-labelledby="candidates-heading">
      <h2 id="candidates-heading">Candidates</h2>
      <p className="hint">
        A candidate is the person a search is run for. Search profiles belong to a candidate.
      </p>

      <form onSubmit={(e) => void onAdd(e)} noValidate className="stack">
        <label>
          Candidate name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-invalid={nameError !== null || fieldMessages(createAction.error, 'name').length > 0}
            aria-describedby={nameError ? createErrorId : undefined}
            disabled={busy}
          />
        </label>
        {nameError && (
          <p className="field-error" id={createErrorId}>
            {nameError}
          </p>
        )}
        <button type="submit" disabled={busy}>
          Add candidate
        </button>
      </form>

      {createAction.error && (
        <ProblemAlert error={createAction.error} subject="candidate" onReload={reload} />
      )}
      {rowAction.error && (
        <ProblemAlert error={rowAction.error} subject="candidate" onReload={reload} />
      )}

      {state.kind === 'loading' && <p role="status">Loading candidates…</p>}
      {state.kind === 'error' && <ProblemAlert error={state.error} onReload={reload} />}
      {state.kind === 'ready' && state.data.length === 0 && (
        <p>No candidates yet. Add one to start building search profiles.</p>
      )}
      {state.kind === 'ready' && state.data.length > 0 && (
        <ul className="rows" aria-label="Candidates">
          {state.data.map((candidate) => (
            <li key={candidate.id}>
              {editingId === candidate.id ? (
                <form onSubmit={(e) => void onSave(e, candidate)} noValidate className="stack">
                  <label>
                    New name for {candidate.name}
                    <input
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      aria-invalid={
                        editError !== null ||
                        (renamingId === candidate.id &&
                          fieldMessages(rowAction.error, 'name').length > 0)
                      }
                      aria-describedby={editError ? editErrorId : undefined}
                      disabled={busy}
                    />
                  </label>
                  {editError && (
                    <p className="field-error" id={editErrorId}>
                      {editError}
                    </p>
                  )}
                  <div className="buttons">
                    <button type="submit" disabled={busy}>
                      Save
                    </button>
                    <button type="button" onClick={() => setEditingId(null)}>
                      Cancel
                    </button>
                  </div>
                </form>
              ) : (
                <>
                  <span className="row-name">{candidate.name}</span>
                  <span className="buttons">
                    <button
                      type="button"
                      onClick={() => startEdit(candidate)}
                      aria-label={`Rename ${candidate.name}`}
                    >
                      Rename
                    </button>
                    {confirmingId === candidate.id ? (
                      <>
                        <button
                          type="button"
                          onClick={() => void onDelete(candidate)}
                          disabled={busy}
                          aria-label={`Confirm delete ${candidate.name}`}
                        >
                          Yes, delete
                        </button>
                        <button type="button" onClick={() => setConfirmingId(null)}>
                          Keep
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={() => {
                          clearErrors()
                          setConfirmingId(candidate.id)
                        }}
                        aria-label={`Delete ${candidate.name}`}
                      >
                        Delete
                      </button>
                    )}
                  </span>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
