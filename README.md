# Job Search & Application Tracker

A generic, local-first application that discovers relevant jobs from saved company lists and enabled job
platforms, consolidates them into one tracker, and records manual or assisted applications consistently.

Code and data stay on your own machine (Windows PC first, home NAS later). Notion holds requirements,
decisions, tasks and handoffs; it is not the runtime database.

## Status

**Phase 0 – foundation.** The repository contains a runnable skeleton only:

- ASP.NET Core Web API (.NET 10) with `/api/health/live` and `/api/health/ready`
- PostgreSQL via EF Core (no entities or migrations yet)
- .NET Worker (placeholder heartbeat; no scheduled work yet)
- React + TypeScript + Vite status page that shows API and database state
- Docker Compose for PostgreSQL, API and Worker (localhost-only ports)
- CI: backend build/test, frontend lint/test/build, secret scan

Job discovery, Gmail, profiles, applications and AI are **not built yet**.

## Layout

```
src/JobSearchTracker.Api/       ASP.NET Core Web API
src/JobSearchTracker.Worker/    Background worker
tests/JobSearchTracker.Api.Tests/  xUnit integration tests
web/                            React + TypeScript + Vite UI
docs/                           Development guide and Phase 0 feasibility matrix
```

## Quick start

See [docs/development.md](docs/development.md).

## Rules that matter

- No secrets in Git. Use `.env` (ignored) and `.env.example` (committed, no values).
- No unauthorized scraping, authentication bypass or automatic applications.
- Source status and application status must be reported honestly; see the Notion Product Requirements.
