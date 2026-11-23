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

- Fresh database: creates all sixteen tables and `__EFMigrationsHistory` (ten of them after `AddApplications`, six more from `AddRoleListsAndLocationSets`).
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

Expected: the first `migrate` applies all four migrations (`InitialDataModel`, `AddJobs`, `AddApplications` and `AddRoleListsAndLocationSets`), `\dt` lists sixteen tables plus `__EFMigrationsHistory` (ten is only the intermediate state after `AddApplications`), and the second
`migrate` applies nothing. `down -v` is safe here only because `COMPOSE_PROJECT_NAME` is `jst_verify` (check the `echo` line first).
Close the CMD window afterwards so the variables do not leak into normal use.

### Order 4: role lists and location sets

The migration `AddRoleListsAndLocationSets` comes after `AddApplications`. On a fresh database the first `migrate` applies it
as well, and `\dt` then lists sixteen tables plus `__EFMigrationsHistory`. An existing database at `AddApplications` only
gets this one migration (six new tables, no data change). Everything else in the verification above stays the same.

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

## Quality Gate (SonarQube Cloud)

Status: the gate in use is the built-in **Sonar way**, which SonarQube Cloud assigns by default (SonarQube Cloud Free plan). It is
not editable and no custom gate is needed. It was confirmed active in the project settings and the new-code period was reviewed.
Coverage on new code was measured at a baseline of 88.29%. Still open: validating pass and fail on a throw-away branch and marking
the SonarQube Cloud check as required in the branch ruleset (see "Rollout"). The gate is therefore active but not yet validated and
not yet enforced. Until the 13 `void` fixes for intentionally ignored `reload()` promises (the frontend task "Mark intentionally
ignored reload() promises with void...") are merged, the Reliability condition fails on `main`; that code is already merged but
still inside the new-code period, and it does not by itself fail a pull request, because on a pull request the gate judges only
the change relative to the target branch.

The Quality Gate is a setting of the SonarQube Cloud project, not a file in this repository. The `SonarQube Cloud analysis` CI job
only runs the analysis; SonarQube Cloud evaluates the gate and reports it on the pull request as its own check and decoration.
The job does not wait for the gate (no `sonar.qualitygate.wait`), so a failed gate does not fail the CI job by itself.

The built-in Sonar way gate applies its conditions only to code classified as new by the project's configured new-code definition
(the Clean as You Code period), not to code outside it. On a pull request, new code is the change relative to the target branch.
On `main`, code that is already merged still counts as new until the new-code period moves past it.
Its conditions:

| Condition | Threshold |
| --- | --- |
| Reliability rating on new code | A (no new bugs) |
| Security rating on new code | A (no new vulnerabilities) |
| Maintainability rating on new code | A |
| Security hotspots reviewed on new code | 100% |
| Coverage on new code | at least 80% |
| Duplicated lines on new code | at most 3% |

SonarQube Cloud skips the duplication and coverage conditions on a pull request with fewer than 20 new lines when the project
setting "Ignore duplication and coverage on small changes" is on (the default). That is acceptable for a gate meant to catch
bugs, vulnerabilities and unreviewed hotspots; switch the setting off only if small changes must also meet the duplication and
coverage limits.

Rollout:

1. **Advisory.** Keep the SonarQube Cloud check non-required in the branch ruleset. Review the first PR results for false
   positives and for new-code period mistakes, and mark confirmed false positives in SonarQube Cloud instead of loosening conditions.
2. **Validate pass and fail** on a throw-away branch, never on `main`: a trivial change passes; a change that adds an issue
   whose type is Bug or Vulnerability fails the matching rating condition, and the PR shows the failed check. Pick the rule from
   the project's quality profile and confirm its type in SonarQube Cloud before relying on it (for example a null dereference
   is normally reported as a Bug, an SQL statement built by string concatenation as a Vulnerability). An unused assignment is only
   a code smell and a hard-coded credential string is normally only a security hotspot, so neither tests these two conditions.
   Delete the branch afterward.
3. **Required.** Only after steps 1 and 2 and a clean run on real PRs, mark the SonarQube Cloud check as required in the branch
   ruleset. The analysis job is skipped for pull requests from forks (they get no GitHub OIDC permission), so a required check
   would block them. This is a single-owner repository whose pull requests come from branches in the same repository. If fork
   pull requests are ever accepted, keep the check non-required or give maintainers a ruleset bypass for them first.

The gate result is evidence only for what SonarQube analyzed. The job sets `sonar.scanner.scanAll=true` without exclusions, so
besides C# and TypeScript under `src/`, `tests/` and `web/` it can also analyze other files there, such as HTML (for example
`web/index.html`). No exclusion has been verified. It does not replace the build, lint, unit-test or e2e jobs.

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

### Phase 1 acceptance run (Order 9)

