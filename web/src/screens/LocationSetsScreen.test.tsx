import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, problem } from '../test/mockApi'
import LocationSetsScreen from './LocationSetsScreen'

afterEach(() => vi.restoreAllMocks())

const summary = { id: 's1', name: 'India targets', locationCount: 4 }

const detail = {
  ...summary,
  locations: [
    { id: 'x1', label: 'Ahmedabad', scope: 'city', countryCode: 'IN' },
    { id: 'x2', label: 'Gujarat', scope: 'state', countryCode: null },
    { id: 'x3', label: 'Remote India', scope: 'remoteCountry', countryCode: 'IN' },
    { id: 'x4', label: 'Remote worldwide', scope: 'remoteWorldwide', countryCode: null },
  ],
}

const listRoutes = (sets: unknown[] = [summary]) => ({
  'GET /api/location-sets': { status: 200, body: sets },
})

const detailRoutes = () => ({
  ...listRoutes(),
  'GET /api/location-sets/s1': { status: 200, body: detail },
})

async function openSet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /India targets/ }))
  return await screen.findByRole('region', { name: 'Location set India targets' })
}

describe('LocationSetsScreen', () => {
  it('shows loading, then an empty state', async () => {
    mockApi(listRoutes([]))
    render(<LocationSetsScreen />)
    expect(screen.getByRole('status')).toHaveTextContent('Loading location sets')
    expect(await screen.findByText(/No location sets yet/)).toBeInTheDocument()
  })

  it('lists sets with their location counts', async () => {
    mockApi(listRoutes([summary, { id: 's2', name: 'Abroad', locationCount: 1 }]))
    render(<LocationSetsScreen />)
    const items = within(await screen.findByRole('list', { name: 'Location sets' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('4 locations')
    expect(items[1]).toHaveTextContent('1 location')
  })

  it('shows an error with a reload action when the sets cannot be loaded', async () => {
    mockApi({ 'GET /api/location-sets': problem(500, 'unexpected') })
    render(<LocationSetsScreen />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Server error')
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('creates a set from a trimmed name and opens it', async () => {
    let created = false
    const calls = mockApi({
      'GET /api/location-sets': () => ({ status: 200, body: created ? [summary] : [] }),
      'POST /api/location-sets': () => {
        created = true
        return { status: 201, body: detail }
      },
      'GET /api/location-sets/s1': { status: 200, body: detail },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)

    const form = await screen.findByRole('form', { name: 'New location set' })
    await user.type(within(form).getByLabelText('Location set name'), '  India targets ')
    await user.click(within(form).getByRole('button', { name: 'Create location set' }))

    expect(await screen.findByRole('region', { name: 'Location set India targets' })).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'India targets' })
  })

  it('requires a name and reports a duplicate name', async () => {
    const calls = mockApi({ ...listRoutes([]), 'POST /api/location-sets': problem(409, 'duplicate_name') })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)

    const form = await screen.findByRole('form', { name: 'New location set' })
    await user.click(within(form).getByRole('button', { name: 'Create location set' }))
    expect(within(form).getByText('A name is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await user.type(within(form).getByLabelText('Location set name'), 'India targets')
    await user.click(within(form).getByRole('button', { name: 'Create location set' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Name already in use')
    expect(alert).toHaveTextContent('Another location set')
  })

  it('spells out every scope, so Remote India and Remote worldwide are never confused', async () => {
    mockApi(detailRoutes())
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const items = within(within(region).getByRole('list', { name: 'Locations in this set' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Ahmedabad · City · IN')
    expect(items[1]).toHaveTextContent('Gujarat · State or region')
    expect(items[2]).toHaveTextContent('Remote India · Remote within a country · IN')
    expect(items[3]).toHaveTextContent('Remote worldwide · Remote worldwide')
    expect(items[3]).not.toHaveTextContent('IN')
  })

  it('keeps work modes out of locations', async () => {
    mockApi(detailRoutes())
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    expect(within(form).queryByLabelText('Hybrid')).not.toBeInTheDocument()
    expect(within(form).queryByLabelText('On-site')).not.toBeInTheDocument()
    expect(within(form).getByText(/work modes, set on the search profile/)).toBeInTheDocument()
    const scopes = within(within(form).getByLabelText('Covers')).getAllByRole('option')
    expect(scopes.map((o) => o.textContent)).toEqual([
      'City',
      'State or region',
      'Country',
      'Remote within a country',
      'Remote worldwide',
    ])
  })

  it('shows an empty locations state', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/location-sets/s1': { status: 200, body: { ...summary, locationCount: 0, locations: [] } },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)
    expect(within(region).getByText('No locations in this set yet.')).toBeInTheDocument()
  })

  it('adds a city with an upper-cased country code', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': {
        status: 201,
        body: { id: 'x9', label: 'Pune', scope: 'city', countryCode: 'IN' },
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), ' Pune ')
    await user.type(within(form).getByLabelText('Country code (optional)'), 'in')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))

    expect(await within(region).findByText('Added Pune.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/locations'))?.body).toEqual({
      label: 'Pune',
      scope: 'city',
      countryCode: 'IN',
    })
  })

  it('sends no country code for a city without one', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': {
        status: 201,
        body: { id: 'x9', label: 'Pune', scope: 'city', countryCode: null },
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), 'Pune')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ label: 'Pune', scope: 'city', countryCode: null })
  })

  it('needs a country code for remote within a country', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': {
        status: 201,
        body: { id: 'x9', label: 'Remote India', scope: 'remoteCountry', countryCode: 'IN' },
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), 'Remote India')
    await user.selectOptions(within(form).getByLabelText('Covers'), 'remoteCountry')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))
    expect(within(form).getByText(/needs a two-letter country code/)).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await user.type(within(form).getByLabelText('Country code'), 'IN')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))
    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      label: 'Remote India',
      scope: 'remoteCountry',
      countryCode: 'IN',
    })
  })

  it('has no country field for remote worldwide and sends no code', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': {
        status: 201,
        body: { id: 'x9', label: 'Anywhere', scope: 'remoteWorldwide', countryCode: null },
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), 'Anywhere')
    await user.selectOptions(within(form).getByLabelText('Covers'), 'remoteWorldwide')
    expect(within(form).queryByLabelText(/Country code/)).not.toBeInTheDocument()
    expect(within(form).getByText(/no country/)).toBeInTheDocument()
    await user.click(within(form).getByRole('button', { name: 'Add location' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      label: 'Anywhere',
      scope: 'remoteWorldwide',
      countryCode: null,
    })
  })

  it('requires a label and a two-letter code before calling the API', async () => {
    const calls = mockApi(detailRoutes())
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.click(within(form).getByRole('button', { name: 'Add location' }))
    expect(within(form).getByText('A label is required.')).toBeInTheDocument()
    expect(within(form).getByLabelText('Label')).toHaveAccessibleDescription('A label is required.')

    await user.type(within(form).getByLabelText('Label'), 'Pune')
    await user.type(within(form).getByLabelText('Country code (optional)'), '1')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))
    expect(within(form).getByText('A country code is two letters, such as IN.')).toBeInTheDocument()
    expect(within(form).getByLabelText('Country code (optional)')).toHaveAccessibleDescription(
      'A country code is two letters, such as IN.',
    )
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('explains a duplicate location and keeps what was typed', async () => {
    mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': problem(409, 'duplicate_location'),
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), 'Ahmedabad')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Location already in this set')
    expect(within(form).getByLabelText('Label')).toHaveValue('Ahmedabad')
  })

  it('shows server validation messages for the scope', async () => {
    mockApi({
      ...detailRoutes(),
      'POST /api/location-sets/s1/locations': problem(400, 'validation', {
        errors: { scope: ['A scope is required.'] },
      }),
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Add location' })
    await user.type(within(form).getByLabelText('Label'), 'Pune')
    await user.click(within(form).getByRole('button', { name: 'Add location' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Scope: A scope is required.')
    expect(within(form).getByLabelText('Covers')).toHaveAttribute('aria-invalid', 'true')
    expect(within(form).getByLabelText('Covers')).toHaveAccessibleDescription('A scope is required.')
  })

  it('edits a location, including its scope and country', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'PUT /api/location-sets/s1/locations/x1': {
        status: 200,
        body: { id: 'x1', label: 'Ahmedabad', scope: 'remoteCountry', countryCode: 'IN' },
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    await user.click(within(region).getByRole('button', { name: 'Edit Ahmedabad' }))
    const form = within(region).getByRole('form', { name: 'Edit location' })
    expect(within(form).getByLabelText('Label')).toHaveValue('Ahmedabad')
    expect(within(form).getByLabelText('Covers')).toHaveValue('city')
    expect(within(form).getByLabelText('Country code (optional)')).toHaveValue('IN')
    await user.selectOptions(within(form).getByLabelText('Covers'), 'remoteCountry')
    await user.click(within(form).getByRole('button', { name: 'Save location' }))

    expect(await within(region).findByText('Saved Ahmedabad.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      label: 'Ahmedabad',
      scope: 'remoteCountry',
      countryCode: 'IN',
    })
  })

  it('asks for confirmation before deleting a location, and Keep cancels', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'DELETE /api/location-sets/s1/locations/x2': { status: 204 },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    await user.click(within(region).getByRole('button', { name: 'Delete Gujarat' }))
    await user.click(within(region).getByRole('button', { name: 'Keep' }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    await user.click(within(region).getByRole('button', { name: 'Delete Gujarat' }))
    await user.click(within(region).getByRole('button', { name: 'Confirm delete Gujarat' }))
    await vi.waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true))
  })

  it('renames the set', async () => {
    const calls = mockApi({
      ...detailRoutes(),
      'PUT /api/location-sets/s1': { status: 200, body: { ...detail, name: 'Targets' } },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    const form = within(region).getByRole('form', { name: 'Rename location set' })
    await user.clear(within(form).getByLabelText('Location set name'))
    await user.type(within(form).getByLabelText('Location set name'), 'Targets')
    await user.click(within(form).getByRole('button', { name: 'Save name' }))

    expect(await within(region).findByText('Location set renamed.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: 'Targets' })
  })

  it('deletes a set after confirmation', async () => {
    let deleted = false
    mockApi({
      'GET /api/location-sets': () => ({ status: 200, body: deleted ? [] : [summary] }),
      'GET /api/location-sets/s1': { status: 200, body: detail },
      'DELETE /api/location-sets/s1': () => {
        deleted = true
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    await user.click(within(region).getByRole('button', { name: 'Delete location set' }))
    await user.click(within(region).getByRole('button', { name: 'Yes, delete this location set' }))

    expect(await screen.findByText(/No location sets yet/)).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Location set India targets' })).not.toBeInTheDocument()
  })

  it('explains that a set selected by a profile cannot be deleted', async () => {
    mockApi({
      ...detailRoutes(),
      'DELETE /api/location-sets/s1': problem(409, 'location_set_in_use', { searchProfileCount: 1 }),
    })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    const region = await openSet(user)

    await user.click(within(region).getByRole('button', { name: 'Delete location set' }))
    await user.click(within(region).getByRole('button', { name: 'Yes, delete this location set' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Location set is in use')
    expect(alert).toHaveTextContent('1 search profile still select this location set')
  })

  it('says a set no longer exists when it was deleted elsewhere', async () => {
    mockApi({ ...listRoutes(), 'GET /api/location-sets/s1': problem(404, 'not_found') })
    const user = userEvent.setup()
    render(<LocationSetsScreen />)
    await user.click(await screen.findByRole('button', { name: /India targets/ }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No longer exists')
  })
})
