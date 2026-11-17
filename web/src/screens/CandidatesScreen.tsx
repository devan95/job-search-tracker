import { useState, type FormEvent } from 'react'
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
  const action = useAction()
  const [name, setName] = useState('')
  const [nameError, setNameError] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editError, setEditError] = useState<string | null>(null)
  const [confirmingId, setConfirmingId] = useState<string | null>(null)

  async function onAdd(event: FormEvent) {
    event.preventDefault()
    const problem = validateName(name)
    setNameError(problem)
    if (problem) return
    const result = await action.run(() => createCandidate(name.trim()))
    if (result.ok) {
      setName('')
      reload()
    }
  }

  function startEdit(candidate: Candidate) {
    action.clearError()
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
    const result = await action.run(() => updateCandidate(candidate.id, editName.trim()))
    if (result.ok) {
      setEditingId(null)
      reload()
    }
  }

  async function onDelete(candidate: Candidate) {
    const result = await action.run(() => deleteCandidate(candidate.id))
    // A conflict or error is shown above the list; the row stays as it is.
    setConfirmingId(null)
    if (result.ok) reload()
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
            aria-invalid={nameError !== null || fieldMessages(action.error, 'name').length > 0}
            disabled={action.busy}
          />
        </label>
        {nameError && <p className="field-error">{nameError}</p>}
        <button type="submit" disabled={action.busy}>
          Add candidate
        </button>
      </form>

      {action.error && <ProblemAlert error={action.error} subject="candidate" onReload={reload} />}

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
                      aria-invalid={editError !== null}
                      disabled={action.busy}
                    />
                  </label>
                  {editError && <p className="field-error">{editError}</p>}
                  <div className="buttons">
                    <button type="submit" disabled={action.busy}>
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
                          disabled={action.busy}
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
                          action.clearError()
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
