import { useState, type FormEvent } from 'react'
import {
  addEmployerToList,
  createCompanyList,
  deleteCompanyList,
  getCompanyList,
  listCompanyLists,
  removeEmployerFromList,
  resolveEmployer,
  updateCompanyList,
  type ApiError,
  type CompanyListInput,
  type Employer,
  type WorkMode,
} from '../api'
import { CompanyImport } from '../components/CompanyImport'
import { ProblemAlert } from '../components/ProblemAlert'
import { WorkModeChecks } from '../components/WorkModeFields'
import { useAction, useLoad } from '../hooks'
import { fieldMessages } from '../problems'
import { validateName } from '../validation'
import { describeDefaultModes } from '../workModes'

const EMPTY_LIST: CompanyListInput = { name: '', locationLabel: null, defaultWorkModes: [] }

interface ListFormProps {
  title: string
  initial: CompanyListInput
  submitLabel: string
  busy: boolean
  error: ApiError | null
  clearOnSuccess?: boolean
  onSubmit: (input: CompanyListInput) => Promise<boolean>
}

function ListForm({
  title,
  initial,
  submitLabel,
  busy,
  error,
  clearOnSuccess,
  onSubmit,
}: ListFormProps) {
  const [name, setName] = useState(initial.name)
  const [location, setLocation] = useState(initial.locationLabel ?? '')
  const [modes, setModes] = useState<WorkMode[]>(initial.defaultWorkModes)
  const [nameError, setNameError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const problem = validateName(name)
    setNameError(problem)
    if (problem) return
    const trimmedLocation = location.trim()
    const ok = await onSubmit({
      name: name.trim(),
      locationLabel: trimmedLocation === '' ? null : trimmedLocation,
      defaultWorkModes: modes,
    })
    if (ok && clearOnSuccess) {
      setName('')
      setLocation('')
      setModes([])
    }
  }

  return (
    <form aria-label={title} onSubmit={(e) => void submit(e)} noValidate className="stack">
      <h3>{title}</h3>
      <label>
        List name
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-invalid={nameError !== null || fieldMessages(error, 'name').length > 0}
          disabled={busy}
        />
      </label>
      {nameError && <p className="field-error">{nameError}</p>}
      <label>
        Location label (optional)
        <input
          value={location}
          onChange={(e) => setLocation(e.target.value)}
          disabled={busy}
        />
      </label>
      <WorkModeChecks
        legend="Default work modes"
        value={modes}
        onChange={setModes}
        disabled={busy}
        errors={fieldMessages(error, 'defaultWorkModes')}
      />
      <p className="hint">Nothing checked means the list states no default work mode.</p>
      <button type="submit" disabled={busy}>
        {submitLabel}
      </button>
    </form>
  )
}

interface DetailProps {
  listId: string
  onChanged: () => void
  onDeleted: () => void
}

