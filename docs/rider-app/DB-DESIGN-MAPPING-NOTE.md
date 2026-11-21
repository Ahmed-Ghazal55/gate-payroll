# DB Design Mapping Note for Planner (Rider App v2)

> **Scope:** Branch `feat/rider-app-v2` planner handoff note (from Review RA1 / Task M3).  
> **Note:** Main repo design file `30-db-design.md` was intentionally NOT edited on this branch; add these items to §12.2 before the relational record split.

## Pending §12.2 Record-Split Mappings

| Current `records.kind` / JSON Field | Design Ref | Target Table / Action Needed Before Record Split |
|---|---|---|
| `mobile_broadcast` → `readReceipts[]` & `acknowledgedBy[]` JSON arrays | `30-db-design.md:215` | Model read receipts and acknowledgements as rows in child table `app.broadcast_ack` (one row per `(broadcast_id, rider_iqama)` with `read_at`, `acknowledged_at`, `session_id`) instead of growing JSON arrays inside `mobile_broadcast`. |
| `rider_service_request` (new kind: `annual_leave`, `iban_update`, `data_update`, `statement_request`, `complaint`, `appointment` + `timeline[]` / `replies[]`) | `30-db-design.md:288` | Add a dedicated target table mapping in §12.2 (e.g., `app.rider_service_request` + child rows for `timeline` / `replies`) before the record split. |
| `rider_service_request` (`requestType = "iban_update"`) final approval writing `rider.verifiedIban` directly | `30-db-design.md:61` | Dedicated per-step permissions (`riders.iban_data.approve_first`, `riders.iban_data.approve_second`) and user-ID maker-checker are enforced in M3; map IBAN change approvals to `hr.iban_change` rows during the record split instead of mutating `rider.verifiedIban` directly. |

## Pre-Split Indexing Note (`dbo.records`)

- Task M3 replaced full `Store.List` scans on rider read paths with filtered SQL queries (`Store.ListWhere`, `Store.ListByIds`, `Store.ListBroadcastsForRider`, `Store.ListIdsByPrefix`) and added SQLite partial indexes (`ix_pay_cycle_line_rider_period`, `ix_pay_cycle_line_period`, `ix_pay_cycle_period`, `ix_mobile_broadcast_target`, `ix_rider_ask_iqama`, `ix_rider_sreq_iqama`, `ix_mobile_session_iqama`, `ix_submission_iqama`, `ix_advance_req_iqama`, `ix_push_outbox_status`).
- On SQL Server (`dbo.records`), `JSON_VALUE` filters are executed in SQL (avoiding full-table transfer to app memory), and if go-live happens before the relational table split, add persisted computed-column indexes matching these filters on `dbo.records`.
