# Gate Payroll — Full Project Review (26 Oct 2026)

Reviewer: Claude (Opus 5.5), reading the local source at `04_Payroll_&_Ledger_App_Source` (version 0.7.0), the Codex/Antigravity conversation logs, `PROJECT_CONTINUITY.md`, the handoff files, GitHub and the live site.

Read-only review: no code, data or deployment was changed. Codex was deploying PR11 to main while this review was being written.

---

## 1. What I understand the project to be

**The business.** Three legal entities (commercial registrations): **BW** (شركة البوابة المقبلة للتجارة), **EX** (إكسبرس جايت) and **TR** (مؤسسة البوابة المقبلة التجارية). They employ or sponsor ~870 delivery riders working on Jahez, Keeta, HungerStation, Ninja and others. The company:

- pays riders. Pay is either a commission (the platform settlement minus a monthly company fee), salary tiers by order count, or an "approved pay" figure taken from a financial source;
- advances money and pays costs on riders' behalf: HR/government fees, housing, fleet costs, fines, equipment, and Jahez cash shortfalls. It then recovers these as debts, by cash or payroll deduction;
- runs Jahez field supervisors who collect cash and new-account fees and hand them over to the central treasury;
- needs each entity's books kept separate, with explicit intercompany entries when one entity pays for or collects on behalf of another.

**The system.** `Gate Payroll` is a single ASP.NET Core (.NET 10) minimal-API app plus TypeScript front-ends:

| Area | Main files | What it does |
|---|---|---|
| Storage | `Store.cs` | One `records(id, kind, body JSON)` table on SQLite (prod) or SQL Server; global write lock + serializable transactions |
| Payroll | `Engine.cs`, `Program.cs` | Excel/CSV work import, policies, per-rider preview, fingerprinted approval, payments |
| Debts | `Debts.cs`, `DebtTransfers.cs` | 22 debt categories, approval workflow, collection, waiver, bulk collection, opening import, ownership transfer between entities |
| General ledger | `FinancialCore.cs`, `AccountingEvents.cs`, `AccountingPolicy.cs` | Hard-coded chart of accounts and vaults, balanced journals per entity, purpose rules for each vault |
| Treasury / Jahez | `TreasuryEntries.cs`, `JahezHandovers.cs`, `JahezTreasury.cs`, `LegacyCashAllocation.cs` | Opening reconciliation, supervisor→finance handover with counting, Jahez platform settlement, one-time MAIN.TR split |
| Access | `UserAccess.cs`, `AccessPolicy.cs`, `FinancialScopes.cs` | 11 roles, atomic permissions with grants/denials, scope by company/vault/category/platform, department/rank approvals |
| Rider-facing | `SubmissionChoices.cs`, `FormBuilder.cs`, `InquiryApi.cs`, rider-app | Public forms by token, payslip/debt lookup with one-time code, mobile portal |
| Delivery | `.github/workflows/build-and-deploy.yml`, `scripts/*.py` | Tests → publish → FTPS to MonsterASP IIS → health check → SHA256 audit |

Live: `https://payroll.marketingalbawaba.com` reports 0.7.0, `dataPreservationVerified=true`.

**How the work has been done.** Antigravity, Codex and now Claude have worked on it in turn, with the user approving each step. Versions 0.5.2 → 0.7.0 shipped in 5 days (22–26 Oct). Every merge to `main` deploys to production automatically. Continuity is kept in long Arabic handoff notes instead of git history on the local machine.

---

## 2. Overall assessment

**The project is better than its pace suggests.** The financial core shows careful thinking:

