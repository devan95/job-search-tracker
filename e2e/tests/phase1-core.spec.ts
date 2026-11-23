import { expect, test } from '@playwright/test'
import {
  addCandidateViaUi,
  apiCreateCandidate,
  apiFindEmployers,
  apiUrl,
  confirmImport,
  createListViaUi,
  previewImport,
  previewSummary,
  requireStack,
  resultText,
  selectListViaUi,
  summaryText,
  listDetail,
  profileCard,
  uid,
} from './support.js'
import {
  addLocationViaUi,
  addRoleTitleViaUi,
  apiCreateApplication,
  apiCreateJob,
  apiFindCandidate,
  apiFindJobs,
  apiFindLocationSet,
  apiFindProfile,
  apiFindRoleList,
  apiGetApplication,
  apiGetJob,
  apiListApplications,
  applicationHistory,
  applicationPanel,
  chooseCandidateViaUi,
  createLocationSetViaUi,
  createRoleListViaUi,
  fillJobForm,
  gotoTab,
  jobDetail,
  jobForm,
  localDateTime,
  openAddJobForm,
  openJobViaUi,
} from './phase1-support.js'

// Phase 1 acceptance (Order 9): the whole manual core, browser -> API -> PostgreSQL, synthetic data only. The behaviour
// under test is always driven through the UI; the API is used for set-up and to read back what was stored. Every name
// carries a random suffix, so the tests are independent of each other and of earlier runs. Nothing here touches an
// external job source, Gmail or AI, and no application is marked applied except by the explicit confirmation step.

test.beforeAll(async ({ playwright }) => {
  await requireStack({ playwright })
})

