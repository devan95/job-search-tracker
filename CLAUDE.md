# AI coding-session entry point

Project: Job Search & Application Tracker (local-first; .NET 10, React/TypeScript, PostgreSQL).

## Read before starting

1. The [Notion Project Hub](https://app.notion.com/p/41544a92926f81dab0b7fc1f6451e23f) is for navigation; open it only if needed.
2. Read the Notion [Session Card (AC1 to AC5)](https://app.notion.com/p/41a44a92926f81c1907bd07e349be105): up to five accounts work in parallel, each on its own Task Board rows, and the card holds your lane, the claim rules and the delivery steps. Open "Account Lanes (AC1 to AC5): Work Queue and Prompt" or the Work Protocol in full only when the card does not answer. Claim your row on the Task Board before you start.
3. Read the relevant confirmed requirement/decision and the exact Task Board item, including **Brief for the next account**. Read the Testing Plan for the checks you will run.
4. Establish the code base. In a local checkout, use the working tree and preserve uncommitted work. In a chat session the repository is public: take the latest `main` yourself (shallow clone, or single raw files), or read the repository attached as context in the Project if GitHub cannot be reached, and record the commit id as your base. If GitHub is readable, verify the latest commit and CI result. Never search the web for the repository.
5. If your row is already In progress under your label, read its Notes and continue; do not redo finished steps.

## Documentation ownership

- Notion owns product requirements, approved decisions, roadmap, task status (including each row's Notes, which replace handoffs), lane assignments and open questions.
- This repository owns source code, tests, API contracts, schema/migrations, development commands, CI and implementation-level technical documentation.
- Do not copy detailed information between the two systems. Link to the authoritative document. Update both only when a change affects two distinct subjects, each in its own canonical location.
- `README.md` is a short repository guide. Record durable session history in the Notes of your Task Board row (the Notion Handoff Log is closed history; do not add entries). The final message of a delivery names the base commit, the branch and the deleted files.

## Non-negotiable rules

- Implement only requirements marked Confirmed. Do not silently change product semantics, especially Applied, source status, scan outcomes or duplicate handling.
- Never commit secrets, real `.env` files, credentials, resumes or real candidate data. Use synthetic test data. The repository is public: anything committed is visible to everyone.
- Do not commit, push, merge, delete data, run destructive migrations or submit applications without explicit user approval.
- Do not bypass CAPTCHA, OTP, authentication or job-platform restrictions. Application submission requires human approval and verified confirmation.
- Core tracking must work without AI, Gmail or any individual job source.
- Report every check honestly as PASS, FAIL, NOT RUN or BLOCKED. A check not run is not a pass.
- Do not claim a branch, commit, CI run, test, deployment or application submission unless verified by actual evidence.
- Work one explicitly scoped Task Board item at a time, and only one you have claimed. Edit only your own rows; never change another account's In progress or In review row. Preserve all existing and uncommitted work.
- Branch name per row: `ac<n>/<short-task-name>` (for example `ac2/candidate-screens`), created from the base commit named in the delivery's final message.
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

Follow the current Notion Work Protocol for branch, review, changed-file delivery and handoff requirements. Present each changed or added file complete and at its repo path (never a patch, diff or archive) and compare the delivered list with your own diff. In the final message name the base commit and the branch, and explicitly list deleted files (or state `Deleted files: none`). Never deliver secrets, real `.env` files, candidate data, local databases, caches or build artifacts. The user performs the Git commit/push unless explicitly authorized otherwise.