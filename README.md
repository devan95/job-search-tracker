# Job Search & Application Tracker

A generic, local-first application for discovering relevant jobs from saved company lists and enabled sources, consolidating vacancies into canonical job records, and tracking applications consistently.

The application and its data run locally (Windows PC first; home NAS later).

## Repository contents

| Area | Location |
|---|---|
| ASP.NET Core API (.NET 10) | `src/JobSearchTracker.Api/` |
| Background worker | `src/JobSearchTracker.Worker/` |
| PostgreSQL / EF Core model and migrations | `src/JobSearchTracker.Api/Data/` |
| xUnit tests | `tests/JobSearchTracker.Api.Tests/` |
| React + TypeScript + Vite frontend | `web/` |
| GitHub Actions CI | `.github/workflows/ci.yml` |
| API contract | `docs/api.md` |
| Data model and migration notes | `docs/data-model.md` |
| Local development and test instructions | `docs/development.md` |
| Phase 0 evidence record | `docs/phase0-feasibility.md` |

## Current implementation

The repository currently contains the API health endpoints and management endpoints for candidates, employers, company lists and search profiles; the initial PostgreSQL/EF Core model and migration; a worker heartbeat placeholder; and a frontend system-status page.

Company-list import, job discovery, canonical job tracking, application tracking, Gmail alert ingestion and AI features are not yet implemented. Check the Notion Task Board for authoritative task status and the next approved task. This section is a high-level implementation snapshot, not a task tracker.

## Getting started

Follow [`docs/development.md`](docs/development.md) for prerequisites, configuration, database setup, build and test commands.

## Project documentation and decisions

- **Product requirements, approved decisions, roadmap, task status, account lanes (AC1 to AC5) and task notes:** [Notion Project Hub](https://app.notion.com/p/41544a92926f81dab0b7fc1f6451e23f).
- **Source code, API contracts, data model, development instructions and CI:** this repository.

Do not copy detailed Notion requirements or project status into this README. Do not copy repository API/schema/setup documentation into Notion. Update the authoritative source for the subject and link to it from the other system.

## Safety

- Never commit secrets, real `.env` files, credentials, resumes or real candidate data. Use synthetic test data and `.env.example`.
- Do not bypass authentication, CAPTCHA, OTP or platform restrictions; do not perform unauthorized scraping or automatic applications.
- Application submission requires human approval and verified confirmation. Follow the approved Notion product and architecture decisions.
- Preserve hidden repository files and folders (including `.github/` and `.config/`) when replacing project files.