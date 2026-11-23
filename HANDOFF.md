# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/forms-evaluation` |
| Updated | 2026-11-23 by Cursor (KPI1 done, not pushed) |
| Live | unchanged (no push/deploy) |
| Next | Grok KPI1 review; then planner integrates evaluation into release |

## Goal now
KPI1 professional analytics scorecards shipped on `feat/forms-evaluation`.

## Done (KPI1)
- Server scorecards: supervisors, apps, heatmap, city×app + Excel exports; filters before aggregate; low-sample + identityVisible; Arabic labels.
- OpenAPI / ENDPOINTS / api-contract regenerated; web panel with drill-down to replies.
- Tests: EvaluationScorecardTests (6) + full sqltest Passed:726; web 152/152.
- Commits: see `git log` on branch. Report: `~/.cache/agents-runs/cursor-KPI1-report.md`.

## How to verify
```bash
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~EvaluationScorecard"
npm --prefix web test
~/.cache/agents-runs/sqltest.sh /home/ghazal/Projects/active/gate-payroll-forms-ev
```

## Open for Ghazal
- Push/merge evaluation branch when ready (forms share with riders).
