# Manual journals (BR28)

`POST /api/finance/journals/manual` requires finance.journal.create and `{companyId,date,reference,description,lines,attachment?,odooVoucherNo?}`. Date is yyyy-MM-dd. Each line is `{accountCode,debit,credit,note?}` on the active target chart. One company,2–200 lines, exact cents and equal debit/credit totals. Financial scope and mapped cash/bank account scope are enforced. Closed periods are refused both on draft creation and approval.

The response is a `manual_journal` draft, separate from posted `journal` records, so it affects no balances. No edit/delete endpoint exists. `POST /api/finance/journals/manual/{id}/approve` requires finance.journal.approve and a different user from the maker (including Admin). It posts once, preserving the requested accounting date and attachment reference. Repeats do not create another journal.

`GET /api/finance/journals/manual?status=draft|approved` uses normal pagination. The actor needs create or approve permission and scope for every company/account line. Both permissions default to FinancialManager/FinancialAccountant; a one-time startup seed reaches existing users, respects explicit denies and does not undo later revocation.

Optional attachment is `{fileName,dataBase64}`: PDF,PNG orJPEG, ≤2MiB, checked by its header. It is stored privately in the same transaction and never returned inline in a draft/list. Authenticated `GET …/{id}/attachment` returns a download with a server-controlled filename, after the same scope checks.

`POST …/{id}/reverse {reason,date}` creates a separate reversal **draft** in an open period; the original stays immutable. Another user approves the returned draft ID through the same approve endpoint, creating the reversing journal. This preserves maker-checker for corrections. System-generated journal IDs are refused by this workflow; correct them through their originating feature. Duplicate reversal requests and reversal-of-reversal are refused.
