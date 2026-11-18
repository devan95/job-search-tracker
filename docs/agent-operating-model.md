# Agent Operating Model (AC1–AC6)

This is the repository-side operating contract for all six agents. The Notion Task Board remains the live source of truth for ownership, status, priority, dependencies and progress. The Notion Account Lanes and Work Protocol pages remain authoritative for product and process decisions.

## 1. Roles

| Agent | Default lane | Primary ownership |
|---|---|---|
| AC1 | Backend A | Lists, profiles, backend APIs and related EF migrations |
| AC2 | Frontend | React/TypeScript UI and frontend integration |
| AC3 | Testing and end-to-end | E2E tests, Playwright and E2E CI |
| AC4 | Quality and job sources | Quality/tooling, API test gaps and assigned source adapters |
| AC5 | Backend B | Jobs, applications, backend APIs and related EF migrations |
| AC6 | Independent PR review; implementation overflow when idle | Reviews PRs independently by default; may implement an eligible AC1–AC5 task when no AC6 review/task is waiting |

AC6 operates in one of two modes:
- **Reviewer mode:** when a review is assigned or pending, inspect the latest PR independently and prepare the repository's `COMMENT` review package. Stay read-only and do not implement findings in the PR being reviewed.
- **Implementation fallback:** when AC6 has no assigned or pending review task, it may take the highest-priority/oldest eligible Ready task from any AC1–AC5 lane. It becomes the implementation owner and follows the normal claim, branch, test, delivery and PR process. AC6 must never review its own implementation PR; another account or the user must perform the independent review.

## 2. Task selection and AC6 takeover

A task is Ready only when Status is Not started, all dependencies are Done, all required user decisions are answered, and Notes do not say NOT READY or BLOCKED.
- AC1–AC5 first select a Ready task from their own lane; existing float-pool rules continue to apply among AC1–AC5.
- AC6 first checks for an assigned/pending review. If none exists, it may select any Ready, unclaimed task owned by AC1, AC2, AC3, AC4 or AC5. This is automatic; no `TAKEOVER: ACn` instruction is required.
- AC6 takeover never means concurrent editing. Do not take a row already In progress or In review, a Blocked row, a row waiting for a user decision, or a row with incomplete dependencies. Reassigning an active row requires the user to explicitly change its Owner on the Task Board after coordinating with its current owner.
- Before editing, claim the row: set Owner to your account label and Status to In progress; record the original owner (for AC6 takeover), date, base commit and branch intent in Notes; re-read the row and proceed only if the claim still belongs to you.
- Work on one claimed row at a time. Do not edit another account's In progress or In review row. The schema baton is global: do not start a Schema task while another Schema task is In progress or In review (not merged).
- If no eligible task exists, report the dependencies or unanswered decisions and stop. Never invent a requirement or bypass a blocker.

## 3. Branches and file ownership

Use one branch and one PR per Task Board row. Branch names are `ac<n>/<short-task-name>`, where `n` is 1–6. AC6 implementation work uses `ac6/<short-task-name>`.

Keep the existing ownership boundaries unless the Task Board explicitly assigns shared work: AC1/AC5 own backend areas for their claimed rows and coordinate through the schema baton; AC2 owns `web/`; AC3 owns `e2e/` and the E2E CI job; AC4 owns assigned quality/tooling and source-adapter work; AC6 follows the lane of the task it has claimed. In shared files, change only the section relevant to the task.

## 4. Self-contained delivery ZIP

Every implementation delivery ZIP must work after extraction without copying a helper from the repository.

**New PR mode** contains changed/added files at exact repository paths, `delivery-manifest.json`, `commit.txt`, `pr-title.txt`, `pr.txt`, and a copy of `tools/delivery/run-delivery.cmd` at the ZIP root named `run-delivery.cmd`.

**Existing PR update mode** contains changed/added files at exact repository paths, `delivery-manifest.json`, `commit.txt`, and the same root-level `run-delivery.cmd`. Update mode uses `"mode": "update"`, the existing PR branch and its exact current tip SHA as `baseCommit`; it does not contain `pr.txt` or `pr-title.txt` and does not create a second PR.