- balanced double entry checked **per entity**, with no hidden 1310 plug;
- each vault is restricted by purpose (Jahez cash can't fund HR or advances);
- recording a debt (accrual/opening) is separate from paying cash;
- immutable debt-ownership history;
- signed previews for every import;
- independent counting for handovers, and no self-approval;
- a database checkpoint before each upgrade, and a deployment audit by SHA256.

The local build has 0 warnings/errors, and the offline suite passes **40/40 accounting tests + 52 legacy checks** (I re-ran it today).

**The main risks are no longer “wrong arithmetic”.** They are:

1. **Operational dead-ends.** Money can get into an account and have no permitted way out, and posted entries can't be corrected.
2. **Segregation-of-duties gaps** in a few powerful actions.
3. **Scale.** Every request loads whole tables into memory.
4. **Process.** Several AI agents, no staging environment, and auto-deploy to production on every merge.

---

## 3. Findings — financial & accounting logic

Ranked by impact. "P1" = fix before relying on it with real money; "P2" = fix soon; "P3" = hardening.

### P1-A. Jahez settlement money is trapped in `BANK.*.JAHEZ`

**Where:** `JahezTreasury.cs` (platform-settlement), `AccountingPolicy.ValidateCompanyTransfer`, `AccountingEvents.CompanyPaymentLines` (line 100).

**The flow today:**
- *Receiving from Jahez:* Dr `BANK.X.JAHEZ` / Cr 1240.
- *Paying Jahez:* Dr 2140 / Cr `BANK.X.JAHEZ`.
- *Cash collected by supervisors* moves SUP → `MAIN.X.SET` → (jahez-transfer) → other settlement accounts only.

**The gap:**
- Salary payments (`/payments`) may only come from `DebtAndHr` or `Bank` accounts, never from `JahezSettlement`.
- The general transfer explicitly refuses settlement accounts.
- So for commission riders, the platform payout lands in `BANK.X.JAHEZ`, while their salaries must be paid from `BANK.X` / `MAIN.X.DEBT`. `DemandAvailable` will block those payments unless the company tops up from its own money.
- The company fee (4102) earned from that payout can never be released either.

**Suggestion:** add one controlled "release" event: settlement account → company bank, limited to

- the rider entitlements (2110) actually paid from that payout, and
- the approved 4102 fee,

with evidence and independent approval.

Also add a **netting** entry (Dr 2140 / Cr 1240) for when Jahez deducts collected cash from its payout. Today the two sides can only be settled by real bank movements, which isn't how Jahez reconciles in practice.

### P1-B. No reversal or correction path for posted entries

There is no endpoint to reverse:
- a journal entry,
- a collection or waiver,
- a payroll run,
- a debt transfer,
- or a handover.

Payroll approval also refuses any retroactive change ("later movements exist"). A single typo in an amount or vault becomes permanent. The only fix today is an unrelated adjusting entry, or editing the database by hand, which destroys the audit trail.

**Suggestion:** add a generic `reverse` action:
- it posts a mirror journal linked to the original;
- it requires a reason and a second approver;
- it rolls back the related document state (debt balance, settlement, run status).

Pair it with **period close**: once a month is closed, it can only be corrected by a reversal in an open period.

### P1-C. Opening balances can be imported without the opening permission or approval

`/api/debts/import-opening/commit` needs only `debts.import_opening` (`AccessPolicy.cs:58`). The **Accountant** role has that by default.

The import:
- posts receivables against **3101 equity**,
- can auto-create riders,
- can choose `Disbursement` against a vault.

By contrast, creating a single opening debt requires `treasury.opening` plus `openingReviewed` (`Debts.cs:168`), and treasury openings require `treasury.opening`. So the bulk path is weaker than the single path.

**Suggestion:**
- Require `treasury.opening` on the opening import too, plus an independent approval step before commit, like the handover.
- Restrict the `Disbursement` mode in that template to cases with a payment document.

### P1-D. Opening Jahez custody against equity overstates company equity

`TreasuryEntries.ValidateOpening` (line 25) allows opening any vault against 3101, including `SUP.*.SET` / `MAIN.*.SET` / `BANK.*.JAHEZ`. That money is owed to Jahez or to riders, not owned by the company.

**Suggestion:** for settlement-purpose vaults, the opening counterpart should be the platform control account (2140/1240) or 2110, not equity.

### P2-E. Waivers and debt transfers are single-person actions

- **Waivers:** `debts.waive` lets one user forgive any amount with just a reason text (`Debts.cs:292`). Accountant and FinancialAccountant have it by default.
- **Transfers:** `debts.transfer_sponsor` moves a debt between legal entities and creates intercompany balances, also without a second approver. Accountant has this too.

These are classic fraud and error paths.

**Suggestion:**
- Amount thresholds: above X needs a decision from `approvals.decide` by a different department/rank (the workflow already exists in `CreateMutualApproval`).
- Daily/monthly caps per user.
- A waiver report for management.

### P2-F. Two sources of truth for debt balances

Each `debt` document keeps a mutable `remainingBalance` / `paidAmount`, and the journal keeps the receivable. The code checks they agree at collection and transfer time, and the statement shows `reconciliationDifference`. But nothing checks the whole book regularly.

**Suggestion:** a nightly or on-demand **reconciliation report**:
- debt balance vs. journal receivable per debt and entity;
- 2110 vs. legacy ledger per rider;
- vault balance ≥ 0;
- 1310 BW↔EX↔TR mirror (the intercompany balances must net to zero across the group).

Show it on the finance dashboard with a red/green status.

### P2-G. Payroll go-live is blocked until each rider's opening is reconciled

`AccountingEvents.VerifyPayrollOpening` refuses approval unless the GL 2110/1211 balance equals the legacy ledger balance **for that entity**. That's correct, but it means **every rider with history needs an opening row before their next payroll**.

**Suggestion:** a guided "payroll opening" screen:
- list riders with a non-zero legacy balance and no opening;
- prefill the template from the legacy ledger;
- let finance review the batch in one go.

Without it, the first payroll month on 0.7.0 will stall.

### P3-H. Smaller accounting points

- **Positive and negative adjustments** both post against 5301. A deduction for damage or a fine should credit the matching cost/recovery account (5201/5203), not reduce salary expense.
- **Jahez account-opening fee** (`/finance/jahez-fee`): the amount is free-form, there's no duplicate check per rider, and an iqama is optional. Add uniqueness per rider + account and make the iqama required.
- **Blank entity silently becomes BW:** `ResolveSponsor("")` returns BW (`FinancialCore.cs:250`). The current callers pass explicit values, but this is unsafe. Make blank an error.
- **Journal numbering:** `JE-{year}-{count+1}` uses a global count. Numbers don't restart each year and can jump. Use a per-year sequence document.
- **Revenue timing:** 4101 is recognised when the cash arrives. That's fine per the owner's decision, but record the decision date on each entry for tax review (partly done in the line note).

---

## 4. Findings — access & security

### P1-I. No password change, reset, MFA or lockout

There's no endpoint for a user to change their own password, or for an admin to reset one. Accounts can't be recovered without editing the database.

Login is protected only by 30 requests/min per IP (`Program.cs:41`). There's no per-account lockout and no MFA, on a system that moves money for three companies.

**Suggestion:**
- Self password change, and admin reset that forces a change at next login.
- Lockout after N failures.
- TOTP MFA for Admin/SuperAdmin and any user with `treasury.*` or `debts.waive`.

### P2-J. Path-based authorisation is fragile

`AccessPolicy.Allowed` matches URL prefixes in a long if-chain:
- Some branches are unreachable (lines for `/api/excel/choices/`, `/api/excel/riders/`, and the second `reconcile-opening`).
- One rule allows any HTTP method (`/api/finance/opening/`).

It's safe today because it denies by default and ASP.NET routing is case-insensitive while the checks are case-sensitive (so unexpected spellings fall through to deny). But every new endpoint needs a matching hand-written line.

**Suggestion:**
- Move to endpoint metadata (`.RequirePermission("debts.collect")` per route).
- Add a startup test that fails if any mapped route has no declared permission.

### P2-K. Inconsistent admin checks and small gaps

- `CampaignOperations.Admin` checks `Role == "Admin"`, so **SuperAdmin** is refused there.
- `toggle-todo` on notifications has no scope check (`FinancialCore.cs:738`). Anyone with `mobile.broadcast` can toggle any task, including approval notices.
- **Legacy finance users** with no `financialScope` get **all entities and all vaults** (`FinancialScopes.Effective`). Assign explicit scopes to existing users and drop the fallback.
- **Firebase service-account JSON** is stored in plain text in the DB (`FinancialCore.cs:837`). Store it in configuration/secret storage instead.

### P3-L. Positive security notes (keep these)

- PBKDF2-SHA256 210k iterations, constant-time compare.
- SameSite=Strict + Origin/Sec-Fetch checks, CSP without inline scripts, HTTPS-only API.
- The cookie carries only 4 claims, and permissions are reloaded from the DB on every request (`securityVersion` revokes sessions).
- Rider portal: one-time 64-hex codes + last 4 digits of the phone + dual rate limit.
- Data directory refused inside wwwroot. Uploads whitelisted (pdf/png/jpg) with server-chosen content type.
- CSV export guards against formula injection.

---

## 5. Findings — architecture & performance

### P2-M. Whole-table scans on every request

`Store.List(c, kind)` loads and parses **every** record of a kind, and the code calls it repeatedly:

- **Balances:** `AccountingPolicy.Balance` scans all journal lines on every balance check (several per transaction).
- **Payroll preview:** loads all work rows, ledger and debts **per rider**, then again for the fingerprint, which is O(riders × records).
- **Signed previews:** `FinancialImports.Signature` serialises **all** debts, riders, settlements, journals, runs and ledger to sign one preview. Any unrelated change by another user (one collection) invalidates everyone's open preview.
- **Indexes:** SQL Server has only the `kind` index; the unique indexes exist on SQLite only.

At ~870 riders and a year of journals this will become slow (seconds per request) and starts to compete with the global write lock.

**Suggestion, in order of value:**
1. A `journal_line` projection table (account, sponsor, iqama, debtId, period, debit, credit), written in the same transaction, with indexes. Balances become one `SUM` query.
2. Sign only the **rows that the preview touches** (the debts/riders/vaults involved), not the whole database.
3. Add the same unique indexes on SQL Server (filtered indexes on computed `JSON_VALUE` columns).
4. A batch payroll preview that loads data once per period.

### P2-N. Hard-coded business configuration

The chart of accounts, the 3 entities, categories, departments, platforms and default costs are C# arrays. Adding a fourth entity, a bank account or a new platform needs a code release.

That's acceptable while rules are still changing, but plan to move them into versioned configuration documents managed by admins (with history), keeping the validation rules in code.

### P2-O. Two financial systems

The main ERP (`01_ERP_System_Gate_Platform`, Logistic-ERP) also has treasury/sponsor logic, and Codex has prepared ERP patches. Running two ledgers for the same companies invites double counting.

**Decide explicitly which system is the book of record** for treasury and rider debts. The other one should consume it (API/export), not post its own entries.

### P3-P. Maintainability

- `enterprise.ts` is 150 KB in one file; `FinancialCore.cs` 84 KB; many lines exceed 500 characters. Split by feature (debts, treasury, users, statements) on both sides.
- The rider mobile app is embedded as C# string constants inside `FinancialCore.cs`. Move it to `wwwroot/rider-app/` so it's linted, tested and cacheable.
- The frontend builds HTML with template strings and `innerHTML` (≈180 `esc(` calls). It works, but one forgotten `esc` is an XSS. A tiny `html\`\`` tagged-template helper that escapes by default would remove that class of bug.

---

## 6. Findings — process, delivery & operations

### P1-Q. Auto-deploy to production on every merge, without a staging environment

The same CI job tests, uploads to production, then verifies. There's no staging site, no GitHub Environment approval gate, and no automatic rollback. Today's 0.7.0 run is an example: it was marked "failure" after the files were already live (the payload verifier failed, fixed in PR11).

**Suggestion:**
- A MonsterASP **staging** site with a copy of anonymised data.
- Deploy `main` → staging automatically.
- Production only through a protected `production` environment with a required reviewer (the owner).
- Keep the previous artifact and add a one-click "redeploy previous version" workflow.

### P1-R. Off-site, restorable backups

Release checkpoints (DB + keys + local settings) are stored in `App_Data` on **the same server**, with no rotation. If the host or account is lost, everything is lost.

**Suggestion:**
- A scheduled encrypted backup to a separate location (another provider or cloud storage).
- Retention (daily 14 / monthly 12).
- A **tested restore** at least monthly.

### P2-S. Several AI agents on one folder

Codex, Antigravity and Claude all work on the same directory, which isn't a git repository locally. Changes are synchronised by SHA comparison scripts. That's how six files were found modified locally but not on GitHub on 26 Oct.

**Suggestion:**
- Make the local folder a proper git clone, with one branch per task.
- Only one agent works at a time (the user says which).
- Agents never edit `main` directly.

### P2-T. Documentation is hard to use

`PROJECT_CONTINUITY.md` is 37k tokens of dense Arabic, much of it without spaces between words. There are also 20+ overlapping `*_AR.md` handoff and upgrade files.

**Suggestion:** keep:
1. a one-page `STATUS.md` (current version, open items, next step);
2. `CHANGELOG.md`;
3. `docs/accounting-rules.md` (the policy decisions, such as fees = 4101 and independent banks);
4. an archive folder for the rest.

### P3-U. Tests

Good coverage of the accounting rules (40 offline + HTTP suites on SQLite and SQL Server + browser). Missing:

- property/fuzz tests for the per-entity balance invariant;
- a **load test** (870 riders × 12 months);
- tests for the reversal/period-close features once added;
- a test that every route has a permission (see P2-J).

`dotnet test` can't run on the local machine (socket permission), so CI is the only full gate. Worth fixing so developers can run everything locally.

---

## 7. Feature gaps (functional)

| Gap | Why it matters |
|---|---|
| Bulk payroll approval for a period (with the per-rider fingerprints) | 870 riders approved one by one isn't practical |
| Payroll run reversal / re-approval | Mistakes are permanent today (P1-B) |
| Month close & financial reports (trial balance, P&L and balance sheet per entity, intercompany matrix) | Data exists in the journal; no report screens |
| Bank reconciliation (import a bank statement, match entries) | Bank balances are book-only today |
| Jahez payout reconciliation (Jahez statement vs. 1240/2140) | Main source of cash differences |
| FCM push notifications / Google Play app | Settings are stored but not connected (documented honestly) |
| Rider self-service: download payslip PDF, dispute a deduction | Reduces support load |
| Admin password reset / user self-service | P1-I |
| Audit-log viewer with filters and export | Audit is written, but `/api/audit` returns everything unfiltered |

---

## 8. Suggested order of work

1. **Now (before real 0.7.0 money flows):**
   - P1-A, Jahez release/netting.
   - P1-C/D, opening controls.
   - P1-I, password reset + lockout + MFA for finance.
   - P1-R, off-site backup.
2. **Next 2 weeks:**
   - P1-B, reversal + period close.
   - P1-Q, staging + protected production.
   - P2-E, waiver/transfer thresholds.
   - P2-G, payroll opening screen.
   - Bulk payroll approval.
3. **Next month:**
   - P2-M, journal-line projection + targeted preview signatures.
   - P2-F, reconciliation dashboard.
   - Financial reports.
   - P2-J, route-level permissions.
4. **Then:**
   - Bank and Jahez statement reconciliation.
   - Split the large files.
   - Configuration-driven chart of accounts.
   - Decide ERP vs. Payroll as the book of record.

---

## 9. Evidence checked for this review

- Local build: `dotnet build Payroll.Api` → 0 warnings, 0 errors.
- `dotnet run --project tests/Engine.Tests -- --accounting-review` → 40/40 accounting tests, shared-cost test, 52 legacy checks passed.
- Live `/health` → 0.7.0, `dataPreservationVerified=true`, `releaseCheckpointPresent=true`.
- GitHub: main `bf26baf` (PR10 merged), PR11 merged 10:34 UTC; deployment run `40308430474` was still running at the time of writing (started by Codex).
- I did **not** compare the local files against the new `main` after PR11, and I did not read production data.
