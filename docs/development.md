# Development guide

## Prerequisites

- .NET SDK 10 (see `global.json`)
- Node.js 22+ and npm
- Docker Desktop (for PostgreSQL and the full stack)

## Option A: full stack in Docker

```
copy .env.example .env        (Windows CMD; use cp on macOS/Linux)
```

Edit `.env` and set `POSTGRES_PASSWORD` (letters and digits only). Then:

```
docker compose up --build
```

- API: http://127.0.0.1:5080/api/health/ready
- PostgreSQL: 127.0.0.1:5432 (localhost only)

Start the web UI separately:

```
cd web
npm ci
npm run dev
```

Open http://127.0.0.1:5173. The dev server proxies `/api` to `http://127.0.0.1:5080`.

## Option B: run the API from source

Start only the database (`docker compose up db`), then provide the connection string through an environment
variable or user secrets. Do not put the password in a committed file.

```
cd src/JobSearchTracker.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=5432;Database=jobsearch;Username=jobsearch;Password=<your password>"
dotnet run --launch-profile http
```

Local runs listen on http://127.0.0.1:5080 (set in `Properties/launchSettings.json`). Do not put a `Urls` setting
in `appsettings.json`: it overrides `ASPNETCORE_URLS` and breaks the Docker container's `0.0.0.0:8080` listener.

## Tests

```
dotnet tool restore
dotnet test JobSearchTracker.slnx
cd web && npm test
```

Tests marked `Category=Database` need a real PostgreSQL server. They create a uniquely named database per test, apply the
migration, and drop the database afterwards (also when the test fails). They **fail** when PostgreSQL is not reachable; they
are never skipped. Point them at a server where the user may create and drop databases:

```
docker compose up db -d
set JOBSEARCH_TEST_ADMIN_CONNECTION=Host=127.0.0.1;Port=5432;Database=postgres;Username=jobsearch;Password=<your password>
dotnet test JobSearchTracker.slnx
```

(PowerShell: `$env:JOBSEARCH_TEST_ADMIN_CONNECTION = "..."`. Use bash `export` on macOS/Linux.) Never put the password in a
committed file. To run only the tests that need no database, use `dotnet test JobSearchTracker.slnx --filter "Category!=Database"`;
that run does not prove the migration or schema.

CI runs the same tests against a PostgreSQL 17 service and also runs
`dotnet ef migrations has-pending-model-changes`, which fails when the model changed without a new migration.
After changing the model, add a migration: `dotnet ef migrations add <Name> --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`
(with `ConnectionStrings:Default` set). The data model is described in `data-model.md`.

## Management API

Candidates, employers, company lists and search profiles are managed through `/api/...`; see [api.md](api.md) for the
endpoints, error semantics and examples. Its tests are database tests (they need `JOBSEARCH_TEST_ADMIN_CONNECTION`).

## Health endpoints

| Endpoint | Meaning |
| --- | --- |
| `GET /api/health/live` | Process is up. Never touches the database. |
| `GET /api/health/ready` | `200 {status: ok, database: ok}` when PostgreSQL answers; `503 {status: degraded, database: unavailable}` otherwise. |
