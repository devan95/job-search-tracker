import { expect, test, type APIRequestContext } from '@playwright/test'
import {
  addCandidateViaUi,
  apiCreateCandidate,
  apiCreateList,
  apiCreateProfile,
  apiFindEmployers,
  apiGetList,
  apiGetProfile,
  apiListAll,
  apiImport,
  apiUrl,
  candidateRow,
  confirmImport,
  createListViaUi,
  gotoSection,
  importPreview,
  listDetail,
  openProfileEditor,
  previewImport,
  previewRow,
  previewSummary,
  profileCard,
  requireStack,
  resultText,
  selectListViaUi,
  summaryText,
  uid,
} from './support.js'

// Lists & Profiles workflow, end to end: browser -> API -> PostgreSQL. Synthetic data only; every name carries a
// random suffix, so the tests are independent of each other and of earlier runs. Set-up and evidence that the UI
// cannot show (stored values) go through the API; the behaviour under test is always driven through the UI.

test.beforeAll(async ({ playwright }) => {
  await requireStack({ playwright })
})

test('journey: candidate, two lists, import with a reused company, search profile', async ({ page, request }) => {
  test.setTimeout(90_000)
  const id = uid()
  const candidate = `Candidate ${id}`
  const alpha = `${id} alpha`
  const beta = `${id} beta`
  const profile = `Profile ${id}`

  // Candidate
  await gotoSection(page, 'Candidates')
  await addCandidateViaUi(page, candidate)

  // Two company lists; the first has a location label and default work modes
  await gotoSection(page, 'Company lists')
  await createListViaUi(page, alpha, { location: 'Ahmedabad', modes: ['Hybrid', 'On-site'] })
  await expect(listDetail(page, alpha)).toBeVisible()
  await createListViaUi(page, beta)
  await expect(listDetail(page, beta)).toBeVisible()

  // Import into the first list: whitespace, blank lines, a comma inside a name, a duplicate that only differs in case/spacing
  await selectListViaUi(page, alpha)
  await previewImport(
    page,
    alpha,
    [
      `  Acme ${id}  `,
      '',
      `Globex ${id}`,
      '   ',
      `Initech, Inc. ${id}`,
      `ACME   ${id}`,
    ].join('\n'),
  )
  await expect(previewSummary(page, alpha)).toHaveText(summaryText({ total: 4, new: 3, existing: 0, alreadyInList: 0, duplicate: 1, invalid: 0 }))
  // Blank lines are ignored but still count in the line numbers.
  await expect(previewRow(page, alpha, 1)).toContainText('New company')
  await expect(previewRow(page, alpha, 3)).toContainText('Globex')
  await expect(previewRow(page, alpha, 5)).toContainText(`Initech, Inc. ${id}`)
  await expect(previewRow(page, alpha, 5)).toContainText('New company')
  await expect(previewRow(page, alpha, 6)).toContainText('Duplicate in pasted text')
  await expect(previewRow(page, alpha, 6)).toContainText('row 1')
  // Previewing saved nothing.
  expect((await apiFindEmployers(request, `acme ${id}`)).length).toBe(0)

  await confirmImport(page, alpha)
  await expect(listDetail(page, alpha).getByText(resultText({ created: 3, reused: 0, skipped: 1, added: 3 }))).toBeVisible()
  const alphaCompanies = listDetail(page, alpha).getByRole('list', { name: 'Companies in this list', exact: true })
  await expect(alphaCompanies.getByRole('listitem')).toHaveCount(3)
  // The comma stayed part of the name: one company, not two.
  await expect(alphaCompanies.getByText(`Initech, Inc. ${id}`)).toBeVisible()

  // Import into the second list: one company that already exists as a canonical employer (other case, extra spaces)
  await selectListViaUi(page, beta)
  await previewImport(page, beta, [`  GLOBEX   ${id} `, `Hooli ${id}`].join('\n'))
  await expect(previewSummary(page, beta)).toHaveText(summaryText({ total: 2, new: 1, existing: 1, alreadyInList: 0, duplicate: 0, invalid: 0 }))
  await expect(previewRow(page, beta, 1)).toContainText('Existing company')
  await expect(previewRow(page, beta, 2)).toContainText('New company')
  await confirmImport(page, beta)
  await expect(listDetail(page, beta).getByText(resultText({ created: 1, reused: 1, skipped: 0, added: 2 }))).toBeVisible()
  const betaCompanies = listDetail(page, beta).getByRole('list', { name: 'Companies in this list', exact: true })
  await expect(betaCompanies.getByRole('listitem')).toHaveCount(2)
  await expect(betaCompanies.getByText(`Globex ${id}`)).toBeVisible()

  // One canonical employer is shared by both lists.
  const globex = await apiFindEmployers(request, `globex ${id}`)
  expect(globex).toHaveLength(1)

  // Search profile: both lists, explicit work modes, one discovery flag on
  await gotoSection(page, 'Search profiles')
  const form = page.getByRole('form', { name: 'New search profile', exact: true })
  await form.getByRole('combobox').selectOption({ label: candidate })
  await form.getByLabel('Profile name', { exact: true }).fill(profile)
  await form.getByRole('checkbox', { name: alpha, exact: true }).check()
  await form.getByRole('checkbox', { name: beta, exact: true }).check()
  await form.getByRole('radio', { name: 'Choose work modes for this profile', exact: true }).check()
  const allowed = form.getByRole('group', { name: 'Allowed work modes', exact: true })
  await allowed.getByRole('checkbox', { name: 'Remote', exact: true }).check()
  await allowed.getByRole('checkbox', { name: 'Hybrid', exact: true }).check()
  await form.getByRole('checkbox', { name: 'Saved-company monitoring', exact: true }).check()
  await form.getByRole('button', { name: 'Create profile', exact: true }).click()

  const card = profileCard(page, profile)
  await expect(card).toBeVisible()
  await expect(card).toContainText(alpha)
  await expect(card).toContainText(beta)
  await expect(card).toContainText('Remote, Hybrid (overrides list defaults)')

  const stored = (
    await (await request.get(`${apiUrl}/api/search-profiles`, { params: { candidateId: (await findCandidateId(request, candidate)) } })).json()
  ) as { name: string; workModes: string[] | null; companyMonitoringEnabled: boolean; broadSearchEnabled: boolean; companyLists: unknown[] }[]
  const saved = stored.find((p) => p.name === profile)
  expect(saved).toBeDefined()
  expect([...(saved?.workModes ?? [])].sort()).toEqual(['hybrid', 'remote'])
  expect(saved?.companyMonitoringEnabled).toBe(true)
  expect(saved?.broadSearchEnabled).toBe(false)
  expect(saved?.companyLists).toHaveLength(2)
})