`phase1-core.spec.ts` is the Phase 1 acceptance run mentioned above, with helpers in `phase1-support.ts`. It covers the whole
manual core in one journey: a synthetic candidate; a company list imported through preview and confirm; a role list with an
equivalent title; a location set with a city, remote within a country and remote worldwide (scopes kept separate from work
modes); a search profile selecting all three kinds of list; a job added by hand whose company reuses the imported canonical
employer, then retrieved and edited; an application tracked as Saved and not applied, confirmed as applied manually (method
Manual, date and time recorded) and moved to Interview, with the three-entry audit history checked in the UI and the API; and
closing and reopening the job without any change to the application. Further tests cover repost and duplicate protection (the
same requisition id is blocked with no override, the same apply link is a warning, one application per candidate and
canonical job, and a second one is refused with `application_exists`), validation and error states (job form, confirmation
needs the tick and no future date, corrections need a reason and cannot make an application applied, a stale status change, a
job in use cannot be deleted) and an unreachable API. No application is marked applied except through the explicit
confirmation step. It adds specs only: there is one E2E tool chain and one CI job (`e2e`).

Run one spec or one test with `npx playwright test tests/phase1-core.spec.ts` or `npx playwright test -g "duplicate"` (add
`--headed` to watch it and `--debug` to step through it).

Failure evidence: `e2e/playwright.config.ts` keeps a trace (`retain-on-failure`) and a screenshot (`only-on-failure`) for every
failed test, and in CI also writes the HTML report. Inside `e2e/`, open a trace with
`npx playwright show-trace test-results/<test>/trace.zip` and the report with `npx playwright show-report`. The CI artifact is
named `playwright-report` and is kept for 7 days.

Troubleshooting:

- `The API and PostgreSQL must be running`: the API is not answering `http://127.0.0.1:5080/api/health/ready`. Run
  `docker compose ps`, then `docker compose logs api`. Apply the migrations (`docker compose run --rm migrate`) before starting
  the API.
- `Executable doesn't exist` or the browser fails to launch: run `npm run install-browsers` in `e2e/` (on Linux add
  `-- --with-deps`).
- A test cannot find a button or list: the Vite server may be an old one started without `API_PROXY_TARGET`; stop it and let
  Playwright start its own, or restart yours with the same value.
- Port 5173 or 5080 is already used by something else: stop it, or set `E2E_WEB_URL` / `E2E_API_URL`.
- Strange duplicates or slow lists after many runs: the tests never delete their data. Reset a disposable database with
  `docker compose down -v` and start again.
- A locator on a `<select>` never matches: a select's accessible name includes its chosen option, so use
  `getByRole('combobox', { name: /^Label/ })` rather than an exact `getByLabel`. Locator waits have no timeout of their own and
  last until the test timeout.

## Code base for an AI session

The repository is public. Every AI session (accounts AC1 to AC6) takes the latest `main` itself: a shallow clone
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

Every implementation ZIP is self-contained. In addition to the changed/added files at their exact repository paths and the
manifest/text files, it contains a copy of `tools/delivery/run-delivery.cmd` at the ZIP root as `run-delivery.cmd`.
The root copy is a launcher only: it is not listed in `delivery-manifest.json` and is not copied or staged into the clone.
The checked-in source remains `tools/delivery/run-delivery.cmd`.

For a new PR delivery, the package contains:
- changed/added repository files (complete files at their repository paths);
- `delivery-manifest.json`, `commit.txt`, `pr-title.txt` and `pr.txt`;
- root-level `run-delivery.cmd`.

For an update to an existing PR, the package contains:
- changed/added repository files (complete files at their repository paths);
- `delivery-manifest.json` and `commit.txt`;
- root-level `run-delivery.cmd` (no `pr-title.txt` or `pr.txt`).

Extract the ZIP outside the repository clone and double-click the root-level `run-delivery.cmd`. It auto-detects its own
package folder. No manual copy from the clone and no separate runner path are needed. The runner checks Git and authenticated
GitHub CLI and validates the manifest, package paths and launcher before touching the clone. Package-validation and dirty-tree
failures occur before fetch and leave the clone unchanged. The runner then fetches `origin`; that can update remote-tracking
refs even when it later refuses to leave the current branch. If the current branch has unpushed commits or cannot safely be
left, the runner stops after fetch without switching the checked-out branch or changing the working tree.

After that check, the runner switches to the target branch (`main` for new mode or the existing PR branch for update mode)
and may fast-forward it. The exact `baseCommit` check happens **after** this synchronization. A
stale base can therefore be rejected after the clone has already fetched, switched branches or fast-forwarded; the runner
does not undo those synchronization steps. A failure while applying files after the delivery branch is selected triggers a
rollback attempt. The runner checks each rollback command and reports completion only when reset/cleanup and (for new mode)
branch removal succeed. If it reports an incomplete rollback, inspect `git status` and restore the clone before retrying.

A tracked file that the delivery deletes may be replaced by a directory of the same name (for example `config` becomes
`config/settings.json`): deletions are validated first, and rollback tolerates the absent child. Destination inspection
errors (for example permission problems) are reported with the failing path component and stop the run.

Once the manifest-listed changes are applied, the runner verifies the resulting change set, commits, pushes and opens the PR
(new mode) or updates the existing PR branch (update mode). It never force-pushes or merges. The branch-tip and post-commit
push-recovery behavior is described below.

### Update mode: fixing review comments on an open pull request

When a pull request needs changes (for example after review comments), the delivery does not create a second branch or PR.
Its `delivery-manifest.json` has `"mode": "update"`; `branch` is the existing PR branch (it keeps its own safe name, for
example `chore/<name>`; only new-mode branches must be `ac1`–`ac6/<task>`) and `baseCommit` is the exact
tip of the PR branch when the delivery was prepared. The package includes `delivery-manifest.json`, `commit.txt`, the
changed files and root-level `run-delivery.cmd`. After `git fetch origin`, the runner compares the local PR branch with
the fetched `origin/<branch>` snapshot and fast-forwards when needed, then requires local `HEAD` to equal `baseCommit`.
The runner also compares the live exact remote ref to `baseCommit` before applying files. The update push sends the commit ID captured right after `git commit` (its parent must be `baseCommit`), not the mutable
local branch name, and uses `--force-with-lease=refs/heads/<branch>:<baseCommit>`: it only publishes the intended descendant when the remote ref
still exists at that exact base. If another push moves the ref or the ref is deleted before the update push, the lease
fails rather than overwriting or recreating the PR branch. This is still a point-in-time protocol; a changed ref must
be inspected and the delivery rebuilt. The runner never force-pushes to a different tip. On success the existing PR
updates automatically.

### Recovering when a push fails after a commit

A failed push after `git commit` leaves the completed delivery commit on the local branch; the runner deliberately does not
roll it back. **Do not rerun the same ZIP**: the existing local branch causes new-mode preflight to stop, and update mode
must not duplicate the commit. Keep the extracted package and clone, then inspect the current branch, `git status` and
`git log -1 --oneline` to verify the retained delivery commit. Run `git fetch origin` and query the **exact** ref:
`git ls-remote --exit-code --heads origin refs/heads/<branch>`. Verify that the returned second column is exactly
`refs/heads/<branch>`; a similarly prefixed ref such as `refs/heads/archive/ac1/task` is not the target branch.
Exit code 2 means no matching ref; any other non-zero exit code is a query failure and must stop recovery.
- If the exact remote branch is absent, and the local branch is clean and the retained commit is the intended delivery,
  retry with `git push -u --force-with-lease=refs/heads/<branch>: origin <branch>`. The empty expected value makes the
  push create the branch only if the remote ref is still absent.
- If the exact remote branch already points to the retained delivery commit, the push may have succeeded despite a lost
  response; do not push again. Continue with the PR step below.
- If the exact remote branch still points to `baseCommit` and the local branch is clean, first record the delivery
  commit with `git rev-parse --verify HEAD` as `<commitId>`. Then require that it is the **only** commit after
  `baseCommit` (`git rev-list --count <baseCommit>..<commitId>` returns `1` and
  `git merge-base --is-ancestor <baseCommit> <commitId>` succeeds), run against `<commitId>` and not against `HEAD`, and
  retry with
  `git push --force-with-lease=refs/heads/<branch>:<baseCommit> origin <commitId>:refs/heads/<branch>`, pushing exactly
  the commit those checks inspected; never push the mutable
  branch name. The ancestry and one-commit checks ensure the update is a fast-forward from `baseCommit`. The explicit expected-tip lease makes the retry fail if
  the remote ref was deleted or moved after inspection, rather than unintentionally recreating or replacing it. Do not
  use a plain push for this race-sensitive retry.
- If the exact remote branch exists at any other commit, stop and compare the histories. Do not force-push; reconcile the
  remote work or request a rebuilt delivery.

After a successful push in new mode, create the PR if the runner did not:
`gh pr create --base main --head <branch> --title "<PR title>" --body-file "<extracted-package>\\pr.txt"`.
Use the title from the package's `pr-title.txt`. In update mode the existing PR receives the pushed commit automatically;
do not create another PR. If the push did not complete a commit (or the runner rolled back before committing), fix the
reported pre-commit cause and rebuild/re-run the delivery rather than manually copying files.

### PR review comment replies and resolution

For inline review threads, reply on the original thread after the fix is pushed and verified. Resolve only when the requested
change is fully addressed and verified (or the thread is genuinely obsolete/no longer actionable and the reply explains why).
For partial fixes, failed checks, unresolved questions or blockers, reply with what remains and leave the thread open.
Use the authenticated GitHub CLI to inspect the review thread, reply to its root comment, and resolve by GraphQL thread ID;
the exact commands and evidence rules are in [the Agent Operating Model](agent-operating-model.md#5-review-comment-handling-reply-then-resolve-or-leave-open).
Never mark a thread resolved just because a reply was posted.
The clone path defaults to `C:\Users\Devan Shah\Repo\job-search-tracker`. When that path is not a clone the script asks once and
saves the answer in `%LOCALAPPDATA%\job-search-tracker-delivery.json` (local only, never committed). The `JOBSEARCH_REPO`
environment variable overrides it. Follow the recovery section above when a stop happens after a commit; only pre-commit
failures that leave the clone restored should be fixed and re-run from the same ZIP.
