import { useId, useState, type FormEvent } from 'react'
import {
  addRoleTitle,
  createRoleList,
  deleteRoleList,
  deleteRoleTitle,
  getRoleList,
  listRoleLists,
  renameRoleList,
  updateRoleTitle,
  type ApiError,
  type RoleTitle,
  type RoleTitleInput,
} from '../api'
import { errorText, FieldError } from '../components/FieldError'
import { NameForm } from '../components/NameForm'
import { ProblemAlert } from '../components/ProblemAlert'
import { useAction, useLoad } from '../hooks'
import { fieldMessages } from '../problems'
import { parseAliases, validateTitle } from '../roles'

interface TitleFormProps {
  title: string
  initial: RoleTitleInput
  submitLabel: string
  busy: boolean
  error: ApiError | null
  clearOnSuccess?: boolean
  onSubmit: (input: RoleTitleInput) => Promise<boolean>
  onCancel?: () => void
}

/** A primary title and its equivalent titles (aliases), one alias per line. */
function TitleForm({
  title: formTitle,
  initial,
  submitLabel,
  busy,
  error,
  clearOnSuccess,
  onSubmit,
  onCancel,
}: TitleFormProps) {
  const [title, setTitle] = useState(initial.title)
  const [aliasText, setAliasText] = useState(initial.aliases.join('\n'))
  const [titleError, setTitleError] = useState<string | null>(null)
  const [aliasError, setAliasError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const aliases = parseAliases(aliasText)
    const problems = validateTitle(title, aliases)
    setTitleError(problems.title)
    setAliasError(problems.aliases)
    if (problems.title || problems.aliases) return
    const ok = await onSubmit({ title: title.trim(), aliases })
    if (ok && clearOnSuccess) {
      setTitle('')
      setAliasText('')
    }
  }

  const ids = useId()
  const titleErrorId = `${ids}-title-error`
  const aliasErrorId = `${ids}-aliases-error`
  const titleMessage = errorText(titleError, fieldMessages(error, 'title'))
  const aliasMessage = errorText(aliasError, fieldMessages(error, 'aliases'))

  return (
    <form aria-label={formTitle} onSubmit={(e) => void submit(e)} noValidate className="stack">
      <h4>{formTitle}</h4>
      <label>
        Primary title
        <input
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          aria-invalid={titleMessage !== null}
          aria-describedby={titleMessage !== null ? titleErrorId : undefined}
          disabled={busy}
        />
      </label>
      <FieldError id={titleErrorId} message={titleMessage} />
      <label>
        Equivalent titles (aliases), one per line
        <textarea
          rows={3}
          value={aliasText}
          onChange={(e) => setAliasText(e.target.value)}
          aria-invalid={aliasMessage !== null}
          aria-describedby={aliasMessage !== null ? aliasErrorId : undefined}
          disabled={busy}
        />
      </label>
      <FieldError id={aliasErrorId} message={aliasMessage} />
      <p className="hint">
        Aliases are other names for the same role. They are not separate employers, and a title or alias can appear only
        once in a list.
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

interface DetailProps {
  listId: string
  onChanged: () => void
  onDeleted: () => void
}

function RoleListDetail({ listId, onChanged, onDeleted }: DetailProps) {
  const load = useLoad(() => getRoleList(listId))
  const action = useAction()
  const [notice, setNotice] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingTitleId, setConfirmingTitleId] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  // Which title form sent the last request, so its field errors reach that form only.
  const [submitter, setSubmitter] = useState<'add' | 'edit' | null>(null)

  if (load.state.kind === 'loading') return <p role="status">Loading role list…</p>
  if (load.state.kind === 'error') {
    return <ProblemAlert error={load.state.error} subject="role list" onReload={load.reload} />
  }
  const detail = load.state.data

  function changed() {
    void load.reload()
    onChanged()
  }

  async function onRename(name: string): Promise<boolean> {
    setNotice(null)
    const result = await action.run(() => renameRoleList(listId, name))
    if (result.ok) {
      setNotice('Role list renamed.')
      changed()
    }
    return result.ok
  }

  async function onAdd(input: RoleTitleInput): Promise<boolean> {
    setNotice(null)
    setSubmitter('add')
    const result = await action.run(() => addRoleTitle(listId, input))
    if (result.ok) {
      setNotice(`Added ${result.value.title}.`)
      changed()
    }
    return result.ok
  }

  async function onSaveTitle(title: RoleTitle, input: RoleTitleInput): Promise<boolean> {
    setNotice(null)
    setSubmitter('edit')
    const result = await action.run(() => updateRoleTitle(listId, title.id, input))
    if (result.ok) {
      setEditingId(null)
      setNotice(`Saved ${result.value.title}.`)
      changed()
    }
    return result.ok
  }

  async function onDeleteTitle(title: RoleTitle) {
    setNotice(null)
    const result = await action.run(() => deleteRoleTitle(listId, title.id))
    setConfirmingTitleId(null)
    if (result.ok) {
      setEditingId((current) => (current === title.id ? null : current))
      changed()
    }
  }

  async function onDeleteList() {
    const result = await action.run(() => deleteRoleList(listId))
    setConfirmingDelete(false)
    if (result.ok) onDeleted()
  }

  return (
    <div className="detail" aria-label={`Role list ${detail.name}`} role="region">
      <h3>{detail.name}</h3>

      {action.error && <ProblemAlert error={action.error} subject="role list" onReload={load.reload} />}
      {notice && <p role="status">{notice}</p>}

      <NameForm
        key={`${detail.id}:${detail.name}`}
        title="Rename role list"
        nameLabel="Role list name"
        initialName={detail.name}
        submitLabel="Save name"
        busy={action.busy}
        error={action.error}
        onSubmit={onRename}
      />

      <h4>Titles</h4>
      {detail.titles.length === 0 ? (
        <p>No titles in this role list yet.</p>
      ) : (
        <ul className="rows cards" aria-label="Titles in this role list">
          {detail.titles.map((title) => (
            <li key={title.id}>
              {editingId === title.id ? (
                <TitleForm
                  key={JSON.stringify(title)}
                  title="Edit title"
                  initial={{ title: title.title, aliases: title.aliases.map((a) => a.alias) }}
                  submitLabel="Save title"
                  busy={action.busy}
                  error={submitter === 'edit' ? action.error : null}
                  onSubmit={(input) => onSaveTitle(title, input)}
                  onCancel={() => setEditingId(null)}
                />
              ) : (
                <>
                  <p className="row-name">{title.title}</p>
                  <p className="meta">
                    {title.aliases.length === 0
                      ? 'No equivalent titles'
                      : `Also known as: ${title.aliases.map((a) => a.alias).join(', ')}`}
                  </p>
                  <div className="buttons">
                    <button
                      type="button"
                      onClick={() => {
                        action.clearError()
                        setConfirmingTitleId(null)
                        setEditingId(title.id)
                      }}
                      disabled={action.busy}
                      aria-label={`Edit ${title.title}`}
                    >
                      Edit
                    </button>
                    {confirmingTitleId === title.id ? (
                      <>
                        <button
                          type="button"
                          onClick={() => void onDeleteTitle(title)}
                          disabled={action.busy}
                          aria-label={`Confirm delete ${title.title}`}
                        >
                          Yes, delete
                        </button>
                        <button type="button" onClick={() => setConfirmingTitleId(null)}>
                          Keep
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={() => {
                          action.clearError()
                          setConfirmingTitleId(title.id)
                        }}
                        disabled={action.busy}
                        aria-label={`Delete ${title.title}`}
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

      <TitleForm
        title="Add title"
        initial={{ title: '', aliases: [] }}
        submitLabel="Add title"
        busy={action.busy}
        error={submitter === 'add' ? action.error : null}
        clearOnSuccess
        onSubmit={onAdd}
      />

      <h4>Delete role list</h4>
      <p className="hint">
        Deleting a role list removes its titles and aliases. A list that a search profile selects cannot be deleted.
      </p>
      {confirmingDelete ? (
        <div className="buttons">
          <button type="button" onClick={() => void onDeleteList()} disabled={action.busy}>
            Yes, delete this role list
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
          Delete role list
        </button>
      )}
    </div>
  )
}

export default function RoleListsScreen() {
  const { state, reload } = useLoad(listRoleLists)
  const createAction = useAction()
  const [selectedId, setSelectedId] = useState<string | null>(null)

  async function onCreate(name: string): Promise<boolean> {
    const result = await createAction.run(() => createRoleList(name))
    if (result.ok) {
      setSelectedId(result.value.id)
      void reload()
    }
    return result.ok
  }

  return (
    <section aria-labelledby="role-lists-heading">
      <h2 id="role-lists-heading">Role lists</h2>
      <p className="hint">
        A role list is a named set of target job titles, each with optional equivalent titles. Search profiles select the
        lists they use.
      </p>

      <NameForm
        title="New role list"
        nameLabel="Role list name"
        initialName=""
        submitLabel="Create role list"
        busy={createAction.busy}
        error={createAction.error}
        clearOnSuccess
        onSubmit={onCreate}
      />
      {createAction.error && <ProblemAlert error={createAction.error} subject="role list" onReload={reload} />}

      {state.kind === 'loading' && <p role="status">Loading role lists…</p>}
      {state.kind === 'error' && <ProblemAlert error={state.error} onReload={reload} />}
      {state.kind === 'ready' && state.data.length === 0 && <p>No role lists yet. Create one above.</p>}
      {state.kind === 'ready' && state.data.length > 0 && (
        <ul className="rows" aria-label="Role lists">
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
                  {list.titleCount === 1 ? '1 title' : `${list.titleCount} titles`} ·{' '}
                  {list.aliasCount === 1 ? '1 alias' : `${list.aliasCount} aliases`}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {selectedId && (
        <RoleListDetail
          key={selectedId}
          listId={selectedId}
          onChanged={reload}
          onDeleted={() => {
            // Only clear the selection if it is still the list that was deleted.
            const deletedId = selectedId
            setSelectedId((current) => (current === deletedId ? null : current))
            void reload()
          }}
        />
      )}
    </section>
  )
}
