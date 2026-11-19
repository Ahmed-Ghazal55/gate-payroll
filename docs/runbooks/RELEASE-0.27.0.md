# Release 0.27.0 (2026-11-19)

## What changed for users
- Menu «التشغيل والأسطول» now has 4 entries: المدن والمناطق، السكن والإسكان، المركبات والتسليمات، الوقود. The page no longer repeats them as a top tab bar (O1).
- Everything here is read live from the ERP, view only: no posting, no payments, no uploads (O2).
- Cities: per operating city, the active registrations, cars / motorbikes, riders, working apps, platform accounts total / active / assigned; city details split by registration and by app (O3).
- Housing: rooms, capacity, residents, residents per room, occupancy %; rooms drawer with residents (full iqama, link to the 360 window) (O4).
- Vehicles: period report like the ERP's «تقارير فترات العهد والتعيينات» (period picker, vehicle ↔ actual-rider view, operating days, amount to collect, spare parts, oil changes count / cost, mileage), summary and detailed export; VIN only in the vehicle details drawer; oil changes list; purchase invoices list (all-company users only) (O5).
- Fuel (new): cards, monthly usage per card (liters, before VAT, VAT, total), consuming riders, card hand-over log; export (O6). The fuel cap has no ERP source yet («لا يوجد مصدر بعد»).
- Removed (O7): العهد العينية، الكفالات، القضايا والمنازعات، حسابات المنصات. Old links redirect: cases → workforce cases tab; accounts → workforce (accounts stay in the 360 window); custody / guarantees → vehicles.

## Rules used (from the ERP code)
- Operating days and amount to collect: the ERP's own period rule (Riyadh day, pickup after 14:00 counts from the next day), clipped to the chosen period; car 1800 / motorbike 800 SAR a month ÷ 30 (the ERP's numbers).
- Spare parts = ERP vehicle expenses of type material from workshop material usage; reversed expenses are netted out; oil-change costs are shown separately.

## Ops step for Ghazal (after deploy)
- None required. Optional: `Operations:ExportTimeoutSeconds` (≥ 30, default 120) if an export is slow.

## Live safety
- No schema, seed, data or setting change on first boot. The new endpoints only run read-only SELECT queries against the live ERP (cities, housing, vehicles, maintenance, fuel tables).
