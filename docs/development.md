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

- Fresh database: creates the ten tables and `__EFMigrationsHistory`.
- Existing database already at the current migration: nothing to apply; data is untouched.
- Existing database behind the latest migration: applies only the missing migrations, in order.
- Running `migrate` again is always safe (idempotent). A failed migration exits non-zero; read the log before retrying.
- It never drops tables or volumes. Take a backup before applying a migration to a database you care about.
- The database image is PostgreSQL 18, which cannot start on a data volume created by PostgreSQL 17. For an existing local volume, dump it first (`docker compose exec db pg_dump -U jobsearch jobsearch > backup.sql`) while still on 17, then run `docker compose down -v`, start the new image and restore the dump. A fresh volume needs nothing.

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

Expected: the first `migrate` applies `InitialDataModel`, `AddJobs` and `AddApplications`, `\dt` lists ten tables plus `__EFMigrationsHistory`, and the second
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

CI runs the same tests against a PostgreSQL 18 service and also runs
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

The company-list paste import (`POST /api/company-lists/{id}/import/preview` and `.../import`, at most 1,000 non-blank rows per
request) needs no schema change or migration. Its parser tests (`CompanyListImportParserTests`) need no database and run with
`--filter "Category!=Database"`; its API tests (`CompanyListImportApiTests`, including a concurrent-import test) are database
tests. Both import endpoints share one parser, so a preview and a save of the same text always agree. Contract: [api.md](api.md),
section "Company-list import".

## Health endpoints

| Endpoint | Meaning |
| --- | --- |
| `GET /api/health/live` | Process is up. Never touches the database. |
| `GET /api/health/ready` | `200 {status: ok, database: ok}` when PostgreSQL answers; `503 {status: degraded, database: unavailable}` otherwise. |

## End-to-end tests (Playwright)

Browser tests live in `e2e/` and run against the real stack: PostgreSQL and the API from Docker Compose, and the Vite dev
server, which Playwright starts itself (`web/` needs `npm ci` once). Two specs exist: `smoke.spec.ts` (app loads, API and
database report healthy) and `lists-profiles.spec.ts`, the Lists & Profiles workflow: a candidate, two company lists, a
multiline import that reuses a canonical company across lists (whitespace, blank lines, commas, duplicate preview), a search
profile with several lists and work modes, `null` versus an explicit empty work-mode set, delete restrictions and
remove-before-delete, and the validation and duplicate feedback. The tests drive the UI and use the API only to seed data
and to read back what was stored. Every name carries a random suffix, so the tests run in parallel and a re-run does not
collide, but they never delete their data: use a disposable database. The spec fails at once, with a clear message, when the
API or PostgreSQL is not ready. No new tooling was added; the tests build on the existing Playwright setup. The Phase 1
acceptance run (Order 9) is a separate task.

```
docker compose up -d --wait db
docker compose run --rm migrate
docker compose up -d api
cd web && npm ci --ignore-scripts && cd ..
cd e2e
npm ci --ignore-scripts
npm run install-browsers
npm test
```

On Linux, use `npm run install-browsers -- --with-deps` instead (as CI does). It also installs Chromium's system
libraries and needs `sudo`; without them the smoke test cannot launch the browser.

Set `E2E_WEB_URL` (default `http://127.0.0.1:5173`) or `E2E_API_URL` (default `http://127.0.0.1:5080`) to point at another
instance. `E2E_API_URL` is also passed to the Vite server that Playwright starts as `API_PROXY_TARGET`, so the browser tests
and the direct health test use the same API. If you already run `npm run dev` yourself, start it with the same
`API_PROXY_TARGET`; Playwright reuses a running server outside CI. Use the disposable project settings from "Verifying the workflow on a disposable database" if you do not want
to touch your normal database, and `docker compose down` afterwards. A failed run leaves `e2e/playwright-report/` and
`e2e/test-results/` (traces and screenshots; both are git-ignored). CI runs this as the `e2e` job and uploads them as an
artifact on failure. `e2e/` is excluded from SonarQube coverage (it contains tests only).

