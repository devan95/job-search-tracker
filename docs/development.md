# Development guide

## Prerequisites

- .NET SDK 10 (see `global.json`)
- Node.js 22+ and npm
- Docker Desktop (for PostgreSQL and the full stack)

## Option A: full stack in Docker

```
copy .env.example .env        (Windows CMD; use cp on macOS/Linux)
```

Edit `.env` and set `POSTGRES_PASSWORD` (letters and digits only). Then start the database, create or upgrade the schema,
and start the stack (details in "Database schema (migrations)" below):

```
docker compose up -d --wait db
docker compose run --rm migrate
docker compose up --build
```

The schema is **not** created automatically. `/api/health/ready` only proves that PostgreSQL answers; it does not prove that the
tables exist. On a fresh database the management API fails until `migrate` has run.

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

## Database schema (migrations)

Policy: the schema changes only when you ask for it. The API never migrates at startup, and `EnsureCreated()` is not used
(it bypasses EF migrations). The explicit step is a one-shot Compose service, `migrate`, which runs the API image with
`--MigrateOnly=true`: it applies pending EF Core migrations and exits without starting the web server. It is behind the
Compose profile `tools`, so `docker compose up` never starts it.

Fresh or existing database (Windows CMD, from the repository root, with `.env` set):

```
docker compose up -d --wait db
docker compose run --rm migrate
docker compose exec db psql -U jobsearch -d jobsearch -c "\dt"
```

- Fresh database: creates the six tables and `__EFMigrationsHistory`.
- Existing database already at the current migration: nothing to apply; data is untouched.
- Existing database behind the latest migration: applies only the missing migrations, in order.
- Running `migrate` again is always safe (idempotent). A failed migration exits non-zero; read the log before retrying.
- It never drops tables or volumes. Take a backup before applying a migration to a database you care about.

Without Docker for the API (Option B) use `dotnet ef database update --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`
with `ConnectionStrings:Default` set.

### Verifying the workflow on a disposable database

Never test this against your real database and never run `docker compose down -v` in your normal project. Use a separate
Compose project name (own volume) and a different host port:

```
set COMPOSE_PROJECT_NAME=jst_verify
set DB_HOST_PORT=55432
set POSTGRES_PASSWORD=<throwaway letters and digits>
echo %COMPOSE_PROJECT_NAME%
docker compose up -d --wait db
docker compose run --rm migrate
docker compose exec db psql -U jobsearch -d jobsearch -c "\dt"
docker compose run --rm migrate
docker compose down -v
```

Expected: the first `migrate` applies `InitialDataModel`, `\dt` lists six tables plus `__EFMigrationsHistory`, and the second
`migrate` applies nothing. `down -v` is safe here only because `COMPOSE_PROJECT_NAME` is `jst_verify` (check the `echo` line first).
Close the CMD window afterwards so the variables do not leak into normal use.

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

## Coverage reports (SonarQube Cloud)

The `SonarQube Cloud analysis` CI job runs both test suites with coverage between `begin` and `end` of the analysis and imports
the reports. No coverage threshold is enforced until a baseline has been measured.

- Backend: `coverlet.collector` writes OpenCover XML to `coverage/dotnet/<run id>/coverage.opencover.xml`
  (`sonar.cs.opencover.reportsPaths`). Locally: `dotnet test JobSearchTracker.slnx --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover`.
- Frontend: `npm run test:coverage` (from `web/`, Vitest with the V8 provider) writes `web/coverage/lcov.info`
  (`sonar.javascript.lcov.reportPaths`). The job rewrites the `SF:` paths to start with `web/` before the analysis ends.
- Both output folders (`coverage/` and `web/coverage/`) are git-ignored.

## Management API

Candidates, employers, company lists and search profiles are managed through `/api/...`; see [api.md](api.md) for the
endpoints, error semantics and examples. Its tests are database tests (they need `JOBSEARCH_TEST_ADMIN_CONNECTION`).

## Health endpoints

| Endpoint | Meaning |
| --- | --- |
| `GET /api/health/live` | Process is up. Never touches the database. |
| `GET /api/health/ready` | `200 {status: ok, database: ok}` when PostgreSQL answers; `503 {status: degraded, database: unavailable}` otherwise. |

## Code base for an AI session

The repository is public. Every AI session (accounts AC1 to AC5) takes the latest `main` itself: a shallow clone
(`git clone --depth 1 https://github.com/devan95/job-search-tracker`), or single files from
`raw.githubusercontent.com/devan95/job-search-tracker/main/<path>`, and records the commit id as its base. No archive of
the repository needs to be attached. The session never commits or pushes; it delivers changed files (see below).

## Applying a delivery (one branch per task)

A delivery is a folder (usually a ZIP, extracted anywhere outside the clone) with only the changed and added files at their
repo paths, plus `delivery-manifest.json` (base commit, branch, commit subject, PR title, `files`, `deleted`), `commit.txt`,
`pr-title.txt` and `pr.txt`. The runner script is in the repository, not in the ZIP:
`tools/delivery/run-delivery.cmd`. You may keep a copy of it outside the clone.

1. Extract the ZIP outside the clone.
2. Run `tools\delivery\run-delivery.cmd` and either drag the extracted folder onto it or paste the folder path when asked.
3. The script runs without a confirmation prompt. It stops, without changing anything, when:
   - Git or `gh` is missing, or `gh` is not authenticated;
   - the manifest or a path is invalid or unsafe, or the folder contains a file that is not in the manifest;
   - the clone has uncommitted changes, or the current branch has commits that are not pushed;
   - local `main` is ahead of or diverged from `origin/main`;
   - the manifest base commit is not the current `origin/main`, or the target branch already exists.
4. Otherwise it fetches `origin`, switches to `main` (fast-forward only), creates the branch from `origin/main`, copies and
   stages only the manifest-listed files, commits with `commit.txt`, pushes the branch and opens the pull request to `main`
   with `pr-title.txt` and `pr.txt`. It never force-pushes or merges. A failure before the commit removes the new branch again.

The clone path defaults to `C:\Users\Devan Shah\Repo\job-search-tracker`. When that path is not a clone the script asks once and
saves the answer in `%LOCALAPPDATA%\job-search-tracker-delivery.json` (local only, never committed). The `JOBSEARCH_REPO`
environment variable overrides it. If the script stops, do not copy files by hand: fix the cause and run it again.
