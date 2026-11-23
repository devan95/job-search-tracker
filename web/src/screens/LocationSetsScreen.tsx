import { useId, useState, type FormEvent } from 'react'
import {
  addLocation,
  createLocationSet,
  deleteLocation,
  deleteLocationSet,
  getLocationSet,
  listLocationSets,
  renameLocationSet,
  updateLocation,
  type ApiError,
  type Location,
  type LocationInput,
  type LocationScope,
} from '../api'
import { errorText, FieldError } from '../components/FieldError'
import { NameForm } from '../components/NameForm'
import { ProblemAlert } from '../components/ProblemAlert'
import { useAction, useLoad } from '../hooks'
import {
  describeLocation,
  LOCATION_SCOPES,
  SCOPE_LABEL,
  toLocationInput,
  validateLocation,
} from '../locations'
import { fieldMessages } from '../problems'

interface LocationFormProps {
  title: string
  initial: LocationInput
  submitLabel: string
  busy: boolean
  error: ApiError | null
  clearOnSuccess?: boolean
  onSubmit: (input: LocationInput) => Promise<boolean>
  onCancel?: () => void
}

/** One location: a free-text label, what it covers (scope) and an optional country code. */
function LocationForm({
  title,
  initial,
  submitLabel,
  busy,
  error,
  clearOnSuccess,
  onSubmit,
  onCancel,
}: LocationFormProps) {
  const [label, setLabel] = useState(initial.label)
  const [scope, setScope] = useState<LocationScope>(initial.scope)
  const [countryCode, setCountryCode] = useState(initial.countryCode ?? '')
  const [labelError, setLabelError] = useState<string | null>(null)
  const [codeError, setCodeError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const problems = validateLocation(label, scope, countryCode)
    setLabelError(problems.label)
    setCodeError(problems.countryCode)
    if (problems.label || problems.countryCode) return
    const ok = await onSubmit(toLocationInput(label, scope, countryCode))
    if (ok && clearOnSuccess) {
      setLabel('')
      setScope(initial.scope)
      setCountryCode(initial.countryCode ?? '')
    }
  }

  const ids = useId()
  const labelErrorId = `${ids}-label-error`
  const scopeErrorId = `${ids}-scope-error`
  const codeErrorId = `${ids}-code-error`
  const labelMessage = errorText(labelError, fieldMessages(error, 'label'))
  const scopeMessage = errorText(null, fieldMessages(error, 'scope'))
  const codeMessage = errorText(codeError, fieldMessages(error, 'countryCode'))

  return (
    <form aria-label={title} onSubmit={(e) => void submit(e)} noValidate className="stack">
      <h4>{title}</h4>
      <label>
        Label
        <input
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          aria-invalid={labelMessage !== null}
          aria-describedby={labelMessage !== null ? labelErrorId : undefined}
          disabled={busy}
        />
      </label>
      <FieldError id={labelErrorId} message={labelMessage} />
      <label>
        Covers
        <select
          value={scope}
          onChange={(e) => {
            const next = e.target.value as LocationScope
            setScope(next)
            // Remote worldwide has no country; do not keep a code the field no longer shows.
            if (next === 'remoteWorldwide') {
              setCountryCode('')
              setCodeError(null)
            }
          }}
          aria-invalid={scopeMessage !== null}
          aria-describedby={scopeMessage !== null ? scopeErrorId : undefined}
          disabled={busy}
        >
          {LOCATION_SCOPES.map((s) => (
            <option key={s} value={s}>
              {SCOPE_LABEL[s]}
            </option>
          ))}
        </select>
      </label>
      <FieldError id={scopeErrorId} message={scopeMessage} />
      {scope === 'remoteWorldwide' ? (
        <p className="hint">Remote worldwide has no country. It is not the same as remote within one country.</p>
      ) : (
        <>
          <label>
            {scope === 'remoteCountry' ? 'Country code' : 'Country code (optional)'}
            <input
              value={countryCode}
              maxLength={2}
              onChange={(e) => setCountryCode(e.target.value)}
              aria-invalid={codeMessage !== null}
              aria-describedby={codeMessage !== null ? codeErrorId : undefined}
              disabled={busy}
            />
          </label>
          <FieldError id={codeErrorId} message={codeMessage} />
        </>
      )}
      <p className="hint">
        This says where, not how the job is worked. Remote, on-site and hybrid are work modes, set on the search profile.
      </p>
      <div className="buttons">
        <button type="submit" disabled={busy}>
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}

const NEW_LOCATION: LocationInput = { label: '', scope: 'city', countryCode: null }

interface DetailProps {
  setId: string
  onChanged: () => void
  onDeleted: () => void
}

function LocationSetDetail({ setId, onChanged, onDeleted }: DetailProps) {
  const load = useLoad(() => getLocationSet(setId))
  const action = useAction()
  const [notice, setNotice] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingId, setConfirmingId] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (load.state.kind === 'loading') return <p role="status">Loading location set…</p>
  if (load.state.kind === 'error') {
    return <ProblemAlert error={load.state.error} subject="location set" onReload={load.reload} />
  }
  const detail = load.state.data

  function changed() {
    void load.reload()
    onChanged()
  }

  async function onRename(name: string): Promise<boolean> {
    setNotice(null)
    const result = await action.run(() => renameLocationSet(setId, name))
    if (result.ok) {
      setNotice('Location set renamed.')
      changed()
    }
    return result.ok
  }

  async function onAdd(input: LocationInput): Promise<boolean> {
    setNotice(null)
    const result = await action.run(() => addLocation(setId, input))
    if (result.ok) {
      setNotice(`Added ${result.value.label}.`)
      changed()
    }
    return result.ok
  }

  async function onSaveLocation(location: Location, input: LocationInput): Promise<boolean> {
    setNotice(null)
    const result = await action.run(() => updateLocation(setId, location.id, input))
    if (result.ok) {
      setEditingId(null)
      setNotice(`Saved ${result.value.label}.`)
      changed()
    }
    return result.ok
  }

  async function onDeleteLocation(location: Location) {
    setNotice(null)
    const result = await action.run(() => deleteLocation(setId, location.id))
    setConfirmingId(null)
    if (result.ok) {
      setEditingId((current) => (current === location.id ? null : current))
      changed()
    }
  }

  async function onDeleteSet() {
    const result = await action.run(() => deleteLocationSet(setId))
    setConfirmingDelete(false)
    if (result.ok) onDeleted()
  }

  return (
    <div className="detail" aria-label={`Location set ${detail.name}`} role="region">
      <h3>{detail.name}</h3>

      {action.error && <ProblemAlert error={action.error} subject="location set" onReload={load.reload} />}
      {notice && <p role="status">{notice}</p>}

      <NameForm
        key={`${detail.id}:${detail.name}`}
        title="Rename location set"
        nameLabel="Location set name"
        initialName={detail.name}
        submitLabel="Save name"
        busy={action.busy}
        error={action.error}
        onSubmit={onRename}
      />

      <h4>Locations</h4>
      {detail.locations.length === 0 ? (
        <p>No locations in this set yet.</p>
      ) : (
        <ul className="rows cards" aria-label="Locations in this set">
          {detail.locations.map((location) => (
            <li key={location.id}>
              {editingId === location.id ? (
                <LocationForm
                  key={JSON.stringify(location)}
                  title="Edit location"
                  initial={{
                    label: location.label,
                    scope: location.scope,
                    countryCode: location.countryCode,
                  }}
                  submitLabel="Save location"
                  busy={action.busy}
                  error={action.error}
                  onSubmit={(input) => onSaveLocation(location, input)}
                  onCancel={() => setEditingId(null)}
                />
              ) : (
                <>
                  <p className="row-name">{describeLocation(location)}</p>
                  <div className="buttons">
                    <button
                      type="button"
                      onClick={() => {
                        action.clearError()
                        setConfirmingId(null)
                        setEditingId(location.id)
                      }}
                      disabled={action.busy}
                      aria-label={`Edit ${location.label}`}
                    >
                      Edit
                    </button>
                    {confirmingId === location.id ? (
                      <>
                        <button
                          type="button"
                          onClick={() => void onDeleteLocation(location)}
                          disabled={action.busy}
                          aria-label={`Confirm delete ${location.label}`}
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
                          setConfirmingId(location.id)
                        }}
                        disabled={action.busy}
                        aria-label={`Delete ${location.label}`}
                      >
                        Delete
                      </button>
                    )}
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      <LocationForm
        title="Add location"
        initial={NEW_LOCATION}
        submitLabel="Add location"
        busy={action.busy}
        error={editingId === null ? action.error : null}
        clearOnSuccess
        onSubmit={onAdd}
      />

      <h4>Delete location set</h4>
      <p className="hint">
        Deleting a location set removes its locations. A set that a search profile selects cannot be deleted.
      </p>
      {confirmingDelete ? (
        <div className="buttons">
          <button type="button" onClick={() => void onDeleteSet()} disabled={action.busy}>
            Yes, delete this location set
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
          Delete location set
        </button>
      )}
    </div>
  )
}

export default function LocationSetsScreen() {
  const { state, reload } = useLoad(listLocationSets)
  const createAction = useAction()
  const [selectedId, setSelectedId] = useState<string | null>(null)

  async function onCreate(name: string): Promise<boolean> {
    const result = await createAction.run(() => createLocationSet(name))
    if (result.ok) {
      setSelectedId(result.value.id)
      void reload()
    }
    return result.ok
  }

  return (
    <section aria-labelledby="location-sets-heading">
      <h2 id="location-sets-heading">Location sets</h2>
      <p className="hint">
        A location set is a named group of places: cities, states, countries, remote within one country, or remote
        worldwide. Search profiles select the sets they use.
      </p>

      <NameForm
        title="New location set"
        nameLabel="Location set name"
        initialName=""
        submitLabel="Create location set"
        busy={createAction.busy}
        error={createAction.error}
        clearOnSuccess
        onSubmit={onCreate}
      />
      {createAction.error && <ProblemAlert error={createAction.error} subject="location set" onReload={reload} />}

      {state.kind === 'loading' && <p role="status">Loading location sets…</p>}
      {state.kind === 'error' && <ProblemAlert error={state.error} onReload={reload} />}
      {state.kind === 'ready' && state.data.length === 0 && <p>No location sets yet. Create one above.</p>}
      {state.kind === 'ready' && state.data.length > 0 && (
        <ul className="rows" aria-label="Location sets">
          {state.data.map((set) => (
            <li key={set.id}>
              <button
                type="button"
                className="row-button"
                aria-current={selectedId === set.id ? 'true' : undefined}
                onClick={() => setSelectedId(set.id)}
              >
                <span className="row-name">{set.name}</span>
                <span className="meta">
                  {set.locationCount === 1 ? '1 location' : `${set.locationCount} locations`}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {selectedId && (
        <LocationSetDetail
          key={selectedId}
          setId={selectedId}
          onChanged={reload}
          onDeleted={() => {
            // Only clear the selection if it is still the set that was deleted.
            const deletedId = selectedId
            setSelectedId((current) => (current === deletedId ? null : current))
            void reload()
          }}
        />
      )}
    </section>
  )
}
