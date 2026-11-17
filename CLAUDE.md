# AI coding-session entry point

Project: Job Search & Application Tracker (local-first; .NET 10, React/TypeScript, PostgreSQL).

## Read before starting

1. Open the [Notion Project Hub](https://app.notion.com/p/41544a92926f81dab0b7fc1f6451e23f).
2. Up to five accounts (AC1 to AC5) work in parallel, each on its own Task Board rows. Read the Notion page "Account Lanes (AC1 to AC5): Work Queue and Prompt" for your lane, the claim rules and the generic session prompt. Claim your row on the Task Board before you start.
3. Read the relevant confirmed requirement/decision and the exact Task Board item, including **Brief for the next account**. Follow the Notion Work Protocol and Testing Plan.
4. Read `devan95/job-search-tracker` (public) directly: shallow clone of the latest `main` (`git clone --depth 1`), or single files from `raw.githubusercontent.com`; do not use a repository attached as context in a Claude Project. Record the commit id as your base; verify the latest commit, CI result and actual files. Inspect the real working tree and preserve uncommitted work. If a user ZIP is attached, follow the Notion Attached ZIP rule before using it.

## Documentation ownership

- Notion owns product requirements, approved decisions, roadmap, task status (including each row's Notes, which replace handoffs), lane assignments and open questions.
- This repository owns source code, tests, API contracts, schema/migrations, development commands, CI and implementation-level technical documentation.
- Do not copy detailed information between the two systems. Link to the authoritative document. Update both only when a change affects two distinct subjects, each in its own canonical location.
- `README.md` is a short repository guide. A delivery's final message names its base commit, branch and deleted files; record durable session history in the Notes of your Task Board row (the Notion Handoff Log is closed history; do not add entries).

## Non-negotiable rules

- Implement only requirements marked Confirmed. Do not silently change product semantics, especially Applied, source status, scan outcomes or duplicate handling.
- Never commit secrets, real `.env` files, credentials, resumes or real candidate data. Use synthetic test data.
- Do not commit, push, merge, delete data, run destructive migrations or submit applications without explicit user approval.
- Do not bypass CAPTCHA, OTP, authentication or job-platform restrictions. Application submission requires human approval and verified confirmation.
- Core tracking must work without AI, Gmail or any individual job source.
- Report every check honestly as PASS, FAIL, NOT RUN or BLOCKED. A check not run is not a pass.
- Do not claim a branch, commit, CI run, test, deployment or application submission unless verified by actual evidence.
- Work one explicitly scoped Task Board item at a time, and only one you have claimed. Edit only your own rows; never change another account's In progress or In review row. Preserve all existing and uncommitted work.
- Branch name per row: `ac<n>/<short-task-name>` (for example `ac2/candidate-screens`), created from the base commit recorded in the row's Notes and the delivery's final message.
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

Follow the current Notion Work Protocol (section 7) for branch, review and delivery requirements. Deliver only the changed and added files, each complete and at its repo path, never a patch, a diff, an archive or the whole tree. Compare the delivered list with your own diff against the base: a changed file missing from the delivery is a blocker, and a file you did not change must not be in it. Hidden files count (`.github/`, `.config/`, `.gitignore`, `.env.example`, and so on). In the final message give the base commit, the branch, the checks and `Deleted files: none` or the full paths of deleted files. Exclude `.git`, secrets, real `.env` files, candidate data, local databases, caches and build artifacts. The user performs the Git commit/push unless explicitly authorized otherwise; the copy steps are in [`docs/development.md`](docs/development.md), section "Applying a delivery".

## PR review submission workflow

For any PR review, first read and follow [`.claude/skills/github-pr-review/SKILL.md`](.claude/skills/github-pr-review/SKILL.md). The skill defines the review checklist, finding format, severity, JSON payload and ZIP packaging rules.

- The reusable Windows runner is `tools/github-review/submit-review.cmd` and `tools/github-review/submit-review.ps1`. The payload example is `tools/github-review/review.example.json`.
- Do not submit the review directly through a GitHub connector, API or browser. Prepare a downloadable ZIP containing exactly these three files:
  - `review.json`
  - `submit-review.cmd`
  - `submit-review.ps1`
- The JSON must identify the repository, PR number, exact current 40-character PR head SHA, event, summary body and inline comments. Never invent paths or line references; each inline comment must be valid against the current PR diff.
- Use `COMMENT` by default. For AC6 (the independent reviewer), `COMMENT` is the only permitted event. Never use `APPROVE` or `REQUEST_CHANGES` for AC6.
- The user extracts the ZIP and double-clicks `submit-review.cmd`. The runner must verify GitHub CLI (`gh`) is installed and authenticated, confirm the PR is open, and confirm the current PR head SHA exactly matches `review.json`. If the PR changed, stop and regenerate the review.
- The runner displays the complete review and requires the user to type `SUBMIT`. Nothing is posted before that confirmation. It submits one GitHub review containing the summary and inline comments.
- Do not claim submission until the runner reports success and returns the GitHub review URL. Do not paste the full review into chat as a substitute for the ZIP.
- The review ZIP is not a code-delivery ZIP: it must not create branches, modify source files, commit, push or create a PR. Never include tokens, credentials, real candidate data or unrelated files.