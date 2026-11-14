# Phase 0 feasibility matrix

Status values: **VERIFIED**, **FAILED**, **NOT RUN**, **BLOCKED**. Nothing below is verified until evidence is
recorded in the Evidence column. Unverified connectors stay disabled or behind interfaces.

| Item | Status | Evidence / next step |
| --- | --- | --- |
| Repository baseline (API, worker, web, compose, CI) | NOT RUN end to end | Created in this change set; see HANDOFF.md for what was actually executed |
| GitHub Actions CI first run | NOT RUN | Push, then record the run URL and result |
| Docker Compose stack starts and `/api/health/ready` returns 200 | NOT RUN | Run `docker compose up --build` locally and record the result |
| Freehire India/API proof of concept (coverage, freshness, pagination, terms) | NOT RUN | Needs outbound access to the Freehire API; check data/API terms before retaining bulk data |
| Greenhouse / Lever / Ashby public endpoints | NOT RUN | Verify one real company per ATS with recorded fixtures |
| Permitted platform methods (Adzuna / Jooble) | NOT RUN | Requires the user's API keys; keep disabled until verified |
| Gmail read-only IMAP authentication | NOT RUN | Requires the user's mailbox and app password; read-only access only |
| OmniRoute route/model/cost visibility | NOT RUN | Owned by the AI Gateway & Privacy Setup page; optional for the core app |
| Windows prerequisites (.NET 10, Node, Docker) | NOT RUN | Run the commands in docs/development.md on the target PC |
| NAS prerequisites (CPU, RAM, disk, Docker) | NOT RUN | Validate before choosing the NAS as the host |
