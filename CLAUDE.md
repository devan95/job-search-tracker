# AI coding-session entry point

Project: Job Search & Application Tracker (local-first; .NET 10, React/TypeScript, PostgreSQL).

## Read before starting

1. Open the [Notion Project Hub](https://app.notion.com/p/41544a92926f81dab0b7fc1f6451e23f).
2. Up to five accounts (AC1 to AC5) work in parallel, each on its own Task Board rows. Read the Notion page "Account Lanes (AC1 to AC5): Work Queue and Prompt" for your lane, the claim rules and the generic session prompt. Claim your row on the Task Board before you start.
3. Read the relevant confirmed requirement/decision and the exact Task Board item, including **Brief for the next account**. Follow the Notion Work Protocol and Testing Plan.
4. Connect to `devan95/job-search-tracker`; verify the intended branch, latest commit, CI result and actual files. Inspect the real working tree and preserve uncommitted work. If a user ZIP is attached, follow the Notion Attached ZIP rule before using it.

## Documentation ownership

- Notion owns product requirements, approved decisions, roadmap, task status (including each row's Notes, which replace handoffs), lane assignments and open questions.
- This repository owns source code, tests, API contracts, schema/migrations, development commands, CI and implementation-level technical documentation.
- Do not copy detailed information between the two systems. Link to the authoritative document. Update both only when a change affects two distinct subjects, each in its own canonical location.
- `README.md` is a short repository guide. `HANDOFF.md` is specific to a delivered change set; record durable session history in the Notes of your Task Board row (the Notion Handoff Log is closed history; do not add entries).

## Non-negotiable rules

- Implement only requirements marked Confirmed. Do not silently change product semantics, especially Applied, source status, scan outcomes or duplicate handling.
- Never commit secrets, real `.env` files, credentials, resumes or real candidate data. Use synthetic test data.
- Do not commit, push, merge, delete data, run destructive migrations or submit applications without explicit user approval.
- Do not bypass CAPTCHA, OTP, authentication or job-platform restrictions. Application submission requires human approval and verified confirmation.
- Core tracking must work without AI, Gmail or any individual job source.
- Report every check honestly as PASS, FAIL, NOT RUN or BLOCKED. A check not run is not a pass.
- Do not claim a branch, commit, CI run, test, deployment or application submission unless verified by actual evidence.
- Work one explicitly scoped Task Board item at a time, and only one you have claimed. Edit only your own rows; never change another account's In progress or In review row. Preserve all existing and uncommitted work.
- Branch name per row: `ac<n>/<short-task-name>` (for example `ac2/candidate-screens`), created from the base commit named in `HANDOFF.md`.
- Shared files (`docs/api.md`, `docs/data-model.md`, `docs/development.md`, `ci.yml`, `README.md`): change only the section for your own task; add a section instead of rewriting existing text.

## Repository checks

Run the commands applicable to the change. Full local setup and database-test requirements are documented in [`docs/development.md`](docs/development.md).

- Restore pinned .NET tools: `dotnet tool restore`
- Backend: `dotnet build JobSearchTracker.slnx`
- Backend tests: `dotnet test JobSearchTracker.slnx`
- EF model check: `dotnet ef migrations has-pending-model-changes --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`
- Frontend, from `web/`: `npm ci`, `npm run lint`, `npm test`, `npm run build`

Database tests require PostgreSQL and `JOBSEARCH_TEST_ADMIN_CONNECTION`; see `docs/development.md`. If the environment lacks the SDK, Docker, database or required credentials, report the affected checks as NOT RUN or BLOCKED. Do not imply CI passed unless you inspected the actual run.

## Delivery

Follow the current Notion Work Protocol for branch, review, complete-tree ZIP and handoff requirements. Before packaging, compare the ZIP file list against the base snapshot and ensure no tracked files are missing. Include a delivery-specific `HANDOFF.md` and explicitly list deleted files (or state `Deleted files: none`). Exclude `.git`, secrets, real `.env` files, candidate data, local databases, caches and build artifacts. The user performs the Git commit/push unless explicitly authorized otherwise.