import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, problem } from '../test/mockApi'
import RoleListsScreen from './RoleListsScreen'

afterEach(() => vi.restoreAllMocks())

const summary = { id: 'r1', name: 'Backend titles', titleCount: 2, aliasCount: 3 }

const detail = {
  ...summary,
  titles: [
    {
      id: 't1',
      title: 'Software Engineer',
      aliases: [
        { id: 'a1', alias: 'Software Developer' },
        { id: 'a2', alias: 'SDE' },
      ],
    },
    { id: 't2', title: 'Backend Engineer', aliases: [{ id: 'a3', alias: 'Server Engineer' }] },
  ],
}

const listRoutes = (lists: unknown[] = [summary]) => ({
  'GET /api/role-lists': { status: 200, body: lists },
})

async function openList(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /Backend titles/ }))
  return await screen.findByRole('region', { name: 'Role list Backend titles' })
}

describe('RoleListsScreen', () => {
  it('shows loading, then an empty state', async () => {
    mockApi(listRoutes([]))
    render(<RoleListsScreen />)
    expect(screen.getByRole('status')).toHaveTextContent('Loading role lists')
    expect(await screen.findByText(/No role lists yet/)).toBeInTheDocument()
  })

  it('lists role lists with title and alias counts', async () => {
    mockApi(listRoutes([summary, { id: 'r2', name: 'Data titles', titleCount: 1, aliasCount: 1 }]))
    render(<RoleListsScreen />)
    const list = await screen.findByRole('list', { name: 'Role lists' })
    const items = within(list).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Backend titles')
    expect(items[0]).toHaveTextContent('2 titles · 3 aliases')
    expect(items[1]).toHaveTextContent('1 title · 1 alias')
  })

  it('shows an error with a reload action when the lists cannot be loaded', async () => {
    mockApi({ 'GET /api/role-lists': problem(500, 'unexpected') })
    render(<RoleListsScreen />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Server error')
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('creates a role list from a trimmed name and opens it', async () => {
    let created = false
    const calls = mockApi({
      'GET /api/role-lists': () => ({ status: 200, body: created ? [summary] : [] }),
      'POST /api/role-lists': () => {
        created = true
        return { status: 201, body: detail }
      },
      'GET /api/role-lists/r1': { status: 200, body: detail },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)

    const form = await screen.findByRole('form', { name: 'New role list' })
    await user.type(within(form).getByLabelText('Role list name'), '  Backend titles ')
    await user.click(within(form).getByRole('button', { name: 'Create role list' }))

    expect(await screen.findByRole('region', { name: 'Role list Backend titles' })).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Backend titles' })
  })

  it('requires a name before calling the API', async () => {
    const calls = mockApi(listRoutes([]))
    const user = userEvent.setup()
    render(<RoleListsScreen />)

    const form = await screen.findByRole('form', { name: 'New role list' })
    await user.click(within(form).getByRole('button', { name: 'Create role list' }))
    expect(within(form).getByText('A name is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('reports a duplicate role list name', async () => {
    mockApi({ ...listRoutes([]), 'POST /api/role-lists': problem(409, 'duplicate_name') })
    const user = userEvent.setup()
    render(<RoleListsScreen />)

    const form = await screen.findByRole('form', { name: 'New role list' })
    await user.type(within(form).getByLabelText('Role list name'), 'Backend titles')
    await user.click(within(form).getByRole('button', { name: 'Create role list' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Name already in use')
    expect(alert).toHaveTextContent('another role list')
  })

  it('shows titles with their aliases as equivalent titles, not as separate entries', async () => {
    mockApi({ ...listRoutes(), 'GET /api/role-lists/r1': { status: 200, body: detail } })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const items = within(within(region).getByRole('list', { name: 'Titles in this role list' })).getAllByRole(
      'listitem',
    )
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Software Engineer')
    expect(items[0]).toHaveTextContent('Also known as: Software Developer, SDE')
    expect(items[1]).toHaveTextContent('Also known as: Server Engineer')
  })

  it('says when a role list has no titles and when a title has no aliases', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': {
        status: 200,
        body: { ...summary, titleCount: 1, aliasCount: 0, titles: [{ id: 't9', title: 'Analyst', aliases: [] }] },
      },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)
    expect(within(region).getByText('No equivalent titles')).toBeInTheDocument()
  })

  it('shows an empty titles state', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: { ...summary, titleCount: 0, aliasCount: 0, titles: [] } },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)
    expect(within(region).getByText('No titles in this role list yet.')).toBeInTheDocument()
  })

  it('adds a title with aliases, one per line, and reloads', async () => {
    let added = false
    const calls = mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': () => ({
        status: 200,
        body: added ? detail : { ...summary, titleCount: 0, aliasCount: 0, titles: [] },
      }),
      'POST /api/role-lists/r1/titles': () => {
        added = true
        return { status: 201, body: detail.titles[0] }
      },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.type(within(form).getByLabelText('Primary title'), ' Software Engineer ')
    await user.type(
      within(form).getByLabelText(/Equivalent titles/),
      'Software Developer{Enter}{Enter}  SDE {Enter}',
    )
    await user.click(within(form).getByRole('button', { name: 'Add title' }))

    expect(await within(region).findByText('Added Software Engineer.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/titles'))?.body).toEqual({
      title: 'Software Engineer',
      aliases: ['Software Developer', 'SDE'],
    })
    expect(await within(region).findByText(/Also known as: Software Developer, SDE/)).toBeInTheDocument()
  })

  it('requires a title and refuses an alias equal to its own title before calling the API', async () => {
    const calls = mockApi({ ...listRoutes(), 'GET /api/role-lists/r1': { status: 200, body: detail } })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.click(within(form).getByRole('button', { name: 'Add title' }))
    expect(within(form).getByText('A title is required.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText('Primary title'), 'Analyst')
    await user.type(within(form).getByLabelText(/Equivalent titles/), 'analyst ')
    await user.click(within(form).getByRole('button', { name: 'Add title' }))
    expect(within(form).getByText('An alias cannot be the same as its own title.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('refuses more than 50 aliases before calling the API', async () => {
    const calls = mockApi({ ...listRoutes(), 'GET /api/role-lists/r1': { status: 200, body: detail } })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.type(within(form).getByLabelText('Primary title'), 'Analyst')
    const many = Array.from({ length: 51 }, (_, i) => `Alias ${i}`).join('\n')
    await user.click(within(form).getByLabelText(/Equivalent titles/))
    await user.paste(many)
    await user.click(within(form).getByRole('button', { name: 'Add title' }))
    expect(within(form).getByText('A title can have at most 50 aliases.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('explains a duplicate title or alias and keeps what was typed', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'POST /api/role-lists/r1/titles': problem(409, 'duplicate_title'),
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.type(within(form).getByLabelText('Primary title'), 'SDE')
    await user.click(within(form).getByRole('button', { name: 'Add title' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Title already in this list')
    expect(alert).toHaveTextContent('Nothing was saved')
    expect(within(form).getByLabelText('Primary title')).toHaveValue('SDE')
  })

  it('shows server validation messages for the title', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'POST /api/role-lists/r1/titles': problem(400, 'validation', {
        errors: { title: ['A title is too long.'] },
      }),
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.type(within(form).getByLabelText('Primary title'), 'Something')
    await user.click(within(form).getByRole('button', { name: 'Add title' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Check the form')
    expect(alert).toHaveTextContent('Title: A title is too long.')
    expect(within(form).getByLabelText('Primary title')).toHaveAttribute('aria-invalid', 'true')
  })

  it('edits a title and replaces its whole alias set', async () => {
    const calls = mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'PUT /api/role-lists/r1/titles/t1': {
        status: 200,
        body: { id: 't1', title: 'Software Engineer II', aliases: [{ id: 'a9', alias: 'SDE II' }] },
      },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    await user.click(within(region).getByRole('button', { name: 'Edit Software Engineer' }))
    const form = within(region).getByRole('form', { name: 'Edit title' })
    expect(within(form).getByLabelText('Primary title')).toHaveValue('Software Engineer')
    expect(within(form).getByLabelText(/Equivalent titles/)).toHaveValue('Software Developer\nSDE')
    await user.clear(within(form).getByLabelText('Primary title'))
    await user.type(within(form).getByLabelText('Primary title'), 'Software Engineer II')
    await user.clear(within(form).getByLabelText(/Equivalent titles/))
    await user.type(within(form).getByLabelText(/Equivalent titles/), 'SDE II')
    await user.click(within(form).getByRole('button', { name: 'Save title' }))

    expect(await within(region).findByText('Saved Software Engineer II.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      title: 'Software Engineer II',
      aliases: ['SDE II'],
    })
  })

  it('clearing every alias sends an empty alias set', async () => {
    const calls = mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'PUT /api/role-lists/r1/titles/t1': {
        status: 200,
        body: { id: 't1', title: 'Software Engineer', aliases: [] },
      },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    await user.click(within(region).getByRole('button', { name: 'Edit Software Engineer' }))
    const form = within(region).getByRole('form', { name: 'Edit title' })
    await user.clear(within(form).getByLabelText(/Equivalent titles/))
    await user.click(within(form).getByRole('button', { name: 'Save title' }))

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ title: 'Software Engineer', aliases: [] })
  })

  it('asks for confirmation before deleting a title, and Keep cancels', async () => {
    const calls = mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'DELETE /api/role-lists/r1/titles/t2': { status: 204 },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    await user.click(within(region).getByRole('button', { name: 'Delete Backend Engineer' }))
    await user.click(within(region).getByRole('button', { name: 'Keep' }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    await user.click(within(region).getByRole('button', { name: 'Delete Backend Engineer' }))
    await user.click(within(region).getByRole('button', { name: 'Confirm delete Backend Engineer' }))
    await vi.waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true))
  })

  it('renames the list', async () => {
    const calls = mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'PUT /api/role-lists/r1': { status: 200, body: { ...detail, name: 'Platform titles' } },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    const form = within(region).getByRole('form', { name: 'Rename role list' })
    await user.clear(within(form).getByLabelText('Role list name'))
    await user.type(within(form).getByLabelText('Role list name'), ' Platform titles ')
    await user.click(within(form).getByRole('button', { name: 'Save name' }))

    expect(await within(region).findByText('Role list renamed.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: 'Platform titles' })
  })

  it('deletes a list after confirmation', async () => {
    let deleted = false
    mockApi({
      'GET /api/role-lists': () => ({ status: 200, body: deleted ? [] : [summary] }),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'DELETE /api/role-lists/r1': () => {
        deleted = true
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    await user.click(within(region).getByRole('button', { name: 'Delete role list' }))
    await user.click(within(region).getByRole('button', { name: 'Yes, delete this role list' }))

    expect(await screen.findByText(/No role lists yet/)).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Role list Backend titles' })).not.toBeInTheDocument()
  })

  it('explains that a role list selected by a profile cannot be deleted', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
      'DELETE /api/role-lists/r1': problem(409, 'role_list_in_use', { searchProfileCount: 2 }),
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    await user.click(within(region).getByRole('button', { name: 'Delete role list' }))
    await user.click(within(region).getByRole('button', { name: 'Yes, delete this role list' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Role list is in use')
    expect(alert).toHaveTextContent('2 search profiles still select this role list')
  })

  it('says a list no longer exists when it was deleted elsewhere', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': problem(404, 'not_found'),
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    await user.click(await screen.findByRole('button', { name: /Backend titles/ }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No longer exists')
    expect(within(alert).getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('shows a network failure on a write as possibly applied', async () => {
    mockApi({
      ...listRoutes(),
      'GET /api/role-lists/r1': { status: 200, body: detail },
    })
    const user = userEvent.setup()
    render(<RoleListsScreen />)
    const region = await openList(user)

    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    const form = within(region).getByRole('form', { name: 'Add title' })
    await user.type(within(form).getByLabelText('Primary title'), 'Analyst')
    await user.click(within(form).getByRole('button', { name: 'Add title' }))

    const alert = await within(region).findByRole('alert')
    expect(alert).toHaveTextContent('Cannot reach the API')
    expect(alert).toHaveTextContent('may or may not have been applied')
  })
})
