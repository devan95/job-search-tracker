import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, problem } from '../test/mockApi'
import CompanyListsScreen from './CompanyListsScreen'

afterEach(() => vi.restoreAllMocks())

const acme = { id: 'e1', name: 'Acme Corp', normalizedName: 'acme corp' }
const beta = { id: 'e2', name: 'Beta Ltd', normalizedName: 'beta ltd' }

const summary = {
  id: 'l1',
  name: 'Ahmedabad product companies',
  locationLabel: 'Ahmedabad',
  defaultWorkModes: ['onSite', 'hybrid'],
  employerCount: 1,
}
const detail = { ...summary, employers: [acme] }

const listName = /Ahmedabad product companies/

describe('CompanyListsScreen', () => {
  it('shows an empty state when there are no lists', async () => {
    mockApi({ 'GET /api/company-lists': { status: 200, body: [] } })
    render(<CompanyListsScreen />)
    expect(await screen.findByText(/No company lists yet/)).toBeInTheDocument()
  })

  it('summarises each list with its location, size and default work modes', async () => {
    mockApi({ 'GET /api/company-lists': { status: 200, body: [summary] } })
    render(<CompanyListsScreen />)
    expect(
      await screen.findByText('Ahmedabad · 1 company · On-site, Hybrid'),
    ).toBeInTheDocument()
  })

  it('shows "No default stated" for a list with the empty work-mode set', async () => {
    mockApi({
      'GET /api/company-lists': {
        status: 200,
        body: [{ ...summary, locationLabel: null, defaultWorkModes: [] }],
      },
    })
    render(<CompanyListsScreen />)
    expect(await screen.findByText('1 company · No default stated')).toBeInTheDocument()
  })

  it('opens a list and shows its companies', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': { status: 200, body: detail },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)

    await user.click(await screen.findByRole('button', { name: listName }))

    const companies = await screen.findByRole('list', { name: 'Companies in this list' })
    expect(within(companies).getByText('Acme Corp')).toBeInTheDocument()
  })

  it('creates a list with a location label and default work modes', async () => {
    const lists: (typeof summary)[] = []
    const calls = mockApi({
      'GET /api/company-lists': () => ({ status: 200, body: [...lists] }),
      'POST /api/company-lists': (req) => {
        lists.push({ ...summary, employerCount: 0 })
        return { status: 201, body: { ...summary, ...(req.body as object), employerCount: 0, employers: [] } }
      },
      'GET /api/company-lists/l1': { status: 200, body: { ...summary, employerCount: 0, employers: [] } },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await screen.findByText(/No company lists yet/)

    const form = screen.getByRole('form', { name: 'New company list' })
    await user.type(within(form).getByLabelText('List name'), '  Ahmedabad product companies ')
    await user.type(within(form).getByLabelText('Location label (optional)'), 'Ahmedabad')
    await user.click(within(form).getByLabelText('Hybrid'))
    await user.click(within(form).getByLabelText('On-site'))
    await user.click(within(form).getByRole('button', { name: 'Create list' }))

    expect(
      await screen.findByRole('heading', { name: 'Ahmedabad product companies' }),
    ).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      name: 'Ahmedabad product companies',
      locationLabel: 'Ahmedabad',
      defaultWorkModes: ['onSite', 'hybrid'],
    })
    expect(within(form).getByLabelText('List name')).toHaveValue('')
  })

  it('sends a blank location as null and no work modes as the empty set', async () => {
    const calls = mockApi({
      'GET /api/company-lists': { status: 200, body: [] },
      'POST /api/company-lists': { status: 201, body: { ...summary, employerCount: 0, employers: [] } },
      'GET /api/company-lists/l1': { status: 200, body: { ...summary, employerCount: 0, employers: [] } },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await screen.findByText(/No company lists yet/)

    const form = screen.getByRole('form', { name: 'New company list' })
    await user.type(within(form).getByLabelText('List name'), 'Remote-first')
    await user.click(within(form).getByRole('button', { name: 'Create list' }))

    await screen.findByRole('heading', { name: 'Ahmedabad product companies' })
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      name: 'Remote-first',
      locationLabel: null,
      defaultWorkModes: [],
    })
  })

  it('validates a blank list name without calling the API', async () => {
    const calls = mockApi({ 'GET /api/company-lists': { status: 200, body: [] } })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await screen.findByText(/No company lists yet/)

    await user.click(screen.getByRole('button', { name: 'Create list' }))

    expect(screen.getByText('A name is required.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows the API field errors for a rejected list', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [] },
      'POST /api/company-lists': problem(400, 'validation', {
        errors: { defaultWorkModes: ["Unknown work mode 'moon base'."] },
      }),
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await screen.findByText(/No company lists yet/)

    await user.type(screen.getByLabelText('List name'), 'Lunar')
    await user.click(screen.getByRole('button', { name: 'Create list' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Check the form')
    expect(alert).toHaveTextContent("Default work modes: Unknown work mode 'moon base'.")
  })

  it('reports a duplicate list name as a conflict', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'POST /api/company-lists': problem(409, 'duplicate_name'),
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await screen.findByRole('button', { name: listName })

    await user.type(screen.getByLabelText('List name'), 'ahmedabad PRODUCT companies')
    await user.click(screen.getByRole('button', { name: 'Create list' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Name already in use')
    expect(alert).toHaveTextContent('Another company list')
  })

  it('saves the list fields from the edit form', async () => {
    const calls = mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': { status: 200, body: detail },
      'PUT /api/company-lists/l1': { status: 200, body: detail },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    const form = await screen.findByRole('form', { name: 'Edit list' })
    const location = within(form).getByLabelText('Location label (optional)')
    await user.clear(location)
    await user.click(within(form).getByLabelText('Hybrid'))
    await user.click(within(form).getByRole('button', { name: 'Save list' }))

    expect(await screen.findByText('List saved.')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      name: 'Ahmedabad product companies',
      locationLabel: null,
      defaultWorkModes: ['onSite'],
    })
  })

  it('reuses an existing canonical company and tells the user', async () => {
    const employers = [acme]
    const calls = mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': () => ({
        status: 200,
        body: { ...summary, employerCount: employers.length, employers: [...employers] },
      }),
      'POST /api/employers/resolve': { status: 200, body: beta },
      'PUT /api/company-lists/l1/employers/e2': () => {
        employers.push(beta)
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.type(await screen.findByLabelText('Company name'), 'beta ltd')
    await user.click(screen.getByRole('button', { name: 'Add company' }))

    expect(await screen.findByText(/already existed as a company and was reused/)).toBeInTheDocument()
    const companies = await screen.findByRole('list', { name: 'Companies in this list' })
    expect(await within(companies).findByText('Beta Ltd')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/employers/resolve')?.body).toEqual({ name: 'beta ltd' })
  })

  it('says so when a company was newly created', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': { status: 200, body: detail },
      'POST /api/employers/resolve': { status: 201, body: beta },
      'PUT /api/company-lists/l1/employers/e2': { status: 204 },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.type(await screen.findByLabelText('Company name'), 'Beta Ltd')
    await user.click(screen.getByRole('button', { name: 'Add company' }))

    expect(await screen.findByText('Added Beta Ltd as a new company.')).toBeInTheDocument()
  })

  it('removes a company from the list only', async () => {
    let employers = [acme]
    const calls = mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': () => ({
        status: 200,
        body: { ...summary, employerCount: employers.length, employers },
      }),
      'DELETE /api/company-lists/l1/employers/e1': () => {
        employers = []
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.click(await screen.findByRole('button', { name: 'Remove Acme Corp from this list' }))

    expect(await screen.findByText('No companies in this list yet.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'DELETE' && c.path.startsWith('/api/employers'))).toBe(false)
  })

  it('explains a removal of a company that was not in the list', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': { status: 200, body: detail },
      'DELETE /api/company-lists/l1/employers/e1': problem(404, 'not_a_member'),
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.click(await screen.findByRole('button', { name: 'Remove Acme Corp from this list' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Already removed')
  })

  it('refuses to delete a list that a search profile selects', async () => {
    mockApi({
      'GET /api/company-lists': { status: 200, body: [summary] },
      'GET /api/company-lists/l1': { status: 200, body: detail },
      'DELETE /api/company-lists/l1': problem(409, 'company_list_in_use', { searchProfileCount: 1 }),
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.click(await screen.findByRole('button', { name: 'Delete list' }))
    await user.click(screen.getByRole('button', { name: 'Yes, delete this list' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('List is in use')
    expect(alert).toHaveTextContent('1 search profile')
  })

  it('deletes an unused list and returns to the overview', async () => {
    let lists = [summary]
    mockApi({
      'GET /api/company-lists': () => ({ status: 200, body: lists }),
      'GET /api/company-lists/l1': { status: 200, body: detail },
      'DELETE /api/company-lists/l1': () => {
        lists = []
        return { status: 204 }
      },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)
    await user.click(await screen.findByRole('button', { name: listName }))

    await user.click(await screen.findByRole('button', { name: 'Delete list' }))
    await user.click(screen.getByRole('button', { name: 'Yes, delete this list' }))

    expect(await screen.findByText(/No company lists yet/)).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /List Ahmedabad/ })).not.toBeInTheDocument()
  })
})
