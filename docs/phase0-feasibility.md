# Phase 0 feasibility matrix and acceptance gate

Last updated: 2026-11-14. Execution list: the Notion **Task Board**, phases 0-6. The phase list on the Notion Scope page
(0-5) is roadmap reference only.

Status vocabulary:

| Status | Meaning |
| --- | --- |
| **VERIFIED** | A real run produced evidence, recorded in the Evidence column. "User-reported" means the user ran it and told the AI; the AI did not see it run. |
| **FAILED** | Run and failed. Record the cause and the fix task. |
| **NOT RUN** | Not attempted yet. |
| **BLOCKED** | Cannot proceed without something outside the AI's control (credentials, a target list, network access). |
| **DEFERRED** | The user explicitly postponed it. Record the date and reason. The connector stays disabled. |
| **NOT ADOPTED** | Decided against by the user. Kept only as a record. |

Nothing is VERIFIED without evidence. Unverified connectors stay disabled or behind interfaces and never block the manual
core application. Core tracking must work without AI, Gmail or any single job source.

## Capability matrix

| Item | Status | Evidence / next step |
| --- | --- | --- |
| Repository baseline: API, Worker, PostgreSQL, web app, Docker Compose, health check | VERIFIED (user-reported) | `dotnet build` and `dotnet test` passed (4 tests). `docker compose build --no-cache` and `up` worked; API on `0.0.0.0:8080`; `/api/health/ready` returned status ok, database ok. |
| GitHub Actions CI | VERIFIED (user-supplied screenshot) | Run #5 "Fixed CI", `main`, commit `9ba4ffc`, success: backend, frontend, secret-scan all passed (42s). Run URL reported by the user: https://github.com/devan95/job-search-tracker/actions/runs/43575054796. The AI later read the run list directly: the latest run on `main` (run 43587009809, "Docs updated", commit `180afff`, 2026-11-14) also succeeded in 37s. Earlier run #4 failed because `Api/Data/` was never committed (`.gitignore` `data/` matched `Data/` on Windows); fixed by anchoring the rule to `/data/`. |
| Branch protection and required checks on `main` | DEFERRED (2026-11-14) | User decision: not needed now. Protection is **not enforced**; do not claim it is. The Work Protocol still asks for feature branches. Revisit before other contributors or before Phase 2. |
| Greenhouse public job-board API | VERIFIED (user-reported) | GitLab board, `boards-api.greenhouse.io`: HTTP 200, JSON, 251 jobs, `meta.total` 251. See "Live ATS evidence" below. |
| Lever public postings API | VERIFIED (user-reported) | Meesho, `api.lever.co`: HTTP 200, JSON, 49 postings; `skip`/`limit` pagination tested (5 + 5 distinct IDs). See below. |
| Ashby public job-board API | VERIFIED (user-reported) | OP Labs, `api.ashbyhq.com`: HTTP 200, JSON, 5 jobs. See below. |
| Live ATS fixtures for a real company per adapter | VERIFIED (user-reported), with limits | One real company per ATS retrieved, parsed and trimmed to 3-record sample fixtures kept outside Git. Limits (fixtures not in the repo, descriptions excluded, tests deferred to Phase 2) are listed below. The AI could not run these calls itself and has not seen the files. |
| Adzuna API, India coverage | DEFERRED (2026-11-14) | User decision: evaluate when job-source integration testing begins (Phase 2). Needs the user's API credentials; India support unconfirmed. Connector stays disabled. |
| Jooble API, India coverage | DEFERRED (2026-11-14) | User decision: evaluate when job-source integration testing begins (Phase 2). Needs the user's API key; keys are per country (free tier reported as 100 requests per key per country per day). India support unconfirmed. Connector stays disabled. |
| Gmail IMAP read-only authentication | DEFERRED (2026-11-14) | User decision: evaluate when email integration testing begins. Needs the user's mailbox and an app password. Connector stays disabled until proven; read-only access only. |
| OmniRoute route, model and cost visibility | DEFERRED (2026-11-14) | User decision: not needed now. Optional; owned by the AI Gateway & Privacy Setup page. Core tracking must work without it. |
| Windows prerequisites (.NET 10, Docker, Node) | VERIFIED (user-reported, partly) | .NET build/test and Docker verified by the user. Local Node/npm not separately reported; the frontend job passes in CI. |
| NAS prerequisites (CPU, RAM, disk, Docker) | DEFERRED (2026-11-14) | User decision: not needed now; Windows first. Validate before choosing the NAS as the host. |
| Freehire India/API proof of concept | NOT ADOPTED | User decision 2026-11-14. Never run. Not used. Do not reopen. Discovery uses verified Greenhouse, Lever and Ashby adapters, company lists, manual entry, Chrome capture and Gmail alerts. |

### Live ATS evidence (user-reported, 2026-11-14)

Run by the user on their Windows PC against public, unauthenticated endpoints. No credentials were involved. The AI could not
reach these hosts from its workspace (proxy 403, endpoints refused by the session allowlist), so this is **user-reported**, not
independently re-run. Raw responses and fixture files were not shared with the AI.

