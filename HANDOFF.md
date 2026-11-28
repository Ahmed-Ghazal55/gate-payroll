# HANDOFF

| Field | Value |
|---|---|
| Current writer | (free) |
| Branch | `release/0.35.0` |
| Updated | 2026-11-28 by Cursor (KAPP1 DONE) |
| Live | unchanged (local only; never push) |
| Next | Planner: push KAPP1 commit for PR #77 CI re-run after Ghazal yes |

## Goal now
Fix Keeta invoice checker approve after default-on-user path (KAPP1). Report: `~/.cache/agents-runs/cursor-KAPP1-report.md`.

## In this release (local)
- KAPP1: Keeta account-row deductions default to `account_level` (match Ninja); company `supplier_adjustment` stays unclassified
- Engine repro: `BrowserShapedDefaultOnUserAllowsCheckerApproveAfterCompanyAdjustmentClassify`

## How to verify
```bash
dotnet build tests/Engine.Tests
dotnet test tests/Engine.Tests --filter "FullyQualifiedName~KeetaInvoice|FullyQualifiedName~NinjaInvoice|FullyQualifiedName~PlatformInvoice|FullyQualifiedName~NFlow1Invoice|FullyQualifiedName~SalPlatformInvoice"
```

## Open for Ghazal
- Push / PR CI re-run / merge / deploy of 0.35.0
- Never push from this worktree without his yes
