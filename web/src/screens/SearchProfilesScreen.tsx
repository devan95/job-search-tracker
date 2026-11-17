import { useState, type FormEvent, type ReactNode } from 'react'
import {
  createSearchProfile,
  deleteSearchProfile,
  listCandidates,
  listCompanyLists,
  listSearchProfiles,
  selectCompanyList,
  unselectCompanyList,
  updateSearchProfile,
  type ApiError,
  type Candidate,
  type CompanyListSummary,
  type SearchProfile,
  type WorkMode,
} from '../api'
import { ProblemAlert } from '../components/ProblemAlert'
import { WorkModeField } from '../components/WorkModeFields'
import { useAction, useLoad } from '../hooks'
import { fieldMessages } from '../problems'
import { validateName } from '../validation'
import { describeDefaultModes, describeProfileModes } from '../workModes'

interface ProfileValues {
  candidateId: string
  name: string
  companyListIds: string[]
  /** null = inherit; [] = explicit empty set. */
  workModes: WorkMode[] | null
  companyMonitoringEnabled: boolean
  broadSearchEnabled: boolean
}

const NEW_PROFILE: ProfileValues = {
  candidateId: '',
  name: '',
  companyListIds: [],
  workModes: null,
  companyMonitoringEnabled: false,
  broadSearchEnabled: false,
}

interface FormProps {
  title: string
  initial: ProfileValues
  submitLabel: string
  lists: CompanyListSummary[]
  /** Create mode: the candidate can be chosen. */
  candidates?: Candidate[]
  /** Edit mode: the candidate is fixed and only shown. */
  fixedCandidateName?: string
  busy: boolean
  error: ApiError | null
  clearOnSuccess?: boolean
  onSubmit: (values: ProfileValues) => Promise<boolean>
  onCancel?: () => void
}