test('a profile keeps "inherit" (null) distinct from an explicit empty set and from a chosen set', async ({ page, request }) => {
  const id = uid()
  const candidate = await apiCreateCandidate(request, `Candidate ${id}`)
  const list = await apiCreateList(request, `${id} modes`, ['hybrid'])
  const name = `Profile ${id}`
  const created = await apiCreateProfile(request, { candidateId: candidate.id, name, companyListIds: [list.id] })
  expect(created.workModes).toBeNull()

  await gotoSection(page, 'Search profiles')
  await expect(profileCard(page, name)).toContainText("Inherits each selected list's default")

  // Explicit empty set: "choose work modes" with nothing checked.
  let form = await openProfileEditor(page, name)
  await form.getByRole('radio', { name: 'Choose work modes for this profile', exact: true }).check()
  await expect(form.getByTestId('explicit-empty-note')).toBeVisible()
  await form.getByRole('button', { name: 'Save profile', exact: true }).click()
  await expect(form).toHaveCount(0)
  await expect(profileCard(page, name)).toContainText('Explicit empty set')
  expect((await apiGetProfile(request, created.id)).workModes).toEqual([])

  // A real set overrides the list default.
  form = await openProfileEditor(page, name)
  await form.getByRole('group', { name: 'Allowed work modes', exact: true }).getByRole('checkbox', { name: 'Remote', exact: true }).check()
  await form.getByRole('button', { name: 'Save profile', exact: true }).click()
  await expect(form).toHaveCount(0)
  await expect(profileCard(page, name)).toContainText('Remote (overrides list defaults)')
  expect((await apiGetProfile(request, created.id)).workModes).toEqual(['remote'])

  // Back to inherit: stored as null again, not as an empty set.
  form = await openProfileEditor(page, name)
  await form.getByRole('radio', { name: 'Inherit from the selected lists', exact: true }).check()
  await form.getByRole('button', { name: 'Save profile', exact: true }).click()
  await expect(form).toHaveCount(0)
  await expect(profileCard(page, name)).toContainText("Inherits each selected list's default")
  expect((await apiGetProfile(request, created.id)).workModes).toBeNull()
})

