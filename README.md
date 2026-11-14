# Job Search & Application Tracker

A generic, local-first application that discovers relevant jobs from saved company lists and enabled job
platforms, consolidates them into one tracker, and records manual or assisted applications consistently.

Code and data stay on your own machine (Windows PC first, home NAS later). Notion holds requirements,
decisions, tasks and handoffs; it is not the runtime database.

## What exists today

- **API** (ASP.NET Core, .NET 10): health endpoints, and a management API for candidates, employers, company lists and
  search profiles ([docs/api.md](docs/api.md)).
- **Database** (PostgreSQL via EF Core): six-table data model and the initial migration
  ([docs/data-model.md](docs/data-model.md)).
- **Worker**: placeholder heartbeat only; no scheduled work yet.
- **Web** (React + TypeScript + Vite): system status page only; the management screens are not built yet.
- **Docker Compose**: PostgreSQL, API and Worker, with localhost-only ports.
- **CI** (GitHub Actions): backend build, EF model-matches-migrations check and tests against PostgreSQL 17; frontend
  lint, test and build; secret scan.

Not built yet: company-list import, job discovery, job and application tracking, Gmail alerts, AI features.

Which tasks are done, in progress or verified is tracked on the Notion Task Board, not in this file.

## Layout

```
.github/workflows/ci.yml           CI pipeline
.config/dotnet-tools.json          Pinned dotnet-ef tool (run `dotnet tool restore`)
src/JobSearchTracker.Api/          ASP.NET Core Web API, EF Core model and migrations
src/JobSearchTracker.Worker/       Background worker
tests/JobSearchTracker.Api.Tests/  xUnit tests (database tests need PostgreSQL)
web/                               React + TypeScript + Vite UI
docs/                              development.md, api.md, data-model.md, phase0-feasibility.md
```

## Quick start

See [docs/development.md](docs/development.md).

## Rules that matter

- No secrets in Git. Use `.env` (ignored) and `.env.example` (committed, no values).
- No unauthorized scraping, authentication bypass or automatic applications.
- Source status and application status must be reported honestly; see the Notion Product Requirements.
- Hidden files are part of the repository: `.github/`, `.config/`, `.gitignore` and friends must never be dropped
  when files are replaced.