test('Phase 1 journey: setup, import, job, manual application, status history', async ({ page, request }) => {
  test.setTimeout(180_000)
  const id = uid()
  const candidate = `Candidate ${id}`
  const companies = `${id} companies`
  const roles = `${id} roles`
  const places = `${id} places`
  const profile = `Profile ${id}`
  const company = `Globex ${id}`
  const title = `Backend Engineer ${id}`
  const applyUrl = `https://jobs.example.test/${id}/backend`

  // 1. Candidate
  await gotoTab(page, 'Candidates')
  await addCandidateViaUi(page, candidate)

  // 1. Company list, imported through the preview and confirm flow (criterion 3)
  await gotoTab(page, 'Company lists')
  await createListViaUi(page, companies)
  await selectListViaUi(page, companies)
  await previewImport(page, companies, [company, `Initech ${id}`].join('\n'))
  await expect(previewSummary(page, companies)).toHaveText(
    summaryText({ total: 2, new: 2, existing: 0, alreadyInList: 0, duplicate: 0, invalid: 0 }),
  )
  expect((await apiFindEmployers(request, `globex ${id}`)).length, 'a preview saves nothing').toBe(0)
  await confirmImport(page, companies)
  await expect(
    listDetail(page, companies).getByText(resultText({ created: 2, reused: 0, skipped: 0, added: 2 })),
  ).toBeVisible()
  const employers = await apiFindEmployers(request, `globex ${id}`)
  expect(employers).toHaveLength(1)

  // 1. Role list: a primary title with an equivalent title
  await gotoTab(page, 'Role lists')
  await createRoleListViaUi(page, roles)
  await addRoleTitleViaUi(page, roles, `Software Engineer ${id}`, [`Backend Engineer ${id}`])
  const storedRoles = await apiFindRoleList(request, roles)
  expect(storedRoles.titles).toHaveLength(1)
  expect(storedRoles.titles[0]?.aliases.map((a) => a.alias)).toEqual([`Backend Engineer ${id}`])

  // 1. Location set: a city, remote within one country and remote worldwide are three different scopes
  await gotoTab(page, 'Location sets')
  await createLocationSetViaUi(page, places)
  await addLocationViaUi(page, places, { label: 'Ahmedabad', scope: 'City' }, 'Ahmedabad · City')
  await addLocationViaUi(
    page,
    places,
    { label: 'Remote India', scope: 'Remote within a country', countryCode: 'in' },
    'Remote India · Remote within a country · IN',
  )
  await addLocationViaUi(page, places, { label: 'Remote anywhere', scope: 'Remote worldwide' }, 'Remote anywhere · Remote worldwide')
  const storedPlaces = await apiFindLocationSet(request, places)
  expect(storedPlaces.locations.map((l) => [l.label, l.scope, l.countryCode])).toEqual(
    expect.arrayContaining([
      ['Ahmedabad', 'city', null],
      ['Remote India', 'remoteCountry', 'IN'],
      ['Remote anywhere', 'remoteWorldwide', null],
    ]),
  )

  // 2. Search profile: company list + role list + location set, with a work mode that is separate from the remote scopes
  await gotoTab(page, 'Search profiles')
  const form = page.getByRole('form', { name: 'New search profile', exact: true })
  await form.getByRole('combobox').selectOption({ label: candidate })
  await form.getByLabel('Profile name', { exact: true }).fill(profile)
  await form.getByRole('checkbox', { name: companies, exact: true }).check()
  await form.getByRole('checkbox', { name: roles, exact: true }).check()
  await form.getByRole('checkbox', { name: places, exact: true }).check()
  await form.getByRole('radio', { name: 'Choose work modes for this profile', exact: true }).check()
  await form
    .getByRole('group', { name: 'Allowed work modes', exact: true })
    .getByRole('checkbox', { name: 'Hybrid', exact: true })
    .check()
  await form.getByRole('button', { name: 'Create profile', exact: true }).click()
  const card = profileCard(page, profile)
  await expect(card).toContainText(companies)
  await expect(card).toContainText(roles)
  await expect(card).toContainText(places)
  await expect(card).toContainText('Hybrid (overrides list defaults)')
  const candidateId = (await apiFindCandidate(request, candidate)).id
  const storedProfile = await apiFindProfile(request, candidateId, profile)
  expect(storedProfile.workModes).toEqual(['hybrid'])
  expect(storedProfile.companyLists.map((l) => l.name)).toEqual([companies])
  expect(storedProfile.roleLists.map((l) => l.name)).toEqual([roles])
  expect(storedProfile.locationSets.map((l) => l.name)).toEqual([places])
  // The remote scopes live in the location set and are untouched by the profile's work mode.
  expect((await apiFindLocationSet(request, places)).locations.map((l) => l.scope).sort()).toEqual([
    'city',
    'remoteCountry',
    'remoteWorldwide',
  ])

  // 4. Add a job by hand; the typed company differs in case and spacing from the imported one and must reuse it
  await gotoTab(page, 'Jobs')
  await openAddJobForm(page)
  await fillJobForm(page, {
    company: `  GLOBEX   ${id} `,
    title,
    applyUrl,
    location: 'Ahmedabad',
    workMode: 'Remote',
    requisitionId: `REQ-${id}`,
    foundOn: 'Company site',
  })
  await jobForm(page).getByRole('button', { name: 'Save job', exact: true }).click()
  const detail = jobDetail(page, title, company)
  await expect(detail).toBeVisible()
  await expect(detail.getByTestId('job-status')).toHaveText('Open')
  await expect(detail).toContainText('By hand')

  const [created] = await apiFindJobs(request, title.toLowerCase())
  expect(created).toBeDefined()
  const jobId = created?.id ?? ''
  const stored = await apiGetJob(request, jobId)
  expect(stored.employer.id, 'the typed company reuses the canonical employer').toBe(employers[0]?.id)
  expect(stored.createdVia).toBe('manual')
  expect(stored.workMode).toBe('remote')
  expect(stored.status).toBe('open')
  expect((await apiFindEmployers(request, `globex ${id}`)).length).toBe(1)

  // 4. Retrieve it again from the list, then update it
  await gotoTab(page, 'Jobs')
  await openJobViaUi(page, title, title, company)
  await expect(jobDetail(page, title, company)).toContainText('Ahmedabad')
  await page.getByRole('button', { name: 'Edit job', exact: true }).click()
  await fillJobForm(page, { location: 'Pune', workMode: 'Hybrid' })
  await jobForm(page).getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(jobForm(page)).toHaveCount(0)
  await expect(jobDetail(page, title, company)).toContainText('Pune')
  await expect(jobDetail(page, title, company)).toContainText('Hybrid')
  const updated = await apiGetJob(request, jobId)
  expect([updated.location, updated.workMode]).toEqual(['Pune', 'hybrid'])

  // 5. Track an application: it starts Saved and is NOT applied
  await chooseCandidateViaUi(page, candidate)
  await page.getByRole('button', { name: 'Track application', exact: true }).click()
  const panel = applicationPanel(page)
  await expect(panel).toBeVisible()
  await expect(panel.getByTestId('application-status')).toHaveText('Saved')
  await expect(panel.getByTestId('application-applied')).toHaveText('No')
  const tracked = await apiListApplications(request, { candidateId, jobId })
  expect(tracked).toHaveLength(1)
  const applicationId = tracked[0]?.id ?? ''
  expect(await apiGetApplication(request, applicationId)).toMatchObject({ status: 'saved', isApplied: false, method: null, appliedAtUtc: null })

  // 5. Manual confirmation: Applied, method Manual, date and time recorded
  await panel.getByRole('button', { name: 'Confirm as applied', exact: true }).click()
  const confirm = page.getByRole('form', { name: 'Confirm application', exact: true })
  await expect(confirm.getByRole('button', { name: 'Confirm applied', exact: true })).toBeDisabled()
  await confirm.getByRole('checkbox', { name: 'I submitted this application myself.', exact: true }).check()
  await confirm.getByRole('button', { name: 'Confirm applied', exact: true }).click()
  await expect(panel.getByTestId('application-applied')).toHaveText('Yes')
  await expect(panel.getByTestId('application-status')).toHaveText('Applied')
  await expect(panel.locator('dd', { hasText: /^Manual$/ })).toBeVisible()
  const applied = await apiGetApplication(request, applicationId)
  expect(applied).toMatchObject({ status: 'applied', isApplied: true, method: 'manual' })
  expect(applied.appliedAtUtc).not.toBeNull()
  expect(Math.abs(Date.now() - Date.parse(applied.appliedAtUtc ?? '')), 'applied now, not at some other time').toBeLessThan(5 * 60_000)

  // 6. A later status, and the audit history that records every step
  const statusForm = page.getByRole('form', { name: 'Change status', exact: true })
  await statusForm.getByLabel('Change status to', { exact: true }).selectOption({ label: 'Interview' })
  await statusForm.getByLabel('Reason (optional)', { exact: true }).fill('Recruiter call booked')
  await statusForm.getByRole('button', { name: 'Update status', exact: true }).click()
  await expect(panel.getByTestId('application-status')).toHaveText('Interview')
  await expect(panel.getByTestId('application-applied')).toHaveText('Yes')

  const history = applicationHistory(page).getByRole('listitem')
  await expect(history).toHaveCount(3)
  await expect(history.nth(0)).toContainText('Saved for tracking')
  await expect(history.nth(1)).toContainText('Confirmed as applied')
  await expect(history.nth(1)).toContainText('Status: Saved → Applied')
  await expect(history.nth(1)).toContainText('Applied: No → Yes')
  await expect(history.nth(2)).toContainText('Status changed')
  await expect(history.nth(2)).toContainText('Status: Applied → Interview')
  await expect(history.nth(2)).toContainText('Reason: Recruiter call booked')

  const final = await apiGetApplication(request, applicationId)
  expect(final.auditTrail.map((e) => [e.action, e.oldStatus, e.newStatus, e.newIsApplied])).toEqual([
    ['created', null, 'saved', false],
    ['appliedConfirmed', 'saved', 'applied', true],
    ['statusChanged', 'applied', 'interview', true],
  ])
  expect(final.auditTrail.every((e) => e.actor === 'user')).toBe(true)

  // Job lifecycle: closing and reopening the job never changes the application
  await page.getByRole('button', { name: 'Mark closed', exact: true }).click()
  await expect(jobDetail(page, title, company).getByTestId('job-status')).toHaveText('Closed')
  await page.getByRole('button', { name: 'Open again', exact: true }).click()
  await expect(jobDetail(page, title, company).getByTestId('job-status')).toHaveText('Open again')
  const lifecycle = await apiGetJob(request, jobId)
  expect(lifecycle.statusHistory.map((h) => [h.fromStatus, h.toStatus])).toEqual([
    [null, 'open'],
    ['open', 'closed'],
    ['closed', 'reopened'],
  ])
  expect(await apiGetApplication(request, applicationId)).toMatchObject({ status: 'interview', isApplied: true })

  // The Applications tab shows the same application
  await gotoTab(page, 'Applications')
  await page.getByLabel('Candidate filter', { exact: true }).selectOption({ label: candidate })
  const row = page.getByRole('button', { name: `Open application: ${title} at ${company} for ${candidate}`, exact: true })
  await expect(row).toContainText('Interview')
  await expect(row).toContainText('Applied (manual)')
  await row.click()
  await expect(applicationPanel(page).getByTestId('application-status')).toHaveText('Interview')
})

