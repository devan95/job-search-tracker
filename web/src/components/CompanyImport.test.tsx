import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import CompanyListsScreen from '../screens/CompanyListsScreen'
import { mockApi, problem, type MockReply } from '../test/mockApi'
import { CompanyImport } from './CompanyImport'

afterEach(() => vi.restoreAllMocks())

const PREVIEW_URL = 'POST /api/company-lists/l1/import/preview'
const IMPORT_URL = 'POST /api/company-lists/l1/import'

const PASTED = 'Acme\n  GLOBEX \nInitech, Inc.\n\ninitech, inc.'

const previewBody = {
  rows: [
    {
      rowNumber: 1,
      input: 'Acme',
      name: 'Acme',
      normalizedName: 'acme',
      status: 'already_in_list',
      existingEmployerId: 'b1',
      duplicateOfRow: null,
      message: 'Already in this list; it will be skipped.',
    },
    {
      rowNumber: 2,
      input: '  GLOBEX ',
      name: 'GLOBEX',
      normalizedName: 'globex',
      status: 'existing_employer',
      existingEmployerId: 'c7',
      duplicateOfRow: null,
      message: 'The company already exists; it will be reused and added to the list.',
    },
    {
      rowNumber: 3,
      input: 'Initech, Inc.',
      name: 'Initech, Inc.',
      normalizedName: 'initech, inc.',
      status: 'new',
      existingEmployerId: null,
      duplicateOfRow: null,
      message: 'New company; it will be created and added to the list.',
    },
    {
      rowNumber: 5,
      input: 'initech, inc.',
      name: 'initech, inc.',
      normalizedName: 'initech, inc.',
      status: 'duplicate_in_input',
      existingEmployerId: null,
      duplicateOfRow: 3,
      message: 'Same company as row 3; it will be skipped.',
    },
  ],
  summary: { total: 4, new: 1, existingEmployer: 1, alreadyInList: 1, duplicateInInput: 1, invalid: 0 },
}

const invalidPreviewBody = {
  rows: [
    {
      rowNumber: 1,
      input: 'Acme',
      name: 'Acme',
      normalizedName: 'acme',
      status: 'new',
      existingEmployerId: null,
      duplicateOfRow: null,
      message: 'New company; it will be created and added to the list.',
    },
    {
      rowNumber: 2,
      input: 'x'.repeat(201),
      name: null,
      normalizedName: null,
      status: 'invalid',
      existingEmployerId: null,
      duplicateOfRow: null,
      message: 'The name is longer than 200 characters.',
    },
  ],
  summary: { total: 2, new: 1, existingEmployer: 0, alreadyInList: 0, duplicateInInput: 0, invalid: 1 },
}

const saved = { created: 1, reused: 1, addedToList: 2, skipped: 2 }

async function pasteAndPreview(user: ReturnType<typeof userEvent.setup>, text = PASTED) {
  const box = screen.getByLabelText('Companies to import (one per line)')
  await user.click(box)
  await user.paste(text)
  await user.click(screen.getByRole('button', { name: 'Preview import' }))
}