| ATS / company | Endpoint | Result | Fixture (outside Git) | Fields seen in the sample record |
| --- | --- | --- | --- | --- |
| Greenhouse / GitLab | `https://boards-api.greenhouse.io/v1/boards/gitlab/jobs?content=true` | HTTP 200, `application/json`, 3,904,961 bytes, parsed; 251 jobs, `meta.total` = 251 | `greenhouse.json`: 3 records, 4,160 bytes | id, title, location, departments, offices, absolute_url, requisition_id, first_published, updated_at |
| Lever / Meesho | `https://api.lever.co/v0/postings/meesho?mode=json` | HTTP 200, `application/json`, 827,658 bytes, parsed; 49 postings; pagination: `skip=0` returned 5, `skip=5` returned 5, all 10 IDs distinct | `lever.json`: 3 records, 2,649 bytes | id, text, categories, country, workplaceType, hostedUrl, applyUrl, createdAt |
| Ashby / OP Labs | `https://api.ashbyhq.com/posting-api/job-board/oplabs` | HTTP 200, `application/json`, 81,896 bytes, parsed; 5 jobs | `ashby.json`: 3 records, 2,763 bytes | id, title, department, team, employmentType, location, secondaryLocations, publishedAt, isRemote, workplaceType, jobUrl, applyUrl |

All three fixture files were re-read and parsed by the user: exactly 3 records each, description and HTML description fields
excluded, 9,572 bytes in total, stored under `%USERPROFILE%\ats-fixtures-raw\fixtures` (outside the repository).

Consistency review by the AI (not a re-run):
- Each endpoint is on the ATS vendor's own API host and returns a schema native to that ATS (for example Greenhouse
  `meta.total` and `requisition_id`; Lever `hostedUrl`, `applyUrl` and `workplaceType`; Ashby `jobUrl` and `isRemote`).
- Lever's 49 postings matches the roughly 49 listings the AI saw on the hosted `jobs.lever.co/meesho` page the same day.
- Ashby's hosted page returned no listings to the AI (JavaScript shell); the API returning 5 jobs is consistent with that.
- Unresolved observation: the hosted Greenhouse page text showed about 48 postings to the AI, while the API reports 251. This
  is most likely the page content being truncated or paginated, but it has not been checked.

What this does **not** cover (remaining work, none of it blocks Phase 1):
- The fixture files are not in the repository. Commit only trimmed samples, and only after the job-data terms of each source are
  reviewed. Do not commit bulk dumps.
- Descriptions and HTML content fields were excluded, so parsing of content fields is untested. Add a small trimmed sample with
  a description to the Phase 2 fixtures.
- Pagination was tested only for Lever, and only that `skip` advances. Greenhouse returned all 251 jobs in one response and
  Ashby returned 5, so no pagination was needed for either at this size.
- No empty, closed/removed, malformed or oversized payload cases exist. These are Phase 2 tests and use synthetic fixtures.
- No automated integration test exists yet. Connector contract tests (recorded fixtures plus live smoke tests, per the Testing
  Plan) belong to Phase 2.
- One India-relevant board is covered (Meesho). GitLab and OP Labs are mostly remote or global.

### Career pages supplied on 2026-11-14

Assessed from page text only (not raw HTML or network traces). Treat as inferences, not verified findings.

| Page | Observation | Usable for an ATS fixture? |
| --- | --- | --- |
| apply.workable.com/civica-uk-ltd-1 | Workable-hosted. Not fetchable from the AI workspace. Workable is not one of the three planned adapters. | No |
| radixweb.com/current-openings | Looks like an in-house page; no Greenhouse, Lever or Ashby links seen. | No |
| ethicsinfotech.in/career | Looks like an in-house page; apply links stay on the company domain. | No |
| careers.restroworks.com/jobs/careers | Likely Zoho Recruit (inferred from URL pattern only). | No |
| theroyalpos.com/blog/careers | Plain WordPress page; applications via a WhatsApp link. | No |

These can still be tracked through manual entry or Chrome capture. They are not evidence for the three planned ATS adapters.

## Phase 0 acceptance criteria (gate to start Phase 1)

Phase 0 is **green** only when the user confirms it, and all of the following hold:

1. **Baseline built and running:** CI is green on `main` (backend, frontend, secret-scan) and the local Docker stack reports
   `/api/health/ready` ok. *Met, per the evidence above.*
2. **This document is on `main`** with the matrix and criteria. *Met once this change is pushed.*
3. **Every matrix row has an honest status.** Each item is either VERIFIED with evidence, or recorded as DEFERRED
   (date and reason) or NOT ADOPTED. Rows left NOT RUN or BLOCKED must be consciously resolved by the user. Unverified
   connectors remain disabled or behind interfaces.
4. **No claim without evidence.** No row says VERIFIED without a run, a screenshot or a user report recorded here.

The user may defer these without blocking Phase 1, because the manual core application does not depend on them: Adzuna,
Jooble, Gmail, OmniRoute, NAS prerequisites and branch protection. (Live ATS fixtures were verified on 2026-11-14 and need no
deferral.) Deferral must be explicit and recorded in this file.

### Gate status (2026-11-14): Phase 0 CLOSED

| Criterion | Status |
| --- | --- |
| 1. Baseline and CI | Met. CI green on `main`; local Docker stack reported `/api/health/ready` ok (user-reported). |
| 2. This document on `main` | Met (matrix and criteria on `main`; the deferral decisions are recorded by the change that adds this section). |
| 3. All rows resolved (verified, deferred or not adopted) | Met. VERIFIED: baseline, CI, Greenhouse, Lever, Ashby, live ATS fixtures, Windows prerequisites. DEFERRED with date and reason: Adzuna, Jooble, Gmail, OmniRoute, NAS, branch protection. NOT ADOPTED: Freehire. |
| 4. Evidence recorded | Met for every VERIFIED row. ATS and Docker results are user-reported; the AI did not see them run. |

**Phase 0 is closed** on the user's confirmation, 2026-11-14. Phase 1 (data model: candidate, company list, search profile, first
EF Core migration) may start.

Open items that do not block Phase 1:
- Unresolved observation: the hosted Greenhouse page showed about 48 postings while the API reported 251 (see above).
- Deferred rows above stay disabled. Each is evaluated in the phase named in its row.
- Branch protection is not enforced; direct pushes to `main` are possible.
