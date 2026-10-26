# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | docs/knowledge-pack |
| Updated | 2026-10-26 by Codex |

## Goal now
Knowledge pack completed locally; commit includes [skip ci]. User explicitly requested no push.
Published accounting fixes remain the previously reported 0.7.0 (3b8ac0e); no deployment or live check was performed for this documentation task.

## Done (latest first)
- Added docs/README.md index and provenance, architecture, all 75 COA entries with owners/purposes, posting rules and reasons, dated decisions, deploy/recovery/backups, and prioritized roadmap.
- Read continuity, working knowledge, architecture/review and archived handoffs under ~/Projects; historical proposals are distinguished from current code and later fixes.
- Added AGENTS.md (52 lines): actual stack, build/test commands, deployment authorization and data rules.
- Matched all 75 account codes and exact names to FinancialCore; verified relative links and screened new docs for sensitive patterns. No operational data copied.
- Build: 0 warnings/errors; accounting offline 44/44 plus shared-cost test and 52 legacy checks; MSTest 90 passed / 6 SQL Server cases skipped; Python deployment-tool unit tests 21 passed (mocked, no live deploy).
- Installed pinned frontend dependencies to run the build; no application sources changed.
- Learning note retained in docs/README.md because this task permits no shared learning-log write outside the repo.
- Earlier live fixes: 5205/5206/5300 classification, no company expense in opening debt, explicit company, same-company Jahez payroll, opening-import permission, login lockout and access-scope fixes.

## Next step (exact)
1. Ghazal: choose the Jahez cash opening counterpart and who receives treasury.opening; docs/accounting/decisions.md lists the options and current behavior.
2. Start implementation with POST /api/finance/journals/{id}/reverse: mirror journal, original link, reason, independent second approver and consistent source-document effects. Add period-close enforcement in FinancialCore.PostJournal.
3. Then JahezTreasury.cs: earned-4102 commission release capped by previous releases, followed by company-specific 2140/1240 netting.
4. Follow docs/roadmap.md for independent waiver/transfer approval, security, off-site backups, staging/production approval, opening UI and performance.

## Open problems / decisions for Ghazal
- Jahez-purpose openings still use 3101; choose documented 2140/1240 or per-row treatment, rather than silently changing equity.
- Accountant cannot commit opening-debt imports without treasury.opening; assign the approver deliberately.
- Old 5109/5203 company-share entries remain history; reclassify by reviewed journal after reversal is available.
- No reversal/period close, commission release/netting, protected staging deployment or scheduled off-site backup is claimed complete.
- docs/knowledge-pack is local only; do not push or deploy without a new explicit instruction.

## How to verify
```bash
npm ci --no-audit --no-fund --prefix RiderForm
dotnet build tests/Engine.Tests -v q
dotnet run --no-build --project tests/Engine.Tests -- --accounting-review
dotnet test tests/Engine.Tests --no-build
python3 -m unittest discover -s scripts -p 'test_*.py' -v
git diff --check
```
Full VSTest requires local TCP permission; SQL Server cases require an isolated configured service. No secrets belong in this file or the knowledge pack.