function ListDetail({ listId, onChanged, onDeleted }: DetailProps) {
  const load = useLoad(() => getCompanyList(listId))
  const action = useAction()
  const [employerName, setEmployerName] = useState('')
  const [employerError, setEmployerError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (load.state.kind === 'loading') return <p role="status">Loading list…</p>
  if (load.state.kind === 'error') {
    return <ProblemAlert error={load.state.error} subject="company list" onReload={load.reload} />
  }
  const detail = load.state.data

  async function onSave(input: CompanyListInput): Promise<boolean> {
    setNotice(null)
    const result = await action.run(() => updateCompanyList(listId, input))
    if (result.ok) {
      setNotice('List saved.')
      load.reload()
      onChanged()
    }
    return result.ok
  }

  async function onAddEmployer(event: FormEvent) {
    event.preventDefault()
    setNotice(null)
    const problem = validateName(employerName)
    setEmployerError(problem)
    if (problem) return
    // Resolve first so an existing canonical company is reused, never duplicated.
    const result = await action.run(async () => {
      const resolved = await resolveEmployer(employerName.trim())
      await addEmployerToList(listId, resolved.employer.id)
      return resolved
    })
    if (!result.ok) return
    const { employer, created } = result.value
    if (detail.employers.some((e) => e.id === employer.id)) {
      setNotice(`${employer.name} was already in this list.`)
    } else if (created) {
      setNotice(`Added ${employer.name} as a new company.`)
    } else {
      setNotice(`Added ${employer.name}; it already existed as a company and was reused.`)
    }
    setEmployerName('')
    load.reload()
    onChanged()
  }

  async function onRemove(employer: Employer) {
    setNotice(null)
    const result = await action.run(() => removeEmployerFromList(listId, employer.id))
    if (result.ok) {
      load.reload()
      onChanged()
    }
  }

  async function onDeleteList() {
    const result = await action.run(() => deleteCompanyList(listId))
    setConfirmingDelete(false)
    if (result.ok) onDeleted()
  }

  return (
    <div className="detail" aria-label={`List ${detail.name}`} role="region">
      <h3>{detail.name}</h3>

      {action.error && (
        <ProblemAlert error={action.error} subject="company list" onReload={load.reload} />
      )}
      {notice && <p role="status">{notice}</p>}

      <ListForm
        key={detail.id}
        title="Edit list"
        initial={{
          name: detail.name,
          locationLabel: detail.locationLabel,
          defaultWorkModes: detail.defaultWorkModes,
        }}
        submitLabel="Save list"
        busy={action.busy}
        error={action.error}
        onSubmit={onSave}
      />

      <h4>Companies</h4>
      <form onSubmit={(e) => void onAddEmployer(e)} noValidate className="stack">
        <label>
          Company name
          <input
            value={employerName}
            onChange={(e) => setEmployerName(e.target.value)}
            aria-invalid={employerError !== null}
            disabled={action.busy}
          />
        </label>
        {employerError && <p className="field-error">{employerError}</p>}
        <button type="submit" disabled={action.busy}>
          Add company
        </button>
      </form>
      {detail.employers.length === 0 ? (
        <p>No companies in this list yet.</p>
      ) : (
        <ul className="rows" aria-label="Companies in this list">
          {detail.employers.map((employer) => (
            <li key={employer.id}>
              <span className="row-name">{employer.name}</span>
              <button
                type="button"
                onClick={() => void onRemove(employer)}
                disabled={action.busy}
                aria-label={`Remove ${employer.name} from this list`}
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      <CompanyImport
        listId={listId}
        onChanged={() => {
          load.reload()
          onChanged()
        }}
      />

      <h4>Delete list</h4>
      <p className="hint">Deleting a list keeps its companies. A list that a search profile selects cannot be deleted.</p>
      {confirmingDelete ? (
        <div className="buttons">
          <button type="button" onClick={() => void onDeleteList()} disabled={action.busy}>
            Yes, delete this list
          </button>
          <button type="button" onClick={() => setConfirmingDelete(false)}>
            Keep
          </button>
        </div>
      ) : (
        <button
          type="button"
          onClick={() => {
            action.clearError()
            setConfirmingDelete(true)
          }}
        >
          Delete list
        </button>
      )}
    </div>
  )
}

export default function CompanyListsScreen() {
  const { state, reload } = useLoad(listCompanyLists)
  const createAction = useAction()
  const [selectedId, setSelectedId] = useState<string | null>(null)

  async function onCreate(input: CompanyListInput): Promise<boolean> {
    const result = await createAction.run(() => createCompanyList(input))
    if (result.ok) {
      setSelectedId(result.value.id)
      reload()
    }
    return result.ok
  }

  return (
    <section aria-labelledby="lists-heading">
      <h2 id="lists-heading">Company lists</h2>
      <p className="hint">
        A company list groups companies to monitor, with an optional location label and default work modes.
      </p>

      <ListForm
        title="New company list"
        initial={EMPTY_LIST}
        submitLabel="Create list"
        busy={createAction.busy}
        error={createAction.error}
        clearOnSuccess
        onSubmit={onCreate}
      />
      {createAction.error && (
        <ProblemAlert error={createAction.error} subject="company list" onReload={reload} />
      )}

      {state.kind === 'loading' && <p role="status">Loading company lists…</p>}
      {state.kind === 'error' && <ProblemAlert error={state.error} onReload={reload} />}
      {state.kind === 'ready' && state.data.length === 0 && (
        <p>No company lists yet. Create one above.</p>
      )}
      {state.kind === 'ready' && state.data.length > 0 && (
        <ul className="rows" aria-label="Company lists">
          {state.data.map((list) => (
            <li key={list.id}>
              <button
                type="button"
                className="row-button"
                aria-current={selectedId === list.id ? 'true' : undefined}
                onClick={() => setSelectedId(list.id)}
              >
                <span className="row-name">{list.name}</span>
                <span className="meta">
                  {[
                    list.locationLabel,
                    list.employerCount === 1 ? '1 company' : `${list.employerCount} companies`,
                    describeDefaultModes(list.defaultWorkModes),
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {selectedId && (
        <ListDetail
          key={selectedId}
          listId={selectedId}
          onChanged={reload}
          onDeleted={() => {
            // Only clear the selection if it is still the list that was deleted.
            const deletedId = selectedId
            setSelectedId((current) => (current === deletedId ? null : current))
            reload()
          }}
        />
      )}
    </section>
  )
}
