# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-28 16:53 |
| Branch | `release/0.35.2` |
| Updated | 2026-11-28 by Cursor (INTEG2) |
| Live | unchanged (local only; never push) |
| Next | Planner: push / PR / CI after Ghazal yes |

## Goal now
Integrate NSET1b + NUI1/NUI1b as release **0.35.2** (local only).

## In this release (local)
- NSET1b: live Omar terms, default settings/assignments seed, eligibility = setup warning only (never blocks)
- NUI1/NUI1b: invoice preview diffs, housingMonthly catalog 200, Ninja commission as % of due
- Contracts regenerated; version 0.35.2

## How to verify
```bash
dotnet build tests/Engine.Tests
dotnet test tests/Engine.Tests
cd scripts && python3 -m unittest test_ftps_deploy
cd web && npx tsc --noEmit && npm test
python3 scripts/release_source.py && rm -rf artifacts
```

## Open for Ghazal
- Push / PR / merge / deploy of 0.35.2 — never from this worktree without his yes
