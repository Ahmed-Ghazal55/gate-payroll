# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-28 (INTEG1) |
| Branch | `release/0.35.0` |
| Updated | 2026-11-28 |
| Live | unchanged (local only; never push) |
| Next | Planner: review INTEG1 report; push/PR only after Ghazal yes |

## Goal now
Release **0.35.0** integration branch ready locally. Report: `~/.cache/agents-runs/cursor-INTEG1-report.md`.

## In this release (local commits)
- Already on branch before INTEG1: Keeta cycle/upload, app-reset, agy-screens
- Applied own diffs: ninja-cycle-flow, core1-terms-schemes, erpv1-ops-display, jc1-jahez-chefz, hr1-staff
- Contracts regenerated; version bumped 0.34.2 → 0.35.0

## How to verify
```bash
dotnet build tests/Engine.Tests
dotnet test tests/Engine.Tests
cd scripts && python3 -m unittest test_ftps_deploy
cd web && npx tsc --noEmit && npm test
python3 scripts/release_source.py   # then delete artifacts/
```

## Open for Ghazal
- Push / PR / merge / deploy of 0.35.0
- Never push from this worktree without his yes
