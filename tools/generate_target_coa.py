#!/usr/bin/env python3
"""Generate the read-only COA resource from the finance spec; never read business data."""
import argparse
import json
import re
from collections import Counter
from pathlib import Path


def parse_status(value):
    parts = value.split(maxsplit=1)
    first = parts[0] if parts else ""
    note = parts[1] if len(parts) > 1 else ""
    statuses = {"فعال": "active", "موقوف": "stopped", "محجوز": "reserved", "عرض": "display"}
    if first not in statuses:
        raise ValueError("Unknown COA status")
    if first == "عرض" and note.startswith("فقط"):
        note = note.removeprefix("فقط")
    return statuses[first], note.lstrip(" —–-").strip() or None


def parse_source(value):
    if value == "Excel":
        return "excel"
    if value in {"Excel معدّل", "Excel معدل"} or value.partition(" ")[0] == "منقول":
        return "modified"
    if value == "جديد":
        return "new"
    raise ValueError("Unknown COA source")


def validate_summary(text, accounts):
    summary = text.split("## 1.", 1)[1].split("## 2.", 1)[0]
    counts = {}
    for line in summary.splitlines():
        cells = [c.strip() for c in line.strip("|").split("|")]
        if len(cells) == 2 and (number := re.match(r"\d+", cells[1])):
            counts[cells[0]] = int(number.group())
    expected = {
        "total": counts["إجمالي الشجرة المستهدفة"],
        "activePosting": counts["حسابات ترحيل فعالة"],
        "excel": counts["حسابات Excel المحتفظ بها بنفس الكود"] - counts["منها معدّل (اسم أو حالة أو تجميع)"],
        "modified": counts["منها معدّل (اسم أو حالة أو تجميع)"] + counts["حسابات Excel منقولة لكود جديد (BR §20)"] + counts["حسابات جديدة منقولة (BR §20)"],
        "new": counts["حسابات جديدة"],
    }
    sources = Counter(a["source"] for a in accounts)
    actual = {"total": len(accounts), "activePosting": sum(a["posting"] and a["status"] == "active" for a in accounts),
              **{key: sources[key] for key in ("excel", "modified", "new")}}
    if actual != expected:
        raise ValueError(f"COA counts differ from §1 summary: expected {expected}; actual {actual}")


def generate(source):
    text = source.read_text(encoding="utf-8")
    chart = text.split("## 4.", 1)[1].split("## 5.", 1)[0]
    accounts = []
    for line in chart.splitlines():
        cells = [c.strip() for c in line.strip("|").split("|")]
        if len(cells) != 11 or not re.fullmatch(r"`\d+`", cells[0]):
            continue
        code, name, parent, posting, nature, kind, direction, company, subledger, raw_status, raw_source = cells
        status, status_note = parse_status(raw_status)
        accounts.append(dict(code=code.strip("`"), nameAr=name, parent=None if parent == "—" else parent.strip("`"),
            level=0, posting=posting == "نعم", nature={"مدين": "debit", "دائن": "credit"}[nature],
            type={"بنك": "bank", "خزينة": "cash", "أخرى": "other"}[kind],
            direction={"صرف": "pay", "قبض": "receive", "الاتنين": "both", "—": None}[direction],
            company=company, status=status, statusNote=status_note, subledger=subledger,
            source=parse_source(raw_source), sourceNote=raw_source))
    by_code = {a["code"]: a for a in accounts}
    if not accounts or len(by_code) != len(accounts):
        raise ValueError("Missing accounts or duplicate codes")

    def level(code, ancestors=()):
        if code in ancestors:
            raise ValueError("COA cycle")
        a = by_code[code]
        return 1 if a["parent"] is None else 1 + level(a["parent"], ancestors + (code,))

    parents = {a["parent"] for a in accounts}
    for a in accounts:
        if a["parent"] is not None and a["parent"] not in by_code:
            raise ValueError("Missing parent: " + a["code"])
        if a["posting"] and a["code"] in parents:
            raise ValueError("Posting account has children: " + a["code"])
        if a["posting"] and a["status"] != "active":
            raise ValueError("Inactive account allows posting: " + a["code"])
        a["level"] = level(a["code"])
    validate_summary(text, accounts)
    mapping = []
    section = text.split("## 6.", 1)[1].split("## 7.", 1)[0]
    for line in section.splitlines():
        cells = [c.strip() for c in line.strip("|").split("|")]
        if len(cells) != 4 or not re.fullmatch(r"`[A-Z0-9.]+`", cells[0]):
            continue
        targets = re.findall(r"`([0-9]+)`", cells[2])
        if any(code not in by_code for code in targets):
            raise ValueError("Mapping references an absent account")
        mapping.append(dict(currentCode=cells[0].strip("`"), targetCodes=targets, ruleAr=cells[2], noteAr=cells[3]))
    if not mapping or len({m["currentCode"] for m in mapping}) != len(mapping):
        raise ValueError("Missing or duplicate mapping")
    return {"schemaVersion": 1, "version": "2026-11-03.1", "source": "gate-finance-spec/docs/02-chart-of-accounts.md §§1,4,6; BUSINESS_RULES §§20,22",
            "accounts": accounts, "mapping": mapping}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[2] / "gate-finance-spec/docs/02-chart-of-accounts.md")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "Payroll.Api/Resources/target-coa.v1.json")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    rendered = json.dumps(generate(args.source), ensure_ascii=False, indent=2) + "\n"
    if args.check:
        if not args.output.exists() or args.output.read_text(encoding="utf-8") != rendered:
            raise SystemExit("Target COA resource differs from the spec; regenerate it")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    print("Target COA resource matches the source" if args.check else "Target COA resource generated")


if __name__ == "__main__":
    main()