test('duplicate and repost protection: one application per candidate and canonical job', async ({ page, request }) => {
  test.setTimeout(120_000)
  const id = uid()
  const company = `Repost ${id}`
  const title = `Data Engineer ${id}`
  const candidate = await apiCreateCandidate(request, `Candidate ${id}`)
  const original = await apiCreateJob(request, {
    employer: company,
    title,
    applyUrl: `https://jobs.example.test/${id}/original`,
    location: 'Ahmedabad',
    requisitionId: `REQ-${id}`,
  })
  const application = await apiCreateApplication(request, candidate.id, original.id)
  expect(application).toMatchObject({ status: 'saved', isApplied: false })

  // The same requisition id at the same company is a repost: blocked, with no way to override it.
  await gotoTab(page, 'Jobs')
  await openAddJobForm(page)
  await fillJobForm(page, {
    company: company.toUpperCase(),
    title: `${title} (reposted)`,
    applyUrl: `https://jobs.example.test/${id}/repost`,
    location: 'Ahmedabad',
    requisitionId: `REQ-${id}`,
  })
  await jobForm(page).getByRole('button', { name: 'Save job', exact: true }).click()
  const alert = jobForm(page).getByRole('alert')
  await expect(alert).toContainText('Same requisition id')
  await expect(page.getByRole('button', { name: 'This is a different job: save anyway', exact: true })).toHaveCount(0)
  expect(await apiFindJobs(request, id), 'the repost was not saved').toHaveLength(1)

  // The existing job opens from the warning, and its candidate already has the application: no second one is offered.
  await page.getByRole('button', { name: 'Open the existing job', exact: true }).click()
  await expect(jobDetail(page, title, company)).toBeVisible()
  await chooseCandidateViaUi(page, candidate.name)
  await expect(page.getByText('already has an application for this job')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Track application', exact: true })).toHaveCount(0)
  await expect(applicationPanel(page).getByTestId('application-status')).toHaveText('Saved')

  // The API refuses a second application for the same candidate and job, and nothing was added.
  const second = await request.post(`${apiUrl}/api/applications`, { data: { candidateId: candidate.id, jobId: original.id } })
  expect(second.status()).toBe(409)
  expect(((await second.json()) as { code: string }).code).toBe('application_exists')
  expect(await apiListApplications(request, { candidateId: candidate.id, jobId: original.id })).toHaveLength(1)

  // The same apply link without a requisition id is only a warning that the user may override as a different job.
  await gotoTab(page, 'Jobs')
  await openAddJobForm(page)
  await fillJobForm(page, { company, title: `${title} (second)`, applyUrl: `https://jobs.example.test/${id}/original` })
  await jobForm(page).getByRole('button', { name: 'Save job', exact: true }).click()
  await expect(jobForm(page).getByRole('alert')).toContainText('Possible duplicate job')
  await expect(page.getByRole('button', { name: 'Open the existing job', exact: true })).toBeVisible()
  expect(await apiFindJobs(request, id), 'a warning saves nothing').toHaveLength(1)
  await page.getByRole('button', { name: 'This is a different job: save anyway', exact: true }).click()
  await expect(jobDetail(page, `${title} (second)`, company)).toBeVisible()
  const jobs = await apiFindJobs(request, id)
  expect(jobs).toHaveLength(2)

  // A different canonical job is a different application: the candidate has none for it yet.
  await chooseCandidateViaUi(page, candidate.name)
  await expect(page.getByText(`No application recorded for ${candidate.name} and this job yet.`)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Track application', exact: true })).toBeVisible()
  expect(await apiListApplications(request, { candidateId: candidate.id })).toHaveLength(1)
})

test('validation and error states: job form, confirmation, corrections, stale status, deletes', async ({ page, request }) => {
  test.setTimeout(150_000)
  const id = uid()
  const company = `Validation ${id}`
  const title = `Analyst ${id}`
  const candidate = await apiCreateCandidate(request, `Candidate ${id}`)
  const job = await apiCreateJob(request, { employer: company, title, requisitionId: `REQ-${id}` })
  const application = await apiCreateApplication(request, candidate.id, job.id)

  // Job form: the field errors are shown beside the fields and nothing is saved.
  await gotoTab(page, 'Jobs')
  await openAddJobForm(page)
  const form = jobForm(page)
  await form.getByRole('button', { name: 'Save job', exact: true }).click()
  await expect(form).toContainText('A company name is required.')
  await expect(form).toContainText('A title is required.')
  await fillJobForm(page, { company: `Rejected ${id}`, title: `Rejected ${id}`, applyUrl: 'not-a-link' })
  await form.getByLabel('Minimum years of experience', { exact: true }).fill('10')
  await form.getByLabel('Maximum years of experience', { exact: true }).fill('5')
  await form.getByRole('button', { name: 'Save job', exact: true }).click()
  await expect(form).toContainText('Enter a full link starting with http:// or https://.')
  await expect(form).toContainText('The maximum cannot be less than the minimum.')
  expect(await apiFindJobs(request, `rejected ${id}`), 'a rejected form saves nothing').toHaveLength(0)

  // The API enforces the same rules on its own.
  const bad = await request.post(`${apiUrl}/api/jobs`, { data: { employer: '  ', title: '' } })
  expect(bad.status()).toBe(400)
  const problem = (await bad.json()) as { code: string; errors: Record<string, string[]> }
  expect(problem.code).toBe('validation')
  expect(Object.keys(problem.errors).map((k) => k.toLowerCase())).toEqual(expect.arrayContaining(['employer', 'title']))

  // Confirming an application needs the tick, and a future date is refused. Nothing becomes applied.
  await gotoTab(page, 'Applications')
  await page.getByLabel('Candidate filter', { exact: true }).selectOption({ label: candidate.name })
  await page.getByRole('button', { name: `Open application: ${title} at ${company} for ${candidate.name}`, exact: true }).click()
  const panel = applicationPanel(page)
  await expect(panel.getByTestId('application-applied')).toHaveText('No')
  await panel.getByRole('button', { name: 'Confirm as applied', exact: true }).click()
  const confirm = page.getByRole('form', { name: 'Confirm application', exact: true })
  await expect(confirm.getByRole('button', { name: 'Confirm applied', exact: true })).toBeDisabled()
  await confirm.getByRole('checkbox', { name: 'I submitted this application myself.', exact: true }).check()
  await confirm.getByLabel(/^Date and time applied/).fill(localDateTime(2 * 24 * 60 * 60_000))
  await confirm.getByRole('button', { name: 'Confirm applied', exact: true }).click()
  await expect(confirm).toContainText('The date and time applied cannot be in the future.')
  await expect(panel.getByTestId('application-applied')).toHaveText('No')
  await confirm.getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(await apiGetApplication(request, application.id)).toMatchObject({ status: 'saved', isApplied: false })

  // A correction needs a reason, and cannot make an application applied.
  await panel.getByRole('button', { name: 'Correct', exact: true }).click()
  const correct = page.getByRole('form', { name: 'Correct application', exact: true })
  await expect(correct.getByLabel('New status', { exact: true }).locator('option', { hasText: /^Applied$/ })).toHaveCount(0)
  await correct.getByLabel('New status', { exact: true }).selectOption({ label: 'Withdrawn' })
  await correct.getByRole('button', { name: 'Save correction', exact: true }).click()
  await expect(correct).toContainText('A short reason is required.')
  expect((await apiGetApplication(request, application.id)).status).toBe('saved')
  await correct.getByRole('button', { name: 'Cancel', exact: true }).click()

  // A stale screen: the application is withdrawn elsewhere, then this screen asks for a change that is no longer allowed.
  const withdraw = await request.post(`${apiUrl}/api/applications/${application.id}/status`, { data: { status: 'withdrawn' } })
  expect(withdraw.status()).toBe(200)
  const statusForm = page.getByRole('form', { name: 'Change status', exact: true })
  await statusForm.getByLabel('Change status to', { exact: true }).selectOption({ label: 'Pending verification' })
  await statusForm.getByRole('button', { name: 'Update status', exact: true }).click()
  await expect(panel.getByRole('alert')).toContainText('Status change not allowed')
  const afterStale = await apiGetApplication(request, application.id)
  expect(afterStale.status).toBe('withdrawn')
  expect(afterStale.isApplied).toBe(false)
  expect(afterStale.auditTrail.map((e) => e.newStatus)).toEqual(['saved', 'withdrawn'])

  // A job that an application refers to cannot be deleted.
  await gotoTab(page, 'Jobs')
  await openJobViaUi(page, title, title, company)
  await page.getByRole('button', { name: 'Delete job', exact: true }).click()
  await page.getByRole('button', { name: 'Yes, delete this job', exact: true }).click()
  await expect(jobDetail(page, title, company).getByRole('alert')).toContainText('Job is in use')
  expect((await apiGetJob(request, job.id)).id).toBe(job.id)
})

test('an unreachable API is reported to the user instead of an empty list', async ({ page }) => {
  await page.goto('/')
  await page.route(/\/api\/jobs/, (route) => route.abort())
  await page.getByRole('tab', { name: 'Jobs', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Cannot reach the API')
  await expect(page.getByText('No jobs yet.')).toHaveCount(0)
})
