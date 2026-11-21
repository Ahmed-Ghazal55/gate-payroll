# Release 0.31.0 (2026-11-21)

New read-only live endpoints that read pay-run inputs straight from Omar's ERP, for the capture stage and later screens (tasks L1 + L2 by Cursor, reviewed and released by Claude in DEP9). No screen calls them yet.

## What changed for users
Nothing visible on screens. Nine new GET endpoints, all read-only, all need `pay.view`; `allowedActions` lists `refresh` (and `capture` where the user has `pay.manage`). Every list is paged in SQL (`page`, `pageSize` up to 200) and filtered by the user's companies in SQL.

Ninja (L1, also need the Ninja platform in the user's scope):
- `GET /api/pay/apps/ninja/live/daily-performance?from&to`: orders and hours per account and day. `coverage` gives the first and last day found and `hasGapAtPeriodEnd` (October: Omar has data to the 29th).
- `GET /api/pay/apps/ninja/live/daily-deductions?from&to`: daily deductions keyed by the actual rider of that day, with the same `coverage`.
- `GET /api/pay/apps/ninja/live/payment-methods`: Omar's `PaymentModel` per account and per open assignment (PayPerOrder / Salary).
- `GET /api/pay/apps/ninja/live/account-bearers?from&to`: owning registration vs actual rider per assignment period.

Deduction and eligibility sources (L2):
- `GET /api/pay/sources/fuel-consumption?month`: fuel per rider and card for a month, with totals.
- `GET /api/pay/sources/vehicle-assignment-periods?from&to`: vehicle periods per rider.
- `GET /api/pay/sources/housing-periods?from&to`: housing periods per rider, with days inside the period.
- `GET /api/pay/sources/keeta-eligibility?month`: Keeta EXPECTED eligibility (newest snapshot per account, rider, month, source); `approval = expected_only`, never an approval.
- `GET /api/pay/sources/hs-stamp-queue`: HungerStation WalletPending / DeferredNextMonth accounts and sealed / pending-seal financial events (read side only).

Facts only: none of these endpoints calculates pay, posts a journal or writes anywhere.

## Known limits (follow-up, not blockers)
- Ninja readers skip accounts that have no company (`SponsorId` empty) for every user, and the coverage count does not show them.
- The Keeta eligibility view columns were tested on a synthetic schema only; if the live view differs, the endpoint returns the usual "ERP source" error until fixed.
- L2 SQL Server tests check company scope for fuel only; vehicles, housing, Keeta and HS rely on the same scope helper.

## Open items for Omar (request file)
- No dated account-owner history: only the current `RegisteredEmployeeId`; holder dates come from assignment periods.
- No separate payment-method field beyond `PaymentModel` (it is the scheme name).
- Fuel usage has no vehicle link: only the card's plate text.
- Keeta `SettlementModel` spelling: the live value may be an Arabic typo; returned raw.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes.
- All ERP reads go through `Operations.OpenErp`, which checks the login is read-only (`SourceReader.VerifyReadOnly`, cached for a short time).