function ProfileForm({
  title,
  initial,
  submitLabel,
  lists,
  candidates,
  fixedCandidateName,
  busy,
  error,
  clearOnSuccess,
  onSubmit,
  onCancel,
}: FormProps) {
  const [candidateId, setCandidateId] = useState(initial.candidateId)
  const [name, setName] = useState(initial.name)
  const [listIds, setListIds] = useState(initial.companyListIds)
  const [workModes, setWorkModes] = useState<WorkMode[] | null>(initial.workModes)
  const [monitoring, setMonitoring] = useState(initial.companyMonitoringEnabled)
  const [broad, setBroad] = useState(initial.broadSearchEnabled)
  const [nameError, setNameError] = useState<string | null>(null)
  const [candidateError, setCandidateError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const nameProblem = validateName(name)
    const candidateProblem =
      fixedCandidateName === undefined && candidateId === '' ? 'Choose a candidate.' : null
    setNameError(nameProblem)
    setCandidateError(candidateProblem)
    if (nameProblem || candidateProblem) return
    const ok = await onSubmit({
      candidateId,
      name: name.trim(),
      companyListIds: listIds,
      workModes,
      companyMonitoringEnabled: monitoring,
      broadSearchEnabled: broad,
    })
    if (ok && clearOnSuccess) {
      setCandidateId(initial.candidateId)
      setName(initial.name)
      setListIds(initial.companyListIds)
      setWorkModes(initial.workModes)
      setMonitoring(initial.companyMonitoringEnabled)
      setBroad(initial.broadSearchEnabled)
    }
  }

  return (
    <form aria-label={title} onSubmit={(e) => void submit(e)} noValidate className="stack">
      <h3>{title}</h3>

      {fixedCandidateName === undefined ? (
        <>
          <label>
            Candidate
            <select
              value={candidateId}
              onChange={(e) => setCandidateId(e.target.value)}
              aria-invalid={candidateError !== null || fieldMessages(error, 'candidateId').length > 0}
              disabled={busy}
            >
              <option value="">Choose a candidate…</option>
              {(candidates ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          {candidateError && <p className="field-error">{candidateError}</p>}
        </>
      ) : (
        <p>Candidate: {fixedCandidateName}</p>
      )}

      <label>
        Profile name
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-invalid={nameError !== null || fieldMessages(error, 'name').length > 0}
          disabled={busy}
        />
      </label>
      {nameError && <p className="field-error">{nameError}</p>}

      <fieldset disabled={busy} aria-invalid={fieldMessages(error, 'companyListIds').length > 0 || undefined}>
        <legend>Company lists</legend>
        {lists.length === 0 && (
          <p className="hint">No company lists yet. Create one in Company lists, or save the profile without any.</p>
        )}
        {lists.map((list) => (
          <label key={list.id} className="inline">
            <input
              type="checkbox"
              checked={listIds.includes(list.id)}
              onChange={(e) =>
                setListIds((prev) =>
                  e.target.checked ? [...prev, list.id] : prev.filter((x) => x !== list.id),
                )
              }
            />{' '}
            {list.name}
          </label>
        ))}
      </fieldset>

      <WorkModeField
        value={workModes}
        onChange={setWorkModes}
        disabled={busy}
        errors={fieldMessages(error, 'workModes')}
      />

      <fieldset disabled={busy}>
        <legend>Discovery</legend>
        <label className="inline">
          <input
            type="checkbox"
            checked={monitoring}
            onChange={(e) => setMonitoring(e.target.checked)}
          />{' '}
          Saved-company monitoring
        </label>
        <label className="inline">
          <input type="checkbox" checked={broad} onChange={(e) => setBroad(e.target.checked)} />{' '}
          Broad search
        </label>
        {!monitoring && !broad && (
          <p className="hint">Both are off: the profile is kept as a draft.</p>
        )}
      </fieldset>

      <div className="buttons">
        <button type="submit" disabled={busy}>
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}

interface LoadedProps {
  candidates: Candidate[]
  lists: CompanyListSummary[]
  profiles: SearchProfile[]
  reload: () => void
}

function ProfilesLoaded({ candidates, lists, profiles, reload }: LoadedProps) {
  const createAction = useAction()
  const rowAction = useAction()
  const [filter, setFilter] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingId, setConfirmingId] = useState<string | null>(null)

  const candidateName = (id: string) =>
    candidates.find((c) => c.id === id)?.name ?? 'Unknown candidate'
  const visible = filter ? profiles.filter((p) => p.candidateId === filter) : profiles

  async function onCreate(values: ProfileValues): Promise<boolean> {
    const result = await createAction.run(() => createSearchProfile(values))
    if (result.ok) reload()
    return result.ok
  }

  async function onSave(profile: SearchProfile, values: ProfileValues): Promise<boolean> {
    const result = await rowAction.run(async () => {
      await updateSearchProfile(profile.id, {
        name: values.name,
        workModes: values.workModes,
        companyMonitoringEnabled: values.companyMonitoringEnabled,
        broadSearchEnabled: values.broadSearchEnabled,
      })
      const before = profile.companyLists.map((l) => l.id)
      for (const id of values.companyListIds.filter((x) => !before.includes(x))) {
        await selectCompanyList(profile.id, id)
      }
      for (const id of before.filter((x) => !values.companyListIds.includes(x))) {
        await unselectCompanyList(profile.id, id)
      }
    })
    // Some steps may have applied even when a later one failed: show the server's state.
    reload()
    if (result.ok) setEditingId(null)
    return result.ok
  }

  async function onDelete(profile: SearchProfile) {
    const result = await rowAction.run(() => deleteSearchProfile(profile.id))
    setConfirmingId(null)
    if (result.ok) reload()
  }

  return (
    <>
      {candidates.length === 0 ? (
        <p>Create a candidate first (Candidates), then add search profiles for them.</p>
      ) : (
        <>
          <ProfileForm
            title="New search profile"
            initial={NEW_PROFILE}
            submitLabel="Create profile"
            lists={lists}
            candidates={candidates}
            busy={createAction.busy}
            error={createAction.error}
            clearOnSuccess
            onSubmit={onCreate}
          />
          {createAction.error && (
            <ProblemAlert error={createAction.error} subject="search profile for this candidate" onReload={reload} />
          )}
        </>
      )}

      {rowAction.error && (
        <ProblemAlert error={rowAction.error} subject="search profile for this candidate" onReload={reload} />
      )}

      <h3>Saved profiles</h3>
      {candidates.length > 0 && (
        <label>
          Show profiles for
          <select value={filter} onChange={(e) => setFilter(e.target.value)}>
            <option value="">All candidates</option>
            {candidates.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      )}

      {visible.length === 0 ? (
        <p>No search profiles yet.</p>
      ) : (
        <ul className="rows cards" aria-label="Search profiles">
          {visible.map((profile) => (
            <li key={profile.id}>
              {editingId === profile.id ? (
                <ProfileForm
                  // Remount when the server's copy changes, so after a partly applied save the
                  // form shows what the server now holds instead of stale local edits.
                  key={JSON.stringify(profile)}
                  title="Edit search profile"
                  initial={{
                    candidateId: profile.candidateId,
                    name: profile.name,
                    companyListIds: profile.companyLists.map((l) => l.id),
                    workModes: profile.workModes,
                    companyMonitoringEnabled: profile.companyMonitoringEnabled,
                    broadSearchEnabled: profile.broadSearchEnabled,
                  }}
                  submitLabel="Save profile"
                  lists={lists}
                  fixedCandidateName={candidateName(profile.candidateId)}
                  busy={rowAction.busy}
                  error={rowAction.error}
                  onSubmit={(values) => onSave(profile, values)}
                  onCancel={() => setEditingId(null)}
                />
              ) : (
                <>
                  <p className="row-name">
                    <strong>{profile.name}</strong> for {candidateName(profile.candidateId)}
                  </p>
                  <dl className="facts">
                    <dt>Company lists</dt>
                    <dd>
                      {profile.companyLists.length === 0
                        ? 'None selected'
                        : profile.companyLists.map((l) => l.name).join(', ')}
                    </dd>
                    <dt>Work modes</dt>
                    <dd>
                      {describeProfileModes(profile.workModes)}
                      {profile.workModes === null && profile.companyLists.length > 0 && (
                        <span className="hint block">
                          {profile.companyLists
                            .map((l) => `${l.name}: ${describeDefaultModes(l.defaultWorkModes)}`)
                            .join('; ')}
                        </span>
                      )}
                    </dd>
                    <dt>Saved-company monitoring</dt>
                    <dd>{profile.companyMonitoringEnabled ? 'On' : 'Off'}</dd>
                    <dt>Broad search</dt>
                    <dd>{profile.broadSearchEnabled ? 'On' : 'Off'}</dd>
                  </dl>
                  <div className="buttons">
                    <button
                      type="button"
                      onClick={() => {
                        rowAction.clearError()
                        setConfirmingId(null)
                        setEditingId(profile.id)
                      }}
                      aria-label={`Edit ${profile.name}`}
                    >
                      Edit
                    </button>
                    {confirmingId === profile.id ? (
                      <>
                        <button
                          type="button"
                          onClick={() => void onDelete(profile)}
                          disabled={rowAction.busy}
                          aria-label={`Confirm delete ${profile.name}`}
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
                          rowAction.clearError()
                          setConfirmingId(profile.id)
                        }}
                        aria-label={`Delete ${profile.name}`}
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
    </>
  )
}

export default function SearchProfilesScreen() {
  const candidates = useLoad(listCandidates)
  const lists = useLoad(listCompanyLists)
  const profiles = useLoad(listSearchProfiles)

  const failed = [candidates, lists, profiles].find((l) => l.state.kind === 'error')
  const reloadAll = () => {
    candidates.reload()
    lists.reload()
    profiles.reload()
  }

  let body: ReactNode
  if (failed && failed.state.kind === 'error') {
    body = <ProblemAlert error={failed.state.error} onReload={reloadAll} />
  } else if (
    candidates.state.kind === 'ready' &&
    lists.state.kind === 'ready' &&
    profiles.state.kind === 'ready'
  ) {
    body = (
      <ProfilesLoaded
        candidates={candidates.state.data}
        lists={lists.state.data}
        profiles={profiles.state.data}
        reload={profiles.reload}
      />
    )
  } else {
    body = <p role="status">Loading search profiles…</p>
  }

  return (
    <section aria-labelledby="profiles-heading">
      <h2 id="profiles-heading">Search profiles</h2>
      <p className="hint">
        A search profile ties a candidate to company lists and decides which kinds of discovery are on.
      </p>
      {body}
    </section>
  )
}
