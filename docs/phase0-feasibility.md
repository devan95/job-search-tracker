# Phase 0 evidence record (closed 2026-11-14)

Historical record of what was verified before Phase 1 started. Phase 0 was closed by the user on 2026-11-14. Roadmap and status live in Notion (Roadmap page, Task Board). "User-reported" means the user ran it and told the AI; the AI did not see it run.

## Outcome

| Item | Status | Evidence |
| --- | --- | --- |
| Baseline: API, Worker, PostgreSQL, web app, Docker Compose, health check | VERIFIED (user-reported) | Build and tests passed; `docker compose up` worked; `/api/health/ready` returned status ok, database ok. |
| GitHub Actions CI | VERIFIED | Green on `main` (backend, frontend, secret-scan). The earlier failure was `.gitignore` `data/` matching `Data/` on Windows; fixed by anchoring it to `/data/`. |
| Greenhouse public board API | VERIFIED (user-reported) | GitLab: HTTP 200 JSON, 251 jobs, `meta.total` 251. |
| Lever public postings API | VERIFIED (user-reported) | Meesho: HTTP 200 JSON, 49 postings; `skip`/`limit` advanced (5 + 5 distinct ids). |
| Ashby public job-board API | VERIFIED (user-reported) | OP Labs: HTTP 200 JSON, 5 jobs. |
| Live ATS sample fixtures | VERIFIED (user-reported), limited | One company per ATS, 3 records each, descriptions excluded, kept outside Git. |
| Adzuna, Jooble (India coverage) | DEFERRED 2026-11-14 | Evaluate in Phase 2; needs the user's keys; connectors stay disabled. |
| Gmail IMAP read-only | DEFERRED 2026-11-14 | Evaluate in Phase 4; needs a mailbox and app password. |
| OmniRoute / AI gateway | NOT APPROVED | No AI provider or gateway is approved (Notion: AI & Privacy Guardrails). |
| NAS prerequisites | DEFERRED 2026-11-14 | Windows first; validate before choosing the NAS. |
| Branch protection on `main` | DEFERRED 2026-11-14 | Not enforced; do not claim it is. |
| Freehire | NOT ADOPTED 2026-11-14 | Never run; do not reopen. |

A status is VERIFIED only with a recorded run, screenshot or user report. DEFERRED and NOT ADOPTED need a date and reason.

## Live ATS evidence (user-reported, 2026-11-14)

Public, unauthenticated endpoints run on the user's Windows PC. The AI could not reach these hosts, so this is not independently re-run.

| ATS | Endpoint | Fields seen |
| --- | --- | --- |
| Greenhouse | `https://boards-api.greenhouse.io/v1/boards/gitlab/jobs?content=true` | id, title, location, departments, offices, absolute_url, requisition_id, first_published, updated_at |
| Lever | `https://api.lever.co/v0/postings/meesho?mode=json` | id, text, categories, country, workplaceType, hostedUrl, applyUrl, createdAt |
| Ashby | `https://api.ashbyhq.com/posting-api/job-board/oplabs` | id, title, department, team, employmentType, location, secondaryLocations, publishedAt, isRemote, workplaceType, jobUrl, applyUrl |

Not covered (Phase 2 work, none of it blocked Phase 1): fixtures are not in the repository (commit trimmed samples only after reviewing each source's data terms); description fields were excluded, so their parsing is untested; pagination was tested only for Lever; no empty, closed, malformed or oversized payload cases; no automated connector tests; only Meesho is India-relevant. Unresolved: the hosted Greenhouse page showed about 48 postings while the API reported 251 (likely truncated page text; not checked).

## Career pages supplied 2026-11-14

None used Greenhouse, Lever or Ashby (Workable, in-house pages, probably Zoho Recruit, a WordPress page). Assessed from page text only. They can be tracked by manual entry or Chrome capture.
