---
name: github-pr-review
description: Prepare and package a GitHub pull-request review using the repository's reusable local GitHub CLI review submitter. Use when asked to review a PR and provide a one-click review ZIP.
---

# GitHub PR Review

This skill applies only when AC6 is in independent reviewer mode. AC6 may switch to implementation fallback when it has no assigned/pending review task; in that mode follow `docs/agent-operating-model.md` and do not use this read-only review workflow for the implementation task.

## Purpose

Prepare a review for a GitHub pull request and package it for the repository owner to submit locally. The local submitter is in `tools/github-review/`; do not recreate its implementation.

## Review process

1. Read the repository's `CLAUDE.md` and applicable project review instructions.
2. Identify the exact repository and PR number. Inspect the PR description, complete diff, relevant surrounding code, tests, CI checks, and related project requirements.
3. Separate confirmed defects from risks, questions, and unverified evidence. Do not report assumptions as facts.
4. Prepare actionable inline comments for findings tied to changed lines. Use a concise summary for overall recommendation, scope concerns, evidence gaps, and questions that do not belong on a particular line.
5. Use one GitHub review, not a series of independent issue comments.
6. Default the event to `COMMENT`. For AC6, `COMMENT` is the only permitted event: never choose `APPROVE` or `REQUEST_CHANGES`. The local script enforces `COMMENT` and submits automatically after successful preflight validation; no interactive `SUBMIT` confirmation is required.
7. Use the exact current PR head SHA as `commit_id`. Do not guess it.
8. Validate every inline comment against the current PR diff. The submitter independently repeats this validation and refuses stale commits or invalid line references.
9. Create `review.json` using the schema in `tools/github-review/review.example.json`. Include only supported fields. Use `RIGHT` for new-file lines and `LEFT` for deleted-file lines. For multi-line comments, provide `start_line` and `start_side` as well.
10. Create a ZIP containing:
   - `submit-review.cmd`
   - `submit-review.ps1`
   - `review.json`
   Copy the two submitter files from `tools/github-review/`. Do not include credentials, tokens, `.git`, or unrelated repository files.
11. Provide the ZIP and concise instructions: extract it and double-click `submit-review.cmd`. The runner displays the validated review details, rechecks that the PR is open and its head SHA is unchanged immediately before submission, and submits one `COMMENT` review automatically. Do not instruct the user to type `SUBMIT`.

## Review quality

- Comment only on actionable issues introduced by the PR or directly affected by it.
- Prefer one comment per distinct finding; avoid duplicate comments on the same root cause.
- State severity where useful (High / Medium / Low), explain impact, and give a concrete correction or verification.
- Do not claim tests, Windows execution, CI, coverage ingestion, or runtime behavior was verified unless evidence supports it.
- If the PR diff is too large for GitHub's patch API to expose a target hunk, do not invent a line reference. Put the finding in the summary and explain the limitation.
- Keep the summary useful without repeating every inline comment.
- The review ZIP is a submission artifact only. It must not change repository files, commit, push, merge, or alter PR state beyond submitting the explicitly prepared review.


## Review-thread follow-up

A submitted review is separate from resolving its inline threads. When AC6 later implements a fix in implementation fallback mode, it must reply to each addressed thread and resolve only after the change is pushed and verified. Partial, blocked or unverified findings stay open with a reply describing what remains. See `docs/agent-operating-model.md` for the GitHub CLI commands.