describe('CompanyImport', () => {
  it('previews the pasted text exactly as typed and reports every row without saving', async () => {
    const calls = mockApi({ [PREVIEW_URL]: { status: 200, body: previewBody } })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user)

    const rows = await screen.findByRole('list', { name: 'Preview rows' })
    expect(within(rows).getByText('Row 1: Acme')).toBeInTheDocument()
    expect(within(rows).getByText('Row 2: GLOBEX')).toBeInTheDocument()
    expect(within(rows).getByText('Row 3: Initech, Inc.')).toBeInTheDocument()
    expect(within(rows).getByText('Row 5: initech, inc.')).toBeInTheDocument()
    expect(within(rows).getByText(/Already in this list\. Already in this list; it will be skipped\./)).toBeInTheDocument()
    expect(within(rows).getByText(/Duplicate in pasted text\. Same company as row 3/)).toBeInTheDocument()
    expect(
      screen.getByText('4 rows: 1 new, 1 existing, 1 already in this list, 1 duplicate in pasted text, 0 invalid.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm import' })).toBeEnabled()
    expect(calls).toHaveLength(1)
    expect(calls[0]).toMatchObject({ method: 'POST', path: '/api/company-lists/l1/import/preview' })
    expect(calls[0].body).toEqual({ text: PASTED })
  })

  it('saves the previewed text on confirm, shows the counts and tells the screen to reload', async () => {
    const onChanged = vi.fn()
    const calls = mockApi({
      [PREVIEW_URL]: { status: 200, body: previewBody },
      [IMPORT_URL]: { status: 200, body: saved },
    })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={onChanged} />)

    await pasteAndPreview(user)
    await user.click(await screen.findByRole('button', { name: 'Confirm import' }))

    expect(
      await screen.findByText('Import saved: 1 created, 1 reused, 2 skipped (2 added to this list).'),
    ).toBeInTheDocument()
    const save = calls.find((c) => c.path === '/api/company-lists/l1/import')
    expect(save?.body).toEqual({ text: PASTED })
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('list', { name: 'Preview rows' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm import' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Companies to import (one per line)')).toHaveValue('')
  })

  it('blocks the save while any row is invalid and says why', async () => {
    const calls = mockApi({ [PREVIEW_URL]: { status: 200, body: invalidPreviewBody } })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user, `Acme\n${'x'.repeat(201)}`)

    expect(await screen.findByText(/1 row is invalid, so nothing can be saved/)).toBeInTheDocument()
    expect(screen.getByText(/Invalid\. The name is longer than 200 characters\./)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm import' })).toBeDisabled()
    expect(calls.some((c) => c.path === '/api/company-lists/l1/import')).toBe(false)
  })

  it('discards the preview when the text is edited, so a stale preview cannot be saved', async () => {
    mockApi({ [PREVIEW_URL]: { status: 200, body: previewBody } })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user)
    await screen.findByRole('button', { name: 'Confirm import' })
    await user.type(screen.getByLabelText('Companies to import (one per line)'), '!')

    expect(screen.queryByRole('button', { name: 'Confirm import' })).not.toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Preview rows' })).not.toBeInTheDocument()
  })

  it('asks for at least one company without calling the API when the text is blank', async () => {
    const calls = mockApi({})
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user, ' \n  \n')

    expect(await screen.findByText('Paste at least one company name, one per line.')).toBeInTheDocument()
    expect(calls).toHaveLength(0)
  })

  it('shows the API validation message when the preview is rejected', async () => {
    mockApi({
      [PREVIEW_URL]: problem(400, 'validation', {
        errors: { text: ['At most 1000 companies can be imported at once.'] },
      }),
    })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user, 'Acme')

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Check the form')).toBeInTheDocument()
    expect(within(alert).getByText('Text: At most 1000 companies can be imported at once.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm import' })).not.toBeInTheDocument()
  })

  it('reports a deleted list as no longer existing', async () => {
    mockApi({ [PREVIEW_URL]: problem(404, 'not_found') })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user, 'Acme')

    expect(await screen.findByText('No longer exists')).toBeInTheDocument()
  })

  it('lists the per-row messages when the save is refused and does not claim a save', async () => {
    const onChanged = vi.fn()
    mockApi({
      [PREVIEW_URL]: { status: 200, body: previewBody },
      [IMPORT_URL]: problem(400, 'validation', {
        errors: { text: ['Row 2: The name is longer than 200 characters.'] },
      }),
    })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={onChanged} />)

    await pasteAndPreview(user)
    await user.click(await screen.findByRole('button', { name: 'Confirm import' }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText(/Row 2: The name is longer than 200 characters\./)).toBeInTheDocument()
    expect(screen.queryByText(/Import saved/)).not.toBeInTheDocument()
    expect(onChanged).not.toHaveBeenCalled()
  })

  it('treats an unreadable preview as an error, not as an empty preview', async () => {
    mockApi({ [PREVIEW_URL]: { status: 200, body: { rows: 'nope' } } })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={vi.fn()} />)

    await pasteAndPreview(user, 'Acme')

    expect(await screen.findByText('Unreadable response')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm import' })).not.toBeInTheDocument()
  })

  it('treats an unreadable save answer as an error', async () => {
    const onChanged = vi.fn()
    mockApi({
      [PREVIEW_URL]: { status: 200, body: previewBody },
      [IMPORT_URL]: { status: 200, body: { created: 'many' } },
    })
    const user = userEvent.setup()
    render(<CompanyImport listId="l1" onChanged={onChanged} />)

    await pasteAndPreview(user)
    await user.click(await screen.findByRole('button', { name: 'Confirm import' }))

    expect(await screen.findByText('Unreadable response')).toBeInTheDocument()
    expect(onChanged).not.toHaveBeenCalled()
  })
})

describe('company list screen with the import', () => {
  const acme = { id: 'e1', name: 'Acme Corp', normalizedName: 'acme corp' }
  const initech = { id: 'e3', name: 'Initech, Inc.', normalizedName: 'initech, inc.' }
  const summary = {
    id: 'l1',
    name: 'Ahmedabad product companies',
    locationLabel: 'Ahmedabad',
    defaultWorkModes: ['onSite'],
    employerCount: 1,
  }

  it('reloads the list after a confirmed import so the new companies appear', async () => {
    let employers = [acme]
    const calls = mockApi({
      'GET /api/company-lists': () => ({
        status: 200,
        body: [{ ...summary, employerCount: employers.length }],
      }),
      'GET /api/company-lists/l1': () => ({
        status: 200,
        body: { ...summary, employerCount: employers.length, employers },
      }),
      [PREVIEW_URL]: { status: 200, body: previewBody },
      [IMPORT_URL]: (): MockReply => {
        employers = [acme, initech]
        return { status: 200, body: saved }
      },
    })
    const user = userEvent.setup()
    render(<CompanyListsScreen />)

    await user.click(await screen.findByRole('button', { name: /Ahmedabad product companies/ }))
    await screen.findByRole('list', { name: 'Companies in this list' })
    await pasteAndPreview(user)
    await user.click(await screen.findByRole('button', { name: 'Confirm import' }))

    const companies = await screen.findByText('Initech, Inc.', {
      selector: '.rows[aria-label="Companies in this list"] .row-name',
    })
    expect(companies).toBeInTheDocument()
    expect(screen.getByText(/Import saved: 1 created/)).toBeInTheDocument()
    expect(calls.filter((c) => c.method === 'GET' && c.path === '/api/company-lists/l1').length).toBeGreaterThanOrEqual(2)
  })
})
