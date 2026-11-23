# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/w3-screens-a` |
| Updated | 2026-11-23 by Cursor (W3AC done, not pushed) |
| Live | unchanged (no push/deploy) |
| Next | Grok review via `run-W3AC.sh` / planner; release after 0.33.0 + 0.34 schema |

## Goal now
W3 screens-A follow-ups after reboot: real `POST /sal/simulations`, payroll sidebar/workspace tabs, simulation banner wired to server ids.

## Done (W3AC)
- `POST /api/sal/simulations` create (AccessPolicy + OpenAPI + contract) with create/duplicate tests.
- Payroll shells redirect to `/payroll/?tab=…`; sidebar no longer uses `/payroll/{app}` for active apps.
- SimulationBanner binds `rows`/`simNet`/`liveNet` and journal `side`/`amount` shapes; client `createSimulation`.
- Approvals maker-checker empty copy; CoA column filters; Jahez error message polish.
- Verify: web 143/143; `api:check` OK; `npm build` OK; sqltest Failed:0 Passed:955.
- Commits on branch (local only). Report: `~/.cache/agents-runs/cursor-W3AC-report.md`.

## How to verify
```bash
npm --prefix web test
npm --prefix web run api:check
npm --prefix web run build
~/.cache/agents-runs/sqltest.sh /home/ghazal/Projects/active/gate-payroll-w3a
```

## Open for Ghazal
- Release W3A only after 0.33.0 backup/merge and the 0.34 schema path.