The root `run-delivery.cmd` is a package helper, not a manifest entry and not a file copied/staged into the repository. If the task changes `tools/delivery/run-delivery.cmd`, include the changed repo file at that path as a manifest-listed file and also include that same updated script as the root launcher.

Do not include extra files, `.git/`, secrets, real `.env` files, credentials, candidate data, local databases, logs, caches, dependencies or build output. The runner rejects unexpected files and unsafe paths.

After extracting the ZIP outside the clone, the user double-clicks the root-level `run-delivery.cmd`. It auto-detects the package, checks Git and authenticated GitHub CLI, validates the manifest and exact base SHA, performs safe branch/worktree preflight, applies only manifest-listed paths, and then commits/pushes and creates a PR (new mode) or updates the existing PR branch (update mode). It never force-pushes or merges. If any check fails, stop and report the exact failure; never work around it by manually copying files.

The final delivery message names the base commit, branch, exact checks/results and deleted paths (`Deleted files: none` when applicable).

## 5. Review comment handling: reply, then resolve or leave open

For each relevant inline review thread, read the full discussion and record its GraphQL thread ID, root inline-comment database ID, author, path/line and requested change. Classify it as fully addressed, partially addressed, not addressed, blocked/needs a decision, or obsolete/no action required.

Implement the fix on the existing PR branch using update-mode delivery. Run the relevant tests and inspect the final diff. Only after the corrected commit is pushed and the current PR head is verified:
- Reply on the original inline thread with what changed, where, and the commit/test evidence.
- Resolve the thread only when the request is fully addressed and verified, or the thread is genuinely obsolete/non-actionable and the reply explains why.
- For partial fixes, failed checks, open decisions or blockers, reply with what remains and leave the thread open.
- Re-fetch the thread after the action and report Resolved or Open. A reply does not automatically resolve a thread; never resolve just to clear the review queue.

Use authenticated GitHub CLI on the user's authorized environment. Never claim a reply or resolution succeeded unless `gh` confirms it. If authenticated access is unavailable, prepare the exact reply/commands, report the action as NOT RUN, and leave the thread open.

### Inspect review threads

Replace OWNER, REPO and PR_NUMBER:

```bash
gh api graphql -F owner=OWNER -F name=REPO -F number=PR_NUMBER -f query='
query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      reviewThreads(first: 100) {
        nodes { id isResolved isOutdated comments(first: 100) {
          nodes { databaseId author { login } body path line originalLine url }
        } }
      }
    }
  }
}'
```

### Reply to the root inline comment

Use the `databaseId` of the root comment, not a reply:

```bash
gh api repos/OWNER/REPO/pulls/PR_NUMBER/comments/COMMENT_ID/replies --method POST -f body="Fixed in COMMIT_SHA: describe the change and passing checks."
```

### Resolve after the reply

Use the GraphQL review-thread `id`:

```bash
gh api graphql -f query='
mutation($threadId: ID!) {
  resolveReviewThread(input: {threadId: $threadId}) { thread { id isResolved } }
}' -f threadId=THREAD_NODE_ID
```

To keep a thread open, do not call the resolve mutation. Resolving an inline thread is separate from approving, dismissing or merging a PR.

## 6. AC6 independent review mode

When AC6 is in reviewer mode, follow `.claude/skills/github-pr-review/SKILL.md` and review the actual latest PR diff, requirements and evidence. Prepare the review ZIP with `review.json`, `submit-review.cmd` and `submit-review.ps1`. The event is `COMMENT` only; never use `APPROVE` or `REQUEST_CHANGES`. The current submitter validates and submits automatically; do not ask the user to type `SUBMIT`. AC6 is read-only for that review and never reviews its own implementation PR.

## 7. Evidence and Task Board notes

Keep the claimed Task Board row's Notes current: claim/takeover and original owner, base commit, files changed, branch, PR, checks (PASS/FAIL/NOT RUN/BLOCKED), each relevant review thread's reply/resolution state, blockers and next action. Update Requirements Status only when the task completion criteria are met. A task is Done only when its PR is merged and required CI is green (unless the project-specific completion rule explicitly states otherwise).