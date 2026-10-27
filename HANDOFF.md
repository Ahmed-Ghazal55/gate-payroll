# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex |
| Branch | docs/knowledge-pack |
| Updated | 2026-10-27 by Codex |

## Goal now
Complete the requested knowledge-pack corrections and a separate deploy-trigger commit, both with [skip ci]. No push or deployment authorized.

## Done (latest first)
- Continued the interrupted session: only the Current writer field was dirty; no duplicated edits.
- Moved the existing learning entry from docs/README.md to ~/.agents/learning/log.md; removed the docs-only learning restriction from AGENTS.md.
- Copied the recovered 2026-10-26 Claude review byte-for-byte into docs/reviews and linked it from the index; clarified its historical evidence.
- Recorded Ghazal's 2026-10-26 Jahez agent rule: commission/remainder split, debt split, opening 2140/equity split and mandatory maker-checker for balance changes.
- Added P1 Jahez agent-model gaps from the Claude 2026-10-27 findings supplied by Ghazal; fix order: debt creation, commission release, per-company isolation.
- Corrected stale claims that opening policy was undecided or same-company 2110 payment fully resolved fund isolation.
- Earlier verification on ee254cc: build clean; offline accounting 44/44 plus shared-cost and 52 legacy checks; MSTest 90 passed / 6 SQL Server skipped; Python deployment tools 21/21. These are historical results, not reruns for this docs task.
- Published accounting fixes remain the previously reported 0.7.0 (3b8ac0e); no fresh production verification.

## Next step (exact)
1. Commit these documentation corrections with [skip ci].
2. Add push.paths-ignore for docs/** and **/*.md in .github/workflows/build-and-deploy.yml; verify and commit separately with [skip ci].
3. Do not push; future implementation starts with debt creation per docs/roadmap.md.

## Open problems / decisions for Ghazal
- Jahez opening policy is settled; its implementation and maker-checker enforcement still need verification/fixes against the rule.
- The debt, pay-source, commission-release and per-company isolation gaps in docs/roadmap.md remain implementation work.
- Assign treasury.opening approvers deliberately; no permissions were changed.
- Reversal/period close, netting, protected staging and scheduled off-site backups remain open.
- No application code or operational data changed; local branch only.

## How to verify
```bash
git diff --check
python3 -m unittest discover -s scripts -p 'test_*.py' -v
```
Also check relative Markdown links, recovered-review byte equality, learning-entry uniqueness and workflow trigger scope. Application, browser, HTTP and SQL Server checks are not rerun for documentation/trigger-only changes.