## Code base for an AI session

The repository is public. Every AI session (accounts AC1 to AC5) takes the latest `main` itself: a shallow clone
(`git clone --depth 1 https://github.com/devan95/job-search-tracker`), or single files from
`raw.githubusercontent.com/devan95/job-search-tracker/main/<path>`, and records the commit id as its base. No archive of
the repository needs to be attached. The session never commits or pushes; it delivers changed files (see below).

## Applying a delivery (one branch per task)

A delivery lists only the changed and added files, each complete and at its repo path, and its final message names the
base commit, the branch (`ac<n>/<task>`) and the deleted files (or `Deleted files: none`). Back up or check your local
tree, then:

1. Create the branch from the base commit: `git switch -c ac<n>/<task> <BASE_COMMIT>`.
2. Copy each delivered file to its repo path (create folders where needed). Delete only the files listed as deleted.
   Keep `.git` and local-only files.
3. Run `git status`. Stop and ask if it shows a changed or deleted file that is not in the delivery list.
4. Review `git diff`, run the checks, commit, push and open one pull request per branch. When two pull requests touch
   the same lines, GitHub shows the conflict and the later one rebases.

## Applying a delivery with the runner (Windows)

`tools/delivery/run-delivery.cmd` automates the manual procedure above. A delivery is a folder with only the changed and
added files at their repo paths, plus `delivery-manifest.json` (base commit, branch, commit subject, PR title, `files`,
`deleted`), `commit.txt`, `pr-title.txt` and `pr.txt`. If it arrives as a ZIP, extract it outside the clone first. The
runner is kept in the repository, not in the delivery.

1. Put the delivery folder outside the clone.
2. Run `tools\delivery\run-delivery.cmd` and either drag the delivery folder onto it or paste the folder path when asked.
3. The script runs without a confirmation prompt. It stops, without changing anything, when:
   - Git or `gh` is missing, or `gh` is not authenticated;
   - the manifest or a path is invalid or unsafe, or the folder contains a file that is not in the manifest;
   - the clone has uncommitted changes, or the current branch has commits that are not pushed;
   - local `main` is ahead of or diverged from `origin/main`;
   - the manifest base commit is not the current `origin/main`, or the target branch already exists.
4. Otherwise it fetches `origin`, switches to `main` (fast-forward only), creates the branch from `origin/main`, applies and
   stages only the manifest-listed files, checks that the change set is exactly the manifest, commits with `commit.txt`,
   pushes the branch and opens the pull request to `main` with `pr-title.txt` and `pr.txt`. It never force-pushes or merges.
   A failure before the commit removes the new branch again.

### Update mode: fixing review comments on an open pull request

When a pull request needs changes (for example after review comments), the delivery does not create a second branch or PR.
Its `delivery-manifest.json` has `"mode": "update"`; `branch` is the existing PR branch and `baseCommit` is the exact commit
at the tip of that branch that the delivery was built on. The folder needs only `delivery-manifest.json`, `commit.txt` and
the changed files (no `pr.txt` or `pr-title.txt`). Run the same script the same way. It stops, without changing anything, when:

- the remote branch does not exist, or the local copy of the branch is ahead of or diverged from `origin`;
- the branch tip is not `baseCommit` (the branch moved since the delivery was built: ask for a rebuilt delivery);
- any of the checks above fails (clean tree, safe paths, change set equals the manifest).

Otherwise it checks out the branch (creating it from `origin/<branch>` when it is not local yet), applies and stages only the
manifest-listed files, commits with `commit.txt`, pushes the branch (never force) and prints the existing pull request address.
A failure before the commit restores the working tree and keeps the branch.

The clone path defaults to `C:\Users\Devan Shah\Repo\job-search-tracker`. When that path is not a clone the script asks once and
saves the answer in `%LOCALAPPDATA%\job-search-tracker-delivery.json` (local only, never committed). The `JOBSEARCH_REPO`
environment variable overrides it. If the script stops, do not copy files by hand: fix the cause and run it again.
