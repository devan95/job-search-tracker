import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { SearchProfile } from '../api'
import { deferred, mockApi, problem, type MockReply } from '../test/mockApi'
import SearchProfilesScreen from './SearchProfilesScreen'

afterEach(() => vi.restoreAllMocks())

const asha = { id: 'c1', name: 'Asha Rao' }
const ravi = { id: 'c2', name: 'Ravi Shah' }

const listA = {
  id: 'l1',
  name: 'Ahmedabad product companies',
  locationLabel: 'Ahmedabad',
  defaultWorkModes: ['onSite', 'hybrid'],
  employerCount: 3,
}
const listB = {
  id: 'l2',
  name: 'Remote-first startups',
  locationLabel: null,
  defaultWorkModes: [],
  employerCount: 5,
}

const roleA = { id: 'r1', name: 'Backend titles', titleCount: 2, aliasCount: 3 }
const roleB = { id: 'r2', name: 'Data titles', titleCount: 1, aliasCount: 0 }
const placeA = { id: 's1', name: 'Gujarat cities', locationCount: 2 }
const placeB = { id: 's2', name: 'Remote scopes', locationCount: 2 }

const profile = (over: Partial<SearchProfile> = {}): SearchProfile => ({
  id: 'p1',
  candidateId: 'c1',
  name: 'Backend roles',
  workModes: null,
  companyMonitoringEnabled: true,
  broadSearchEnabled: false,
  profileVersion: 1,
  companyLists: [],
  roleLists: [],
  locationSets: [],
  ...over,
})

function baseRoutes(profiles: SearchProfile[] = []) {
  return {
    'GET /api/candidates': { status: 200, body: [asha, ravi] },
    'GET /api/company-lists': { status: 200, body: [listA, listB] },
    'GET /api/role-lists': { status: 200, body: [roleA, roleB] },
    'GET /api/location-sets': { status: 200, body: [placeA, placeB] },
    'GET /api/search-profiles': { status: 200, body: profiles },
  }
}

