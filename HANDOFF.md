# Handoff: Phase 0 repository foundation

**Date:** 2026-11-14
**Task:** Create the initial runnable skeleton (API + worker + web + PostgreSQL + health check + smoke tests + CI + docs).
**Base branch/commit:** none. The repository was empty at the start of this work and no Git state was inspected
(no GitHub access in this session). Nothing was committed or pushed.

## What is in this ZIP

| Area | Contents |
| --- | --- |
| `src/JobSearchTracker.Api` | ASP.NET Core (.NET 10) API: `GET /api/health/live`, `GET /api/health/ready` (200 when PostgreSQL answers, 503 `degraded` otherwise), EF Core `AppDbContext` with no entities yet, Dockerfile |
| `src/JobSearchTracker.Worker` | .NET Worker placeholder (heartbeat log only; no scheduled work), Dockerfile |
| `tests/JobSearchTracker.Api.Tests` | xUnit integration tests for the health endpoints (4 tests, no database needed) |
| `web/` | React + TypeScript + Vite status page showing API and database state (running / degraded / unreachable), Vitest tests (7), proxy of `/api` to the API |
| `docker-compose.yml`, `.env.example` | PostgreSQL 17 + API + Worker; all ports bound to 127.0.0.1; `POSTGRES_PASSWORD` required, no default |
| `.github/workflows/ci.yml` | Backend build/test, frontend lint/test/build, gitleaks secret scan |
| `README.md`, `CLAUDE.md`, `docs/` | Overview, AI session rules, development guide, Phase 0 feasibility matrix (all items NOT RUN) |
| `global.json`, `JobSearchTracker.slnx` | SDK pin (10.0.100, roll forward) and solution (XML `.slnx` format, needs .NET 10 SDK) |

## Verification results (actually executed in the AI workspace)

| Check | Result |
| --- | --- |
| Frontend `npm ci` from a clean copy | PASS |
| Frontend `npm run lint` (oxlint) | PASS (no findings) |
| Frontend `npm test` (Vitest) | PASS: 2 files, 7 tests |
| Frontend `npm run build` (tsc + Vite) | PASS |
| `docker compose config` with a dummy password | PASS (valid) |
| `docker compose config` without `POSTGRES_PASSWORD` | Fails as designed (password required) |
| CI workflow and compose YAML parse | PASS |
| Secret sweep of the tree | No secrets found |
| **Backend restore/build** | **NOT RUN.** The workspace has no .NET SDK and its network allowlist blocks the SDK and NuGet downloads |
| **Backend xUnit tests** | **NOT RUN** (same reason) |
| **`docker compose up` end to end** | **NOT RUN** (no .NET images/NuGet reachable, no running stack) |
| **GitHub Actions CI** | **NOT RUN** (never pushed) |

## Known limitations and risks

- The C# code and NuGet package versions (EF Core 10.0.0, Npgsql EF 10.0.0, Mvc.Testing 10.0.0, xunit 2.9.3,
  Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4) were written without a compiler and without checking NuGet.
  Expect that a version or small compile fix may be needed on the first `dotnet build`. Treat the backend as unverified.
- No database entities or migrations exist yet. The `ready` endpoint only proves a connection can be opened.
- The worker is a placeholder. The PostgreSQL-backed queue is a later task.
- No Playwright, Gmail, Freehire, AI or discovery code exists. Phase 0 feasibility items remain NOT RUN (docs/phase0-feasibility.md).
- Branch protection and the required-checks policy must be configured in GitHub; not verified.

## Your next steps

1. Back up or check your local working tree at `C:\Users\Devan Shah\Repos\job-search-tracker`.
2. Extract this ZIP **into that folder** and let it overwrite files. Keep the existing `.git` directory (it is not in the ZIP).
3. Create a feature branch, then run:
   ```
   dotnet build JobSearchTracker.slnx
   dotnet test JobSearchTracker.slnx
   cd web && npm ci && npm test && npm run build
   ```
   and, with `.env` created from `.env.example`: `docker compose up --build`, then open
   http://127.0.0.1:5080/api/health/ready (expect `{"status":"ok","database":"ok"}`).
4. Review `git status` and `git diff`, commit and push yourself, then check the first CI run.
5. Upload this same ZIP to the Notion Project Hub page as the latest snapshot.
6. Send me the build/test output (especially any backend errors) and I will fix them first.

Suggested next task after the backend is green: Phase 1 data model (candidate, company list, search profile) with
the first EF Core migration, driven by the confirmed requirements.