test('deleting: a selected list and a candidate with profiles are refused until the dependants are removed', async ({ page, request }) => {
  test.setTimeout(90_000)
  const id = uid()
  const candidate = await apiCreateCandidate(request, `Candidate ${id}`)
  const list = await apiCreateList(request, `${id} delete`)
  await apiImport(request, list.id, `Keeper ${id}`)
  const profileName = `Profile ${id}`
  const profile = await apiCreateProfile(request, { candidateId: candidate.id, name: profileName, companyListIds: [list.id] })

  // A list that a profile selects cannot be deleted.
  await gotoSection(page, 'Company lists')
  await selectListViaUi(page, list.name)
  const detail = listDetail(page, list.name)
  await detail.getByRole('button', { name: 'Delete list', exact: true }).click()
  await detail.getByRole('button', { name: 'Yes, delete this list', exact: true }).click()
  await expect(detail.getByRole('alert')).toContainText('List is in use')
  await expect(detail.getByRole('alert')).toContainText('1 search profile still select this list')
  expect((await request.get(`${apiUrl}/api/company-lists/${list.id}`)).status()).toBe(200)

  // Unselect it in the profile, then the delete works.
  await gotoSection(page, 'Search profiles')
  const form = await openProfileEditor(page, profileName)
  await form.getByRole('checkbox', { name: list.name, exact: true }).uncheck()
  await form.getByRole('button', { name: 'Save profile', exact: true }).click()
  await expect(form).toHaveCount(0)
  await expect(profileCard(page, profileName)).toContainText('None selected')

  await gotoSection(page, 'Company lists')
  await selectListViaUi(page, list.name)
  await detail.getByRole('button', { name: 'Delete list', exact: true }).click()
  await detail.getByRole('button', { name: 'Yes, delete this list', exact: true }).click()
  await expect(detail).toHaveCount(0)
  await expect(page.getByText(list.name)).toHaveCount(0)
  expect((await request.get(`${apiUrl}/api/company-lists/${list.id}`)).status()).toBe(404)
  // Deleting a list keeps its companies.
  expect((await apiFindEmployers(request, `keeper ${id}`)).length).toBe(1)

  // A candidate that still has a profile cannot be deleted.
  await gotoSection(page, 'Candidates')
  await page.getByRole('button', { name: `Delete ${candidate.name}`, exact: true }).click()
  await page.getByRole('button', { name: `Confirm delete ${candidate.name}`, exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Candidate still has search profiles')
  await expect(page.getByRole('alert')).toContainText('1 search profile still belong to this candidate')
  await expect(candidateRow(page, candidate.name)).toBeVisible()

  // Delete the profile first, then the candidate.
  await gotoSection(page, 'Search profiles')
  await page.getByRole('button', { name: `Delete ${profileName}`, exact: true }).click()
  await page.getByRole('button', { name: `Confirm delete ${profileName}`, exact: true }).click()
  await expect(page.getByText(profileName)).toHaveCount(0)
  expect((await request.get(`${apiUrl}/api/search-profiles/${profile.id}`)).status()).toBe(404)

  await gotoSection(page, 'Candidates')
  await page.getByRole('button', { name: `Delete ${candidate.name}`, exact: true }).click()
  await page.getByRole('button', { name: `Confirm delete ${candidate.name}`, exact: true }).click()
  await expect(page.getByText(candidate.name)).toHaveCount(0)
  expect((await request.get(`${apiUrl}/api/candidates/${candidate.id}`)).status()).toBe(404)
})

test('deleting: a company that is still in a list is refused until it is removed from the list', async ({ page, request }) => {
  const id = uid()
  const list = await apiCreateList(request, `${id} members`)
  await apiImport(request, list.id, `Member ${id}`)
  const employer = (await apiGetList(request, list.id)).employers[0]

  const refused = await request.delete(`${apiUrl}/api/employers/${employer.id}`)
  expect(refused.status()).toBe(409)
  expect((await refused.json()).code).toBe('employer_in_company_list')

  await gotoSection(page, 'Company lists')
  await selectListViaUi(page, list.name)
  await page.getByRole('button', { name: `Remove ${employer.name} from this list`, exact: true }).click()
  await expect(listDetail(page, list.name).getByText('No companies in this list yet.')).toBeVisible()

  expect((await request.delete(`${apiUrl}/api/employers/${employer.id}`)).status()).toBe(204)
})

test('validation and conflict feedback: blank names, duplicate names, missing candidate', async ({ page, request }) => {
  test.setTimeout(90_000)
  const id = uid()

  // Candidate: a blank name is refused in the form.
  await gotoSection(page, 'Candidates')
  await page.getByLabel('Candidate name', { exact: true }).fill('   ')
  await page.getByRole('button', { name: 'Add candidate', exact: true }).click()
  await expect(page.getByText('A name is required.')).toBeVisible()

  // Company list: blank name, then a duplicate that differs only in case and spacing.
  const list = await apiCreateList(request, `${id} dup`)
  await gotoSection(page, 'Company lists')
  await createListViaUi(page, '  ')
  await expect(page.getByText('A name is required.')).toBeVisible()
  await createListViaUi(page, `  ${list.name.toUpperCase()}  `)
  const listAlert = page.getByRole('alert')
  await expect(listAlert).toContainText('Name already in use')
  await expect(listAlert).toContainText('Another company list already uses this name')
  const all = await apiListAll<{ name: string }>(request, '/api/company-lists')
  expect(all.filter((l) => l.name.toLowerCase() === list.name.toLowerCase())).toHaveLength(1)

  // Search profile: no candidate chosen, then a duplicate name for the same candidate (allowed for another candidate).
  const candidate = await apiCreateCandidate(request, `Candidate ${id}`)
  const other = await apiCreateCandidate(request, `Other ${id}`)
  const name = `Profile ${id}`
  await apiCreateProfile(request, { candidateId: candidate.id, name })
  await gotoSection(page, 'Search profiles')
  const form = page.getByRole('form', { name: 'New search profile', exact: true })
  await form.getByLabel('Profile name', { exact: true }).fill(name)
  await form.getByRole('button', { name: 'Create profile', exact: true }).click()
  await expect(form.getByText('Choose a candidate.')).toBeVisible()

  await form.getByRole('combobox').selectOption({ label: candidate.name })
  await form.getByLabel('Profile name', { exact: true }).fill(name.toUpperCase())
  await form.getByRole('button', { name: 'Create profile', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Name already in use')
  await expect(page.getByRole('alert')).toContainText('Another search profile for this candidate already uses this name')

  await form.getByRole('combobox').selectOption({ label: other.name })
  await form.getByRole('button', { name: 'Create profile', exact: true }).click()
  await expect(profileCard(page, `for ${other.name}`)).toBeVisible()
})

test('import: empty paste, invalid row blocks the save, edits discard the preview, repeat import changes nothing', async ({ page, request }) => {
  test.setTimeout(90_000)
  const id = uid()
  const list = await apiCreateList(request, `${id} import`)
  await apiImport(request, list.id, `Existing ${id}`)

  await gotoSection(page, 'Company lists')
  await selectListViaUi(page, list.name)

  // Nothing pasted.
  await previewImport(page, list.name, '  \n \n')
  await expect(listDetail(page, list.name).getByText('Paste at least one company name, one per line.')).toBeVisible()

  // A too-long name is invalid and blocks the whole save.
  await previewImport(page, list.name, [`Existing ${id}`, `Fresh ${id}`, 'x'.repeat(201)].join('\n'))
  await expect(previewSummary(page, list.name)).toHaveText(summaryText({ total: 3, new: 1, existing: 0, alreadyInList: 1, duplicate: 0, invalid: 1 }))
  await expect(previewRow(page, list.name, 3)).toContainText('Invalid')
  await expect(importPreview(page, list.name)).toContainText('1 row is invalid, so nothing can be saved')
  await expect(listDetail(page, list.name).getByRole('button', { name: 'Confirm import', exact: true })).toBeDisabled()
  expect((await apiGetList(request, list.id)).employers).toHaveLength(1)

  // Editing the text discards the preview; the fixed text previews and saves.
  const text = [`Existing ${id}`, `Fresh ${id}`].join('\n')
  await listDetail(page, list.name).getByLabel('Companies to import').fill(text)
  await expect(importPreview(page, list.name)).toHaveCount(0)
  await listDetail(page, list.name).getByRole('button', { name: 'Preview import', exact: true }).click()
  await expect(previewSummary(page, list.name)).toHaveText(summaryText({ total: 2, new: 1, existing: 0, alreadyInList: 1, duplicate: 0, invalid: 0 }))
  await confirmImport(page, list.name)
  await expect(listDetail(page, list.name).getByText(resultText({ created: 1, reused: 0, skipped: 1, added: 1 }))).toBeVisible()
  expect((await apiGetList(request, list.id)).employers).toHaveLength(2)

  // The same import again changes nothing.
  await previewImport(page, list.name, text)
  await expect(previewSummary(page, list.name)).toHaveText(summaryText({ total: 2, new: 0, existing: 0, alreadyInList: 2, duplicate: 0, invalid: 0 }))
  await confirmImport(page, list.name)
  await expect(listDetail(page, list.name).getByText(resultText({ created: 0, reused: 0, skipped: 2, added: 0 }))).toBeVisible()
  expect((await apiGetList(request, list.id)).employers).toHaveLength(2)
})

async function findCandidateId(request: APIRequestContext, name: string): Promise<string> {
  const found = (await apiListAll<{ id: string; name: string }>(request, '/api/candidates')).find((c) => c.name === name)
  if (!found) throw new Error(`Candidate ${name} was not stored`)
  return found.id
}
