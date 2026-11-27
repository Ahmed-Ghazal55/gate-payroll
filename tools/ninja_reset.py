#!/usr/bin/env python3
"""Reset Ninja payroll drafts so the app looks never tried.

Connects with PAYROLL_SQL (or GATE_PAYROLL_SQL / ConnectionStrings__DefaultConnection).
Default = dry-run. Pass --apply to delete in ONE transaction and write audit
'platform.ninja.reset'. Never touches platform accounts, riders, settings,
schemes, contracts/terms, other apps, or users.

Discovery follows platform/app/cycle/invoice references (no name/IBAN/amount output).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import uuid
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Iterable
from urllib.parse import unquote

# Optional: pymssql (preferred). Fallback: docker sqlcmd is not used — require pymssql.
try:
    import pymssql
except ImportError:  # pragma: no cover
    pymssql = None  # type: ignore

APP = "ninja"
PLATFORM = "Ninja"
PLATFORM_CODE = "NINJA"
AUDIT_ACTION = "platform.ninja.reset"

# Master / other-app kinds that must never be deleted even if body mentions Ninja.
PROTECTED_KINDS = frozenset(
    {
        "platform_account",
        "ninja_account",
        "rider",
        "rider_account_assignment",
        "setting",
        "pay_scheme",
        "pay_terms",
        "pay_setting",
        "pay_eligibility",
        "user",
        "company",
        "commercial_registration",
        "role_permissions",
        "city",
        "housing",
        "vehicle",
        "vehicle_assignment",
        "ninja_daily",  # ERP ops mirror — not a payroll draft
        "ninja_daily_deduction",
        "keeta_daily",
        "chefz_daily",
        "chefz_account",
        "chefz_settlement",
        "jahez_ledger_entry",
        "jahez_transaction",
        "employee",
        "employee_document",
        "audit",  # keep history; we append one new row
        "campaign",
        "submission",
        "evaluation_staff",
        "evaluation_round",
    }
)

# Document-store kinds that are Ninja payroll drafts when linked.
DRAFT_KINDS = (
    "platform_invoice",
    "platform_invoice_line",
    "platform_deduction",
    "platform_dispute",
    "platform_allocation",
    "platform_invoice_action",
    "pay_cycle",
    "pay_cycle_line",
    "journal",
    "manual_journal",
    "voucher_index",
    "odoo_voucher_index",
    "debt",
    "debt_settlement",
    "debt_transfer",
    "operations_import",
    "pay_assignment",
    "pay_consolidation",
    "finance_adjustment",
)

# Money workflow statuses that mean the row left the draft sandbox.
CYCLE_BLOCK_STATUSES = frozenset(
    {
        "approved",
        "partially_approved",
        "salary_approved",
        "ready",
        "paid",
        "bank_file",
        "sent",
    }
)
INVOICE_POSTED_HINTS = frozenset({"posted", "paid"})


@dataclass
class Hit:
    kind: str
    id: str
    period: str = ""
    table: str = "records"  # records | sal.* | fin.* | pay.* | debt.* | approvals.*
    blocker_reason: str = ""
    meta: dict[str, Any] = field(default_factory=dict)


@dataclass
class Plan:
    hits: list[Hit] = field(default_factory=list)
    blockers: list[Hit] = field(default_factory=list)
    baselines: dict[str, int] = field(default_factory=dict)

    def by_kind(self) -> dict[str, list[Hit]]:
        out: dict[str, list[Hit]] = defaultdict(list)
        for h in self.hits:
            out[h.kind].append(h)
        return out

    def by_period(self) -> dict[str, int]:
        out: dict[str, int] = defaultdict(int)
        for h in self.hits:
            out[h.period or "(none)"] += 1
        return out


def parse_conn(cs: str) -> dict[str, str]:
    """Parse ADO.NET / ODBC-style connection string into host/port/db/user/password."""
    parts: dict[str, str] = {}
    for chunk in cs.split(";"):
        chunk = chunk.strip()
        if not chunk or "=" not in chunk:
            continue
        k, v = chunk.split("=", 1)
        v = v.strip()
        if len(v) >= 2 and v[0] == v[-1] and v[0] in "'\"":
            v = v[1:-1].replace(v[0] * 2, v[0])
        parts[k.strip().lower()] = v
    server = parts.get("server") or parts.get("data source") or ""
    host, port = server, "1433"
    if "," in server:
        host, port = server.rsplit(",", 1)
    elif ":" in server and not server.startswith("tcp:"):
        host, port = server.rsplit(":", 1)
    host = host.replace("tcp:", "").strip()
    db = parts.get("database") or parts.get("initial catalog") or ""
    user = parts.get("user id") or parts.get("uid") or parts.get("user") or ""
    password = parts.get("password") or parts.get("pwd") or ""
    if not host or not db or not user:
        raise SystemExit("Connection string missing Server/Database/User Id")
    return {"host": host, "port": port, "db": db, "user": user, "password": password}


def connect(cs: str):
    if pymssql is None:
        raise SystemExit(
            "pymssql is required. Example:\n"
            "  python3 -m venv ~/.cache/agents-runs/nrst1-venv\n"
            "  ~/.cache/agents-runs/nrst1-venv/bin/pip install pymssql\n"
            "  ~/.cache/agents-runs/nrst1-venv/bin/python tools/ninja_reset.py"
        )
    cfg = parse_conn(cs)
    return pymssql.connect(
        server=cfg["host"],
        port=int(cfg["port"]),
        user=cfg["user"],
        password=cfg["password"],
        database=cfg["db"],
        login_timeout=15,
        timeout=120,
    )


def jload(body: str | None) -> dict[str, Any]:
    if not body:
        return {}
    try:
        val = json.loads(body)
        return val if isinstance(val, dict) else {}
    except json.JSONDecodeError:
        return {}


def period_of(doc: dict[str, Any], fallback: str = "") -> str:
    for key in ("period", "periodMonth", "period_month"):
        v = doc.get(key)
        if v is None:
            continue
        s = str(v)
        if len(s) >= 7 and s[4] == "-":
            return s[:7]
    date = str(doc.get("date") or doc.get("reportDate") or "")
    if len(date) >= 7 and date[4] == "-":
        return date[:7]
    return fallback


def table_exists(cur, schema: str, name: str) -> bool:
    cur.execute(
        """
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA=%s AND TABLE_NAME=%s
        """,
        (schema, name),
    )
    return cur.fetchone() is not None


def fetch_all(cur, sql: str, params: tuple[Any, ...] | None = None) -> list[tuple]:
    cur.execute(sql, params or ())
    return list(cur.fetchall())


def is_ninja_invoice_doc(doc: dict[str, Any], rid: str) -> bool:
    if rid.startswith("ninja-invoice:"):
        return True
    plat = str(doc.get("platform") or "")
    app = str(doc.get("app") or doc.get("appCode") or doc.get("app_code") or "").lower()
    code = str(doc.get("platformCode") or "").upper()
    return plat == PLATFORM or app == APP or code == PLATFORM_CODE


def is_ninja_cycle_doc(doc: dict[str, Any]) -> bool:
    app = str(doc.get("app") or "").lower()
    if app == APP:
        return True
    invoices = doc.get("invoices")
    if isinstance(invoices, list):
        for inv in invoices:
            if not isinstance(inv, dict):
                continue
            if str(inv.get("app") or "").lower() == APP:
                return True
            if str(inv.get("invoiceId") or "").startswith("ninja-invoice:"):
                return True
            if str(inv.get("platform") or "") == PLATFORM:
                return True
    return str(doc.get("platform") or "") == PLATFORM


def baseline_counts(cur) -> dict[str, int]:
    """Counts that must stay unchanged after apply (other apps + masters)."""
    out: dict[str, int] = {}
    pairs = [
        ("records.keeta_like", "SELECT COUNT(*) FROM dbo.records WHERE kind LIKE 'keeta%'"),
        ("records.chefz_daily", "SELECT COUNT(*) FROM dbo.records WHERE kind='chefz_daily'"),
        ("records.jahez_ledger_entry", "SELECT COUNT(*) FROM dbo.records WHERE kind='jahez_ledger_entry'"),
        ("records.platform_account", "SELECT COUNT(*) FROM dbo.records WHERE kind='platform_account'"),
        ("records.ninja_account", "SELECT COUNT(*) FROM dbo.records WHERE kind='ninja_account'"),
        ("records.ninja_daily", "SELECT COUNT(*) FROM dbo.records WHERE kind='ninja_daily'"),
        ("records.rider", "SELECT COUNT(*) FROM dbo.records WHERE kind='rider'"),
        ("records.pay_scheme", "SELECT COUNT(*) FROM dbo.records WHERE kind='pay_scheme'"),
        ("records.pay_terms", "SELECT COUNT(*) FROM dbo.records WHERE kind='pay_terms'"),
        ("records.setting", "SELECT COUNT(*) FROM dbo.records WHERE kind='setting'"),
        ("records.user", "SELECT COUNT(*) FROM dbo.records WHERE kind='user'"),
        (
            "records.platform_invoice.keeta",
            "SELECT COUNT(*) FROM dbo.records WHERE kind='platform_invoice' AND JSON_VALUE(body,'$.platform')='Keeta'",
        ),
        (
            "records.platform_invoice.ninja",
            "SELECT COUNT(*) FROM dbo.records WHERE kind='platform_invoice' AND (JSON_VALUE(body,'$.platform')='Ninja' OR id LIKE 'ninja-invoice:%')",
        ),
        (
            "records.pay_cycle.ninja",
            "SELECT COUNT(*) FROM dbo.records WHERE kind='pay_cycle' AND (JSON_VALUE(body,'$.app')='ninja' OR LOWER(body) LIKE '%ninja-invoice:%')",
        ),
    ]
    for key, sql in pairs:
        cur.execute(sql)
        out[key] = int(cur.fetchone()[0])
    if table_exists(cur, "sal", "platform_invoice"):
        cur.execute("SELECT app_code, COUNT(*) FROM sal.platform_invoice GROUP BY app_code")
        for app, n in cur.fetchall():
            out[f"sal.platform_invoice.{app}"] = int(n)
        cur.execute("SELECT COUNT(*) FROM sal.platform_invoice WHERE app_code='ninja'")
        out["sal.platform_invoice.ninja"] = int(cur.fetchone()[0])
    if table_exists(cur, "sal", "pay_run"):
        cur.execute("SELECT app_code, COUNT(*) FROM sal.pay_run GROUP BY app_code")
        for app, n in cur.fetchall():
            out[f"sal.pay_run.{app}"] = int(n)
    return out


def discover(conn) -> Plan:
    cur = conn.cursor()
    plan = Plan(baselines=baseline_counts(cur))
    hits: dict[tuple[str, str], Hit] = {}

    def add(hit: Hit) -> None:
        key = (hit.table, hit.id)
        if key in hits:
            if hit.blocker_reason and not hits[key].blocker_reason:
                hits[key].blocker_reason = hit.blocker_reason
            return
        hits[key] = hit
        if hit.blocker_reason:
            plan.blockers.append(hit)

    # --- 1) Seed: Ninja invoices (document store) ---
    invoice_ids: set[str] = set()
    cur.execute("SELECT id, body FROM dbo.records WHERE kind='platform_invoice'")
    for rid, body in cur.fetchall():
        doc = jload(body)
        if not is_ninja_invoice_doc(doc, rid):
            continue
        invoice_ids.add(rid)
        reason = ""
        status = str(doc.get("status") or "").lower()
        approval = str(doc.get("approvalStatus") or "").lower()
        journal_id = str(doc.get("journalEntryId") or doc.get("journalId") or "")
        pay_cycle = str(doc.get("payCycleId") or "")
        # Hard blockers = money that left the draft sandbox (posted / paid / bank-file / cycle-bound).
        # Invoice approvalStatus=approved with no journal is still wiped (Ghazal NRST1); noted in meta.
        if journal_id:
            reason = "invoice has journalEntryId (posted)"
        elif status in INVOICE_POSTED_HINTS or approval in ("paid", "bank_file"):
            reason = f"invoice status/approval={status or approval}"
        elif pay_cycle:
            reason = "invoice bound to payCycleId"
        add(
            Hit(
                kind="platform_invoice",
                id=rid,
                period=period_of(doc),
                blocker_reason=reason,
                meta={
                    "approvalStatus": approval,
                    "status": status,
                    "hasJournal": bool(journal_id),
                    "note": (
                        "approved_unposted_included"
                        if approval == "approved" and not journal_id and not reason
                        else ""
                    ),
                },
            )
        )

    # --- 2) Typed sal.platform_invoice ---
    typed_invoice_ids: set[str] = set()
    if table_exists(cur, "sal", "platform_invoice"):
        cur.execute(
            """
            SELECT id, app_code, period_month, status, approved_at, journal_id
            FROM sal.platform_invoice WHERE LOWER(app_code)='ninja'
            """
        )
        for rid, app, pm, status, approved_at, journal_id in cur.fetchall():
            rid_s = str(rid)
            typed_invoice_ids.add(rid_s)
            period = str(pm)[:7] if pm else ""
            reason = ""
            st = int(status) if status is not None else 0
            # 3=approved-ish, 5 often posted in money workflow; journal_id => posted link
            if journal_id:
                reason = "sal.platform_invoice.journal_id set"
            elif st >= 3 and approved_at is not None:
                # approved typed invoice — still a draft wipe target per Ghazal, but flag if looks paid/posted
                if st >= 5:
                    reason = f"sal.platform_invoice status={st} (posted/closed)"
            add(
                Hit(
                    kind="sal.platform_invoice",
                    id=rid_s,
                    period=period,
                    table="sal.platform_invoice",
                    blocker_reason=reason,
                    meta={"status": st, "hasJournal": bool(journal_id)},
                )
            )

    all_invoice_ids = invoice_ids | typed_invoice_ids

    # --- 3) Follow invoice children in records ---
    if all_invoice_ids:
        cur.execute(
            """
            SELECT id, kind, body FROM dbo.records
            WHERE kind IN (
              'platform_invoice_line','platform_deduction','platform_dispute',
              'platform_allocation','platform_invoice_action','voucher_index',
              'odoo_voucher_index','debt','debt_settlement','debt_transfer',
              'finance_adjustment','operations_import'
            )
            """
        )
        for rid, kind, body in cur.fetchall():
            if kind in PROTECTED_KINDS:
                continue
            doc = jload(body)
            linked = False
            for key in ("invoiceId", "recordId", "sourceId", "sourceRef", "platformInvoiceId"):
                val = str(doc.get(key) or "")
                if val in all_invoice_ids or val.startswith("ninja-invoice:"):
                    linked = True
                    break
            if not linked and str(doc.get("platform") or "") == PLATFORM and kind in (
                "platform_deduction",
                "platform_dispute",
                "platform_allocation",
                "platform_invoice_action",
                "debt",
                "debt_settlement",
            ):
                # Only when invoiceId empty but platform is Ninja and (for debt) looks draft-tied
                if kind.startswith("platform_") or str(doc.get("invoiceId") or "") in all_invoice_ids:
                    linked = True
            if not linked and kind in ("voucher_index", "odoo_voucher_index"):
                if str(doc.get("recordId") or "") in all_invoice_ids:
                    linked = True
            if not linked:
                continue
            add(Hit(kind=kind, id=rid, period=period_of(doc)))

    # Lines/deductions by invoiceId JSON path (belt + suspenders)
    for kind in ("platform_invoice_line", "platform_deduction", "platform_dispute"):
        cur.execute(
            """
            SELECT id, body FROM dbo.records
            WHERE kind=%s AND (
              JSON_VALUE(body,'$.invoiceId') LIKE 'ninja-invoice:%'
              OR JSON_VALUE(body,'$.platform')='Ninja'
            )
            """,
            (kind,),
        )
        for rid, body in cur.fetchall():
            doc = jload(body)
            add(Hit(kind=kind, id=rid, period=period_of(doc)))

    # --- 4) Pay cycles that reference Ninja ---
    cycle_ids: set[str] = set()
    cur.execute("SELECT id, body FROM dbo.records WHERE kind='pay_cycle'")
    for rid, body in cur.fetchall():
        doc = jload(body)
        if not is_ninja_cycle_doc(doc):
            # also: any invoice id overlap
            invs = doc.get("invoices")
            overlap = False
            if isinstance(invs, list):
                for inv in invs:
                    if isinstance(inv, dict) and str(inv.get("invoiceId") or "") in all_invoice_ids:
                        overlap = True
                        break
            if not overlap:
                continue
        cycle_ids.add(rid)
        status = str(doc.get("status") or "").lower()
        reason = ""
        if status in CYCLE_BLOCK_STATUSES:
            reason = f"pay_cycle status={status}"
        add(
            Hit(
                kind="pay_cycle",
                id=rid,
                period=period_of(doc),
                blocker_reason=reason,
                meta={"status": status},
            )
        )

    if cycle_ids:
        cur.execute("SELECT id, body FROM dbo.records WHERE kind='pay_cycle_line'")
        for rid, body in cur.fetchall():
            doc = jload(body)
            cid = str(doc.get("cycleId") or doc.get("payCycleId") or "")
            if cid in cycle_ids or str(doc.get("app") or "").lower() == APP:
                add(Hit(kind="pay_cycle_line", id=rid, period=period_of(doc)))

    # --- 5) Journals (document store) linked to Ninja ---
    cur.execute("SELECT id, kind, body FROM dbo.records WHERE kind IN ('journal','manual_journal')")
    for rid, kind, body in cur.fetchall():
        doc = jload(body)
        linked = (
            str(doc.get("platform") or "") == PLATFORM
            or str(doc.get("app") or "").lower() == APP
            or str(doc.get("sourceId") or "") in all_invoice_ids
            or str(doc.get("sourceRef") or "") in all_invoice_ids
            or any(str(doc.get(k) or "") in cycle_ids for k in ("cycleId", "payCycleId"))
            or any(i in (body or "") for i in all_invoice_ids)
        )
        if not linked:
            continue
        status = str(doc.get("status") or "").lower()
        reason = ""
        if status in ("posted", "reversed") or doc.get("postedAt"):
            reason = f"{kind} status={status or 'posted'}"
        add(
            Hit(
                kind=kind,
                id=rid,
                period=period_of(doc),
                blocker_reason=reason,
                meta={"status": status},
            )
        )

    # --- 6) Typed fin.journal linked by source_id / invoice journal_id ---
    if table_exists(cur, "fin", "journal"):
        journal_ids: set[str] = set()
        # From typed invoices
        for h in hits.values():
            jid = h.meta.get("hasJournal") and None
            _ = jid
        if typed_invoice_ids and table_exists(cur, "sal", "platform_invoice"):
            cur.execute(
                """
                SELECT journal_id FROM sal.platform_invoice
                WHERE LOWER(app_code)='ninja' AND journal_id IS NOT NULL
                """
            )
            for (jid,) in cur.fetchall():
                if jid:
                    journal_ids.add(str(jid))
        # By source_id matching invoice/cycle guids (typed only; doc ids are not guids)
        cur.execute(
            """
            SELECT id, status, posted_at, source_type, source_id, period_month, reverses_journal_id
            FROM fin.journal
            WHERE LOWER(CAST(source_type AS nvarchar(100))) LIKE '%ninja%'
               OR LOWER(purpose) LIKE '%ninja%'
            """
        )
        for rid, status, posted_at, source_type, source_id, pm, rev in cur.fetchall():
            journal_ids.add(str(rid))
            st = int(status) if status is not None else 0
            reason = ""
            if st in (2, 3) or posted_at is not None:  # posted / reversed
                reason = f"fin.journal status={st} (posted/reversed)"
            add(
                Hit(
                    kind="fin.journal",
                    id=str(rid),
                    period=str(pm)[:7] if pm else "",
                    table="fin.journal",
                    blocker_reason=reason,
                    meta={"status": st, "source_type": str(source_type), "reverses": str(rev or "")},
                )
            )
        # Journals whose source_id is a typed ninja invoice
        if typed_invoice_ids:
            # batch IN
            ids = list(typed_invoice_ids)
            for i in range(0, len(ids), 50):
                chunk = ids[i : i + 50]
                placeholders = ",".join(["%s"] * len(chunk))
                cur.execute(
                    f"""
                    SELECT id, status, posted_at, period_month
                    FROM fin.journal WHERE CAST(source_id AS nvarchar(64)) IN ({placeholders})
                    """,
                    tuple(chunk),
                )
                for rid, status, posted_at, pm in cur.fetchall():
                    st = int(status) if status is not None else 0
                    reason = ""
                    if st in (2, 3) or posted_at is not None:
                        reason = f"fin.journal status={st} (posted/reversed)"
                    add(
                        Hit(
                            kind="fin.journal",
                            id=str(rid),
                            period=str(pm)[:7] if pm else "",
                            table="fin.journal",
                            blocker_reason=reason,
                            meta={"status": st},
                        )
                    )
                    journal_ids.add(str(rid))
        if journal_ids and table_exists(cur, "fin", "journal_line"):
            for i in range(0, len(list(journal_ids)), 50):
                chunk = list(journal_ids)[i : i + 50]
                ph = ",".join(["%s"] * len(chunk))
                cur.execute(
                    f"SELECT id, journal_id FROM fin.journal_line WHERE CAST(journal_id AS nvarchar(64)) IN ({ph})",
                    tuple(chunk),
                )
                for lid, jid in cur.fetchall():
                    add(
                        Hit(
                            kind="fin.journal_line",
                            id=str(lid),
                            table="fin.journal_line",
                            meta={"journal_id": str(jid)},
                        )
                    )

    # --- 7) sal.pay_run + children + captures ---
    pay_run_ids: set[str] = set()
    if table_exists(cur, "sal", "pay_run"):
        cur.execute(
            """
            SELECT id, period_month, status, capture_state
            FROM sal.pay_run WHERE LOWER(app_code)='ninja'
            """
        )
        for rid, pm, status, cap in cur.fetchall():
            pay_run_ids.add(str(rid))
            st = int(status) if status is not None else 0
            reason = ""
            # 2/3/5 = pending-approve / approved / closed-ish in money workflow
            if st in (2, 3, 5):
                reason = f"sal.pay_run status={st} (approved/closed)"
            add(
                Hit(
                    kind="sal.pay_run",
                    id=str(rid),
                    period=str(pm)[:7] if pm else "",
                    table="sal.pay_run",
                    blocker_reason=reason,
                    meta={"status": st, "capture_state": int(cap or 0)},
                )
            )
        if pay_run_ids:
            for child_table, fk in (
                ("pay_run_rider", "pay_run_id"),
                ("pay_run_item", None),  # via rider
                ("rider_request", None),
            ):
                if not table_exists(cur, "sal", child_table):
                    continue
                if child_table == "pay_run_rider":
                    ph = ",".join(["%s"] * len(pay_run_ids))
                    cur.execute(
                        f"SELECT id, period_month FROM sal.pay_run_rider WHERE CAST(pay_run_id AS nvarchar(64)) IN ({ph})",
                        tuple(pay_run_ids),
                    )
                    rider_ids = []
                    for rid, pm in cur.fetchall():
                        rider_ids.append(str(rid))
                        add(
                            Hit(
                                kind="sal.pay_run_rider",
                                id=str(rid),
                                period=str(pm)[:7] if pm else "",
                                table="sal.pay_run_rider",
                            )
                        )
                    if rider_ids and table_exists(cur, "sal", "pay_run_item"):
                        for i in range(0, len(rider_ids), 50):
                            chunk = rider_ids[i : i + 50]
                            ph2 = ",".join(["%s"] * len(chunk))
                            cur.execute(
                                f"SELECT id FROM sal.pay_run_item WHERE CAST(pay_run_rider_id AS nvarchar(64)) IN ({ph2})",
                                tuple(chunk),
                            )
                            for (iid,) in cur.fetchall():
                                add(Hit(kind="sal.pay_run_item", id=str(iid), table="sal.pay_run_item"))
                    if rider_ids and table_exists(cur, "sal", "mudad_deduction"):
                        for i in range(0, len(rider_ids), 50):
                            chunk = rider_ids[i : i + 50]
                            ph2 = ",".join(["%s"] * len(chunk))
                            cur.execute(
                                f"SELECT id FROM sal.mudad_deduction WHERE CAST(pay_run_rider_id AS nvarchar(64)) IN ({ph2})",
                                tuple(chunk),
                            )
                            for (iid,) in cur.fetchall():
                                add(Hit(kind="sal.mudad_deduction", id=str(iid), table="sal.mudad_deduction"))
                    if rider_ids and table_exists(cur, "sal", "rider_statement_snapshot"):
                        for i in range(0, len(rider_ids), 50):
                            chunk = rider_ids[i : i + 50]
                            ph2 = ",".join(["%s"] * len(chunk))
                            cur.execute(
                                f"SELECT id FROM sal.rider_statement_snapshot WHERE CAST(pay_run_rider_id AS nvarchar(64)) IN ({ph2})",
                                tuple(chunk),
                            )
                            for (iid,) in cur.fetchall():
                                add(
                                    Hit(
                                        kind="sal.rider_statement_snapshot",
                                        id=str(iid),
                                        table="sal.rider_statement_snapshot",
                                    )
                                )

    # invoice lines/items typed
    if typed_invoice_ids and table_exists(cur, "sal", "platform_invoice_line"):
        ph = ",".join(["%s"] * len(typed_invoice_ids))
        cur.execute(
            f"SELECT id, period_month FROM sal.platform_invoice_line WHERE CAST(invoice_id AS nvarchar(64)) IN ({ph})",
            tuple(typed_invoice_ids),
        )
        line_ids = []
        for lid, pm in cur.fetchall():
            line_ids.append(str(lid))
            add(
                Hit(
                    kind="sal.platform_invoice_line",
                    id=str(lid),
                    period=str(pm)[:7] if pm else "",
                    table="sal.platform_invoice_line",
                )
            )
        if line_ids and table_exists(cur, "sal", "platform_invoice_item"):
            for i in range(0, len(line_ids), 50):
                chunk = line_ids[i : i + 50]
                ph2 = ",".join(["%s"] * len(chunk))
                cur.execute(
                    f"SELECT id FROM sal.platform_invoice_item WHERE CAST(line_id AS nvarchar(64)) IN ({ph2})",
                    tuple(chunk),
                )
                for (iid,) in cur.fetchall():
                    add(Hit(kind="sal.platform_invoice_item", id=str(iid), table="sal.platform_invoice_item"))
        # distribution / settlement / item_bearer
        for tbl, fk in (
            ("distribution", "invoice_id"),
            ("app_settlement_invoice", "invoice_id"),
            ("item_bearer", None),
        ):
            if not table_exists(cur, "sal", tbl):
                continue
            if fk == "invoice_id":
                cur.execute(
                    f"SELECT id FROM sal.{tbl} WHERE CAST(invoice_id AS nvarchar(64)) IN ({ph})",
                    tuple(typed_invoice_ids),
                )
                for (iid,) in cur.fetchall():
                    add(Hit(kind=f"sal.{tbl}", id=str(iid), table=f"sal.{tbl}"))

    # ops snapshots + capture links for ninja
    if table_exists(cur, "sal", "ops_snapshot"):
        cur.execute(
            """
            SELECT id, period_month, kind FROM sal.ops_snapshot
            WHERE LOWER(scope_app)='ninja' OR LOWER(kind) LIKE 'ninja%'
            """
        )
        for rid, pm, kind in cur.fetchall():
            add(
                Hit(
                    kind="sal.ops_snapshot",
                    id=str(rid),
                    period=str(pm)[:7] if pm else "",
                    table="sal.ops_snapshot",
                    meta={"snapshot_kind": str(kind)},
                )
            )
    if table_exists(cur, "fin", "operation_snapshot_link"):
        cur.execute(
            """
            SELECT id, period_month FROM fin.operation_snapshot_link
            WHERE LOWER(CAST(operation_id AS nvarchar(100))) IN (
              SELECT CAST(id AS nvarchar(64)) FROM sal.pay_run WHERE LOWER(app_code)='ninja'
            ) OR LOWER(snapshot_kind) LIKE 'ninja%'
            """
        )
        try:
            for rid, pm in cur.fetchall():
                add(
                    Hit(
                        kind="fin.operation_snapshot_link",
                        id=str(rid),
                        period=str(pm)[:7] if pm else "",
                        table="fin.operation_snapshot_link",
                    )
                )
        except Exception:
            conn.rollback()
            cur = conn.cursor()
    if table_exists(cur, "fin", "operation_capture_manifest") and pay_run_ids:
        ph = ",".join(["%s"] * len(pay_run_ids))
        try:
            cur.execute(
                f"""
                SELECT id, period_month FROM fin.operation_capture_manifest
                WHERE CAST(operation_id AS nvarchar(64)) IN ({ph})
                """,
                tuple(pay_run_ids),
            )
            for rid, pm in cur.fetchall():
                add(
                    Hit(
                        kind="fin.operation_capture_manifest",
                        id=str(rid),
                        period=str(pm)[:7] if pm else "",
                        table="fin.operation_capture_manifest",
                    )
                )
        except Exception:
            conn.rollback()
            cur = conn.cursor()

    # pay.PayRunInput for ninja cycles / runs
    if table_exists(cur, "pay", "PayRunInput"):
        # columns vary; discover
        cur.execute(
            """
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA='pay' AND TABLE_NAME='PayRunInput'
            """
        )
        cols = {r[0].lower() for r in cur.fetchall()}
        if "pay_cycle_id" in cols or "cycle_id" in cols:
            col = "pay_cycle_id" if "pay_cycle_id" in cols else "cycle_id"
            if cycle_ids:
                ph = ",".join(["%s"] * len(cycle_ids))
                cur.execute(
                    f"SELECT id FROM pay.PayRunInput WHERE CAST({col} AS nvarchar(128)) IN ({ph})",
                    tuple(cycle_ids),
                )
                for (iid,) in cur.fetchall():
                    add(Hit(kind="pay.PayRunInput", id=str(iid), table="pay.PayRunInput"))
        if "pay_run_id" in cols and pay_run_ids:
            ph = ",".join(["%s"] * len(pay_run_ids))
            cur.execute(
                f"SELECT id FROM pay.PayRunInput WHERE CAST(pay_run_id AS nvarchar(64)) IN ({ph})",
                tuple(pay_run_ids),
            )
            for (iid,) in cur.fetchall():
                add(Hit(kind="pay.PayRunInput", id=str(iid), table="pay.PayRunInput"))

    # bank files linked to ninja pay runs / cycles
    if table_exists(cur, "sal", "bank_file"):
        cur.execute("SELECT id, status, period_month FROM sal.bank_file")
        # If any bank file exists while we have ninja pay runs, inspect lines for payable link — conservative:
        # only flag bank_file rows whose lines reference our pay_run_rider payable ids (complex). For empty table OK.
        rows = cur.fetchall()
        if rows and pay_run_ids:
            for rid, status, pm in rows:
                st = int(status) if status is not None else 0
                # Do not delete unrelated bank files; only block if we can prove ninja link via lines later.
                _ = (rid, st, pm)

    # approvals.request referencing ninja invoice/cycle ids
    if table_exists(cur, "approvals", "request"):
        cur.execute("SELECT id, kind, status, reference_id, payload_json FROM approvals.request")
        for rid, kind, status, ref, payload in cur.fetchall():
            blob = f"{ref or ''}\n{payload or ''}"
            if not any(i in blob for i in all_invoice_ids) and not any(c in blob for c in cycle_ids):
                if "ninja" not in blob.lower():
                    continue
                # generic ninja mention without our draft ids — skip (could be settings)
                continue
            st = str(status)
            reason = ""
            if st.lower() in ("approved", "3", "paid") or str(status) == "3":
                reason = f"approvals.request status={status}"
            add(
                Hit(
                    kind="approvals.request",
                    id=str(rid),
                    table="approvals.request",
                    blocker_reason=reason,
                    meta={"request_kind": str(kind), "status": st},
                )
            )

    # debt.debt typed with platform Ninja (drafts only unless approved)
    if table_exists(cur, "debt", "debt"):
        cur.execute(
            """
            SELECT id, status, platform FROM debt.debt
            WHERE LOWER(ISNULL(platform,'')) IN ('ninja','nin')
            """
        )
        try:
            for rid, status, platform in cur.fetchall():
                st = int(status) if status is not None else 0
                reason = ""
                if st >= 3:
                    reason = f"debt.debt status={st}"
                add(
                    Hit(
                        kind="debt.debt",
                        id=str(rid),
                        table="debt.debt",
                        blocker_reason=reason,
                        meta={"status": st},
                    )
                )
        except Exception:
            conn.rollback()
            cur = conn.cursor()

    plan.hits = list(hits.values())
    # Dedup blockers
    seen_b: set[tuple[str, str]] = set()
    uniq_b = []
    for b in plan.blockers:
        k = (b.table, b.id)
        if k in seen_b:
            continue
        seen_b.add(k)
        uniq_b.append(b)
    plan.blockers = uniq_b
    return plan


def print_report(plan: Plan, apply: bool) -> None:
    print("=== Ninja reset plan ===")
    print(f"mode: {'APPLY' if apply else 'DRY-RUN'}")
    print(f"total_ids: {len(plan.hits)}")
    print(f"blockers: {len(plan.blockers)}")
    print()
    print("-- counts per kind --")
    for kind, rows in sorted(plan.by_kind().items(), key=lambda kv: (-len(kv[1]), kv[0])):
        print(f"{kind}\t{len(rows)}")
    print()
    print("-- counts per period --")
    for period, n in sorted(plan.by_period().items()):
        print(f"{period}\t{n}")
    print()
    print("-- id list (no personal values) --")
    for kind, rows in sorted(plan.by_kind().items()):
        ids = sorted(r.id for r in rows)
        print(f"[{kind}] {len(ids)}")
        for i in ids:
            print(f"  {i}")
    notes = [h for h in plan.hits if h.meta.get("note")]
    if notes:
        print()
        print("-- notes (included for delete; not blockers) --")
        for h in notes:
            print(f"NOTE\t{h.kind}\t{h.id}\t{h.meta.get('note')}\tapproval={h.meta.get('approvalStatus')}")
    if plan.blockers:
        print()
        print("-- BLOCKERS --")
        for b in plan.blockers:
            print(f"BLOCKER\t{b.table}\t{b.id}\t{b.blocker_reason}")
    print()
    print("-- baseline (other apps / masters) --")
    for k, v in sorted(plan.baselines.items()):
        print(f"{k}\t{v}")


def delete_ids(cur, table: str, ids: list[str]) -> int:
    if not ids:
        return 0
    n = 0
    for i in range(0, len(ids), 40):
        chunk = ids[i : i + 40]
        ph = ",".join(["%s"] * len(chunk))
        if table == "records":
            cur.execute(f"DELETE FROM dbo.records WHERE id IN ({ph})", tuple(chunk))
        else:
            # table like sal.platform_invoice
            cur.execute(f"DELETE FROM {table} WHERE CAST(id AS nvarchar(128)) IN ({ph})", tuple(chunk))
        n += cur.rowcount
    return n


def disable_money_delete_triggers(cur) -> list[str]:
    """Money tables use INSTEAD OF DELETE guards; disable for this admin reset only."""
    cur.execute(
        """
        SELECT s.name, t.name
        FROM sys.triggers tr
        JOIN sys.tables t ON tr.parent_id = t.object_id
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        WHERE tr.is_disabled = 0 AND (
          tr.name LIKE '%_no_delete'
          OR tr.name LIKE '%_frozen%'
          OR tr.name LIKE 'TR_%_no_delete'
        )
        AND s.name IN ('sal','fin','pay','debt')
        """
    )
    disabled: list[str] = []
    for schema, table in cur.fetchall():
        # disable all triggers on the table for the duration
        fq = f"{schema}.{table}"
        cur.execute(f"DISABLE TRIGGER ALL ON {fq}")
        disabled.append(fq)
    return disabled


def enable_triggers(cur, tables: Iterable[str]) -> None:
    for fq in tables:
        cur.execute(f"ENABLE TRIGGER ALL ON {fq}")


def write_audit(cur, counts: dict[str, int], deleted: int) -> None:
    now = datetime.now(timezone.utc)
    action = AUDIT_ACTION
    # Ensure action_label
    cur.execute("SELECT 1 FROM audit.action_label WHERE action_code=%s", (action,))
    if not cur.fetchone():
        cur.execute(
            """
            INSERT INTO audit.action_label(id, action_code, name_ar, created_at)
            VALUES(%s, %s, %s, %s)
            """,
            (str(uuid.uuid4()), action, "إعادة ضبط مسودات نينجا", now.isoformat()),
        )
    audit_id = uuid.uuid4().hex
    entity = "ninja"
    diff = json.dumps(
        {"action": action, "deleted": deleted, "counts": counts, "id": audit_id},
        ensure_ascii=False,
        separators=(",", ":"),
    )
    if len(diff) > 400:
        diff = json.dumps(
            {"action": action, "deleted": deleted, "kinds": len(counts), "id": audit_id},
            ensure_ascii=False,
            separators=(",", ":"),
        )
    body = {
        "id": audit_id,
        "action": action,
        "actionAr": "إعادة ضبط مسودات نينجا",
        "entityId": entity,
        "entityRef": entity,
        "entity": entity,
        "recordId": entity,
        "actor": "ninja_reset",
        "at": now.isoformat(),
        "occurredAt": now.isoformat(),
        "source": "نظام",
        "counts": counts,
        "deleted": deleted,
    }
    cur.execute(
        "INSERT INTO dbo.records(id, kind, body) VALUES(%s, %s, %s)",
        (audit_id, "audit", json.dumps(body, ensure_ascii=False)),
    )
    cur.execute(
        """
        INSERT INTO audit.change_log
        (id, occurred_on, occurred_at, user_id, username, display_name_ar,
         action_code, entity_type, entity_ref, diff_short, source, created_at)
        VALUES(%s, %s, %s, NULL, %s, %s, %s, %s, %s, %s, %s, %s)
        """,
        (
            str(uuid.uuid4()),
            now.date().isoformat(),
            now.replace(tzinfo=None),
            "ninja_reset",
            "إعادة ضبط نينجا",
            action,
            "record",
            entity,
            diff[:400],
            "system",
            now.replace(tzinfo=None),
        ),
    )


def apply_plan(conn, plan: Plan) -> dict[str, int]:
    if plan.blockers:
        raise SystemExit(
            f"Refusing --apply: {len(plan.blockers)} BLOCKER(s). Re-run dry-run and resolve."
        )
    cur = conn.cursor()
    disabled: list[str] = []
    deleted_counts: dict[str, int] = defaultdict(int)
    try:
        disabled = disable_money_delete_triggers(cur)
        # Delete order: children → parents (typed), then records
        order = [
            "fin.journal_line",
            "fin.operation_capture_manifest",
            "fin.operation_snapshot_link",
            "sal.ops_snapshot",
            "sal.mudad_deduction",
            "sal.rider_statement_snapshot",
            "sal.pay_run_item",
            "sal.pay_run_rider",
            "sal.item_bearer",
            "sal.distribution",
            "sal.app_settlement_invoice",
            "sal.platform_invoice_item",
            "sal.platform_invoice_line",
            "pay.PayRunInput",
            "approvals.request",
            "debt.debt",
            "fin.journal",
            "sal.pay_run",
            "sal.platform_invoice",
            # records last (document store has no FK to typed money tables for these drafts)
            "records",
        ]
        by_table: dict[str, list[str]] = defaultdict(list)
        for h in plan.hits:
            by_table[h.table].append(h.id)
        for table in order:
            ids = sorted(set(by_table.get(table, [])))
            if not ids:
                continue
            n = delete_ids(cur, table, ids)
            deleted_counts[table] = n
        # any leftover tables not in order
        for table, ids in by_table.items():
            if table in deleted_counts:
                continue
            deleted_counts[table] = delete_ids(cur, table, sorted(set(ids)))
        kind_counts = {k: len(v) for k, v in plan.by_kind().items()}
        write_audit(cur, kind_counts, sum(deleted_counts.values()))
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        try:
            enable_triggers(cur, disabled)
            conn.commit()
        except Exception:
            conn.rollback()
    return dict(deleted_counts)


def after_check(conn, before: dict[str, int]) -> list[str]:
    cur = conn.cursor()
    after = baseline_counts(cur)
    problems = []
    print()
    print("-- after-check (before → after) --")
    keys = sorted(set(before) | set(after))
    print(f"{'metric':40} {'before':>8} {'after':>8} {'ok':>4}")
    for k in keys:
        b, a = before.get(k, 0), after.get(k, 0)
        # Ninja draft metrics must go to 0
        if k.endswith(".ninja") or k == "records.platform_invoice.ninja" or k == "records.pay_cycle.ninja":
            ok = a == 0
            if not ok:
                problems.append(f"{k} expected 0 after reset, got {a}")
        elif k.startswith("sal.platform_invoice.") and k.endswith(".ninja"):
            ok = a == 0
        else:
            # masters / other apps unchanged
            ok = a == b
            if not ok:
                problems.append(f"{k} changed {b} → {a}")
        print(f"{k:40} {b:8} {a:8} {'YES' if ok else 'NO':>4}")
    return problems


def resolve_cs() -> str:
    for key in (
        "PAYROLL_SQL",
        "GATE_PAYROLL_SQL",
        "ConnectionStrings__DefaultConnection",
    ):
        val = os.environ.get(key, "").strip()
        if val:
            return unquote(val) if val.startswith("mssql+") else val
    raise SystemExit(
        "Set PAYROLL_SQL (or GATE_PAYROLL_SQL / ConnectionStrings__DefaultConnection)"
    )


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--apply", action="store_true", help="Delete in one transaction (default: dry-run)")
    args = ap.parse_args(argv)
    cs = resolve_cs()
    # Never print password
    cfg = parse_conn(cs)
    print(f"target: {cfg['host']},{cfg['port']} db={cfg['db']} user={cfg['user']}")
    conn = connect(cs)
    try:
        plan = discover(conn)
        print_report(plan, apply=args.apply)
        if not args.apply:
            print("\nDRY-RUN complete. Re-run with --apply to delete.")
            return 1 if plan.blockers else 0
        deleted = apply_plan(conn, plan)
        print()
        print("-- deleted rows --")
        for k, v in sorted(deleted.items()):
            print(f"{k}\t{v}")
        problems = after_check(conn, plan.baselines)
        if problems:
            print("\nAFTER-CHECK FAILED:")
            for p in problems:
                print(f"  {p}")
            return 2
        print("\nAPPLY complete. after-check OK.")
        return 0
    finally:
        conn.close()


if __name__ == "__main__":
    sys.exit(main())