describe('SearchProfilesScreen', () => {
  it('asks for a candidate first when none exists', async () => {
    mockApi({
      'GET /api/candidates': { status: 200, body: [] },
      'GET /api/company-lists': { status: 200, body: [] },
      'GET /api/role-lists': { status: 200, body: [] },
      'GET /api/location-sets': { status: 200, body: [] },
      'GET /api/search-profiles': { status: 200, body: [] },
    })
    render(<SearchProfilesScreen />)
    expect(await screen.findByText(/Create a candidate first/)).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'New search profile' })).not.toBeInTheDocument()
  })

  it('creates a profile for a candidate with several lists, inheriting work modes', async () => {
    const calls = mockApi({
      ...baseRoutes(),
      'POST /api/search-profiles': {
        status: 201,
        body: profile({ companyLists: [] }),
      },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    await user.selectOptions(within(form).getByLabelText('Candidate'), 'c1')
    await user.type(within(form).getByLabelText('Profile name'), '  Backend roles ')
    await user.click(within(form).getByLabelText('Ahmedabad product companies'))
    await user.click(within(form).getByLabelText('Remote-first startups'))
    await user.click(within(form).getByLabelText('Saved-company monitoring'))
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      candidateId: 'c1',
      name: 'Backend roles',
      companyListIds: ['l1', 'l2'],
      roleListIds: [],
      locationSetIds: [],
      companyMonitoringEnabled: true,
      broadSearchEnabled: false,
    })
  })

  it('allows both discovery flags to be off and says the profile is a draft', async () => {
    const calls = mockApi({
      ...baseRoutes(),
      'POST /api/search-profiles': { status: 201, body: profile() },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    expect(within(form).getByText(/Both are off/)).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText('Candidate'), 'c1')
    await user.type(within(form).getByLabelText('Profile name'), 'Draft')
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      companyMonitoringEnabled: false,
      broadSearchEnabled: false,
    })
  })

  it('sends an explicit empty work-mode set as [] and keeps inherit apart', async () => {
    const calls = mockApi({
      ...baseRoutes(),
      'POST /api/search-profiles': { status: 201, body: profile({ workModes: [] }) },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    await user.selectOptions(within(form).getByLabelText('Candidate'), 'c1')
    await user.type(within(form).getByLabelText('Profile name'), 'No modes')
    await user.click(within(form).getByLabelText('Choose work modes for this profile'))
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({ workModes: [] })
  })

  it('requires a candidate and a name before calling the API', async () => {
    const calls = mockApi(baseRoutes())
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    expect(within(form).getByText('Choose a candidate.')).toBeInTheDocument()
    expect(within(form).getByText('A name is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('reports a duplicate profile name for the same candidate', async () => {
    mockApi({
      ...baseRoutes(),
      'POST /api/search-profiles': problem(409, 'duplicate_name'),
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    await user.selectOptions(within(form).getByLabelText('Candidate'), 'c1')
    await user.type(within(form).getByLabelText('Profile name'), 'Backend roles')
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Name already in use')
    expect(alert).toHaveTextContent('for this candidate')
  })

  it('shows inherit, explicit empty and explicit sets differently', async () => {
    mockApi(
      baseRoutes([
        profile({
          id: 'p1',
          name: 'Inheriting',
          workModes: null,
          companyLists: [{ id: 'l1', name: listA.name, defaultWorkModes: ['onSite', 'hybrid'] }],
        }),
        profile({ id: 'p2', name: 'Empty set', workModes: [] }),
        profile({ id: 'p3', name: 'Remote only', workModes: ['remote'] }),
      ]),
    )
    render(<SearchProfilesScreen />)

    const list = await screen.findByRole('list', { name: 'Search profiles' })
    const items = within(list).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent("Inherits each selected list's default")
    expect(items[0]).toHaveTextContent('Ahmedabad product companies: On-site, Hybrid')
    expect(items[1]).toHaveTextContent('Explicit empty set')
    expect(items[1]).not.toHaveTextContent('Inherits')
    expect(items[2]).toHaveTextContent('Remote (overrides list defaults)')
  })

  it('shows the discovery flags independently, including both off', async () => {
    mockApi(
      baseRoutes([
        profile({ id: 'p1', name: 'Monitoring only', companyMonitoringEnabled: true, broadSearchEnabled: false }),
        profile({ id: 'p2', name: 'Both off', companyMonitoringEnabled: false, broadSearchEnabled: false }),
      ]),
    )
    render(<SearchProfilesScreen />)

    const items = within(await screen.findByRole('list', { name: 'Search profiles' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Saved-company monitoringOn')
    expect(items[0]).toHaveTextContent('Broad searchOff')
    expect(items[1]).toHaveTextContent('Saved-company monitoringOff')
    expect(items[1]).toHaveTextContent('Broad searchOff')
  })

  it('filters the saved profiles by candidate', async () => {
    mockApi(
      baseRoutes([
        profile({ id: 'p1', name: 'Asha search', candidateId: 'c1' }),
        profile({ id: 'p2', name: 'Ravi search', candidateId: 'c2' }),
      ]),
    )
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await screen.findByText('Ravi search')
    await user.selectOptions(screen.getByLabelText('Show profiles for'), 'c2')

    expect(screen.queryByText('Asha search')).not.toBeInTheDocument()
    expect(screen.getByText('Ravi search')).toBeInTheDocument()
  })

  it('edits a profile: inherit to explicit empty set, and changes the selected lists', async () => {
    const calls = mockApi({
      ...baseRoutes([
        profile({
          workModes: null,
          companyLists: [{ id: 'l1', name: listA.name, defaultWorkModes: ['onSite', 'hybrid'] }],
        }),
      ]),
      'PUT /api/search-profiles/p1': { status: 200, body: profile() },
      'PUT /api/search-profiles/p1/company-lists/l2': { status: 204 },
      'DELETE /api/search-profiles/p1/company-lists/l1': { status: 204 },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    expect(within(form).getByText('Candidate: Asha Rao')).toBeInTheDocument()
    await user.click(within(form).getByLabelText('Choose work modes for this profile'))
    await user.click(within(form).getByLabelText('Broad search'))
    await user.click(within(form).getByLabelText('Ahmedabad product companies'))
    await user.click(within(form).getByLabelText('Remote-first startups'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    await vi.waitFor(() =>
      expect(calls.some((c) => c.method === 'DELETE' && c.path.endsWith('/company-lists/l1'))).toBe(true),
    )
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/search-profiles/p1')?.body).toEqual({
      name: 'Backend roles',
      workModes: [],
      companyMonitoringEnabled: true,
      broadSearchEnabled: true,
    })
    expect(calls.some((c) => c.method === 'PUT' && c.path.endsWith('/company-lists/l2'))).toBe(true)
  })

  it('sends null when an explicit set is switched back to inherit', async () => {
    const calls = mockApi({
      ...baseRoutes([profile({ workModes: ['remote'] })]),
      'PUT /api/search-profiles/p1': { status: 200, body: profile() },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    expect(within(form).getByLabelText('Remote')).toBeChecked()
    await user.click(within(form).getByLabelText('Inherit from the selected lists'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    await vi.waitFor(() =>
      expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/search-profiles/p1')).toBe(true),
    )
    const put = calls.find((c) => c.method === 'PUT' && c.path === '/api/search-profiles/p1')
    expect((put?.body as { workModes: unknown }).workModes).toBeNull()
  })

  it('deletes a profile after confirmation', async () => {
    let profiles = [profile()]
    mockApi({
      ...baseRoutes(),
      'GET /api/search-profiles': () => ({ status: 200, body: profiles }),
      'DELETE /api/search-profiles/p1': () => {
        profiles = []
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Delete Backend roles' }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete Backend roles' }))

    expect(await screen.findByText('No search profiles yet.')).toBeInTheDocument()
  })

  it('shows a not-found conflict when the profile disappeared', async () => {
    mockApi({
      ...baseRoutes([profile()]),
      'DELETE /api/search-profiles/p1': problem(404, 'not_found'),
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Delete Backend roles' }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete Backend roles' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No longer exists')
  })

  it('shows a load error with a way to retry', async () => {
    mockApi({
      'GET /api/candidates': { status: 200, body: [asha] },
      'GET /api/company-lists': problem(500, 'unexpected'),
      'GET /api/search-profiles': { status: 200, body: [] },
    })
    render(<SearchProfilesScreen />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Server error')
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('keeps the editor disabled until the reload after a partly failed save finishes', async () => {
    const stale = profile({ companyLists: [{ id: 'l1', name: listA.name, defaultWorkModes: [] }] })
    const reloaded = profile({ name: 'Renamed on server', companyLists: stale.companyLists })
    const release = deferred<MockReply>()
    let gets = 0
    mockApi({
      ...baseRoutes(),
      'GET /api/search-profiles': () => {
        gets += 1
        return gets === 1 ? { status: 200, body: [stale] } : release.promise
      },
      'PUT /api/search-profiles/p1': { status: 200, body: reloaded },
      'PUT /api/search-profiles/p1/company-lists/l2': problem(500, 'unexpected'),
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    await user.click(within(form).getByLabelText('Remote-first startups'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    // The write has failed and the reload is still in flight: nothing may be edited yet.
    await vi.waitFor(() => expect(gets).toBe(2))
    expect(within(form).getByRole('button', { name: 'Save profile' })).toBeDisabled()
    expect(within(form).getByLabelText('Profile name')).toBeDisabled()

    release.resolve({ status: 200, body: [reloaded] })
    const fresh = await screen.findByDisplayValue('Renamed on server')
    expect(fresh).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Save profile' })).toBeEnabled()
  })

  it('creates a profile selecting several role lists and location sets with the company lists', async () => {
    const calls = mockApi({
      ...baseRoutes(),
      'POST /api/search-profiles': { status: 201, body: profile() },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    await user.selectOptions(within(form).getByLabelText('Candidate'), 'c1')
    await user.type(within(form).getByLabelText('Profile name'), 'Criteria')
    await user.click(within(form).getByLabelText('Remote-first startups'))
    await user.click(within(form).getByLabelText('Backend titles'))
    await user.click(within(form).getByLabelText('Data titles'))
    await user.click(within(form).getByLabelText('Gujarat cities'))
    await user.click(within(form).getByLabelText('Remote scopes'))
    await user.click(within(form).getByLabelText('Remote scopes'))
    await user.click(within(form).getByRole('button', { name: 'Create profile' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      candidateId: 'c1',
      name: 'Criteria',
      companyListIds: ['l2'],
      roleListIds: ['r1', 'r2'],
      locationSetIds: ['s1'],
      companyMonitoringEnabled: false,
      broadSearchEnabled: false,
    })
  })

  it('says when there are no role lists or location sets to select', async () => {
    mockApi({
      ...baseRoutes(),
      'GET /api/role-lists': { status: 200, body: [] },
      'GET /api/location-sets': { status: 200, body: [] },
    })
    render(<SearchProfilesScreen />)

    const form = await screen.findByRole('form', { name: 'New search profile' })
    expect(within(form).getByText(/No role lists yet/)).toBeInTheDocument()
    expect(within(form).getByText(/No location sets yet/)).toBeInTheDocument()
  })

  it('shows the selected role lists and location sets of each profile', async () => {
    mockApi(
      baseRoutes([
        profile({
          id: 'p1',
          name: 'With criteria',
          roleLists: [{ id: 'r1', name: roleA.name }],
          locationSets: [
            { id: 's1', name: placeA.name },
            { id: 's2', name: placeB.name },
          ],
        }),
        profile({ id: 'p2', name: 'Bare' }),
      ]),
    )
    render(<SearchProfilesScreen />)

    const items = within(await screen.findByRole('list', { name: 'Search profiles' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Role listsBackend titles')
    expect(items[0]).toHaveTextContent('Location setsGujarat cities, Remote scopes')
    expect(items[1]).toHaveTextContent('Role listsNone selected')
    expect(items[1]).toHaveTextContent('Location setsNone selected')
  })

  it('selects and unselects role lists and location sets independently of company lists on save', async () => {
    const calls = mockApi({
      ...baseRoutes([
        profile({
          companyLists: [{ id: 'l1', name: listA.name, defaultWorkModes: [] }],
          roleLists: [{ id: 'r1', name: roleA.name }],
          locationSets: [{ id: 's1', name: placeA.name }],
        }),
      ]),
      'PUT /api/search-profiles/p1': { status: 200, body: profile() },
      'PUT /api/search-profiles/p1/role-lists/r2': { status: 204 },
      'DELETE /api/search-profiles/p1/role-lists/r1': { status: 204 },
      'PUT /api/search-profiles/p1/location-sets/s2': { status: 204 },
      'DELETE /api/search-profiles/p1/location-sets/s1': { status: 204 },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    expect(within(form).getByLabelText('Ahmedabad product companies')).toBeChecked()
    expect(within(form).getByLabelText('Backend titles')).toBeChecked()
    expect(within(form).getByLabelText('Gujarat cities')).toBeChecked()
    await user.click(within(form).getByLabelText('Backend titles'))
    await user.click(within(form).getByLabelText('Data titles'))
    await user.click(within(form).getByLabelText('Gujarat cities'))
    await user.click(within(form).getByLabelText('Remote scopes'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    await vi.waitFor(() =>
      expect(calls.some((c) => c.method === 'DELETE' && c.path.endsWith('/location-sets/s1'))).toBe(true),
    )
    const writes = calls.filter((c) => c.method !== 'GET').map((c) => `${c.method} ${c.path}`)
    expect(writes).toEqual([
      'PUT /api/search-profiles/p1',
      'PUT /api/search-profiles/p1/role-lists/r2',
      'DELETE /api/search-profiles/p1/role-lists/r1',
      'PUT /api/search-profiles/p1/location-sets/s2',
      'DELETE /api/search-profiles/p1/location-sets/s1',
    ])
  })

  it('keeps the company-list selection, work-mode override and discovery flags when criteria change', async () => {
    const calls = mockApi({
      ...baseRoutes([
        profile({
          workModes: ['remote'],
          companyMonitoringEnabled: true,
          broadSearchEnabled: true,
          companyLists: [{ id: 'l1', name: listA.name, defaultWorkModes: [] }],
        }),
      ]),
      'PUT /api/search-profiles/p1': { status: 200, body: profile() },
      'PUT /api/search-profiles/p1/role-lists/r1': { status: 204 },
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    await user.click(within(form).getByLabelText('Backend titles'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    await vi.waitFor(() => expect(calls.some((c) => c.path.endsWith('/role-lists/r1'))).toBe(true))
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/search-profiles/p1')?.body).toEqual({
      name: 'Backend roles',
      workModes: ['remote'],
      companyMonitoringEnabled: true,
      broadSearchEnabled: true,
    })
    expect(calls.some((c) => c.path.includes('/company-lists/'))).toBe(false)
  })

  it('shows the server state and the error when a role-list selection fails part-way', async () => {
    mockApi({
      ...baseRoutes([profile()]),
      'PUT /api/search-profiles/p1': { status: 200, body: profile() },
      'PUT /api/search-profiles/p1/role-lists/r1': problem(404, 'not_found'),
    })
    const user = userEvent.setup()
    render(<SearchProfilesScreen />)

    await user.click(await screen.findByRole('button', { name: 'Edit Backend roles' }))
    const form = screen.getByRole('form', { name: 'Edit search profile' })
    await user.click(within(form).getByLabelText('Backend titles'))
    await user.click(within(form).getByRole('button', { name: 'Save profile' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No longer exists')
    expect(screen.getByRole('form', { name: 'Edit search profile' })).toBeInTheDocument()
  })

  it('blocks the screen with an error when role lists cannot be loaded', async () => {
    mockApi({
      ...baseRoutes(),
      'GET /api/role-lists': problem(500, 'unexpected'),
    })
    render(<SearchProfilesScreen />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Server error')
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'New search profile' })).not.toBeInTheDocument()
  })
})
