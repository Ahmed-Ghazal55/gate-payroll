# Daily ERP → staging sync

The importer accepts `--live --consistent --dry-run` and `--live --consistent --commit`. Source and target credentials are read internally from their private env files; the only accepted target remains staging db77738. Production is never accepted. A live commit without `--consistent` is refused. A new staging load requires Ghazal's «حمّل» after its dry-run; the certificate-load approval does not enable this timer.

Each attempt reads the explicit source-table allowlist twice, ordered by primary key. Per-table canonical fingerprints are compared in memory, covering every table that contributes to a kind, including nested history. At most three pairs are attempted. Any mismatch retries the complete pair; three mismatches refuse the run before Store creation or writes. No snapshot-isolation setting or ERP record is changed.

The accepted plan is compared with the ERP-managed target baseline. More than 10% new/changed records in a kind, or more than 2% count loss, refuses the entire run before writes. A previously empty kind with new records also requires manual review. Manual/certificate records are excluded from the ERP baseline; certificate-verified registry fields survive subsequent ERP merges. Missing source records are retained; there is no DELETE path. The Store transaction checks concurrent target changes and writes records/audits atomically, then verifies the same source snapshot without rereading it.

Every `--live` run writes a uniquely named JSON log under `~/Private/gate-data/sync-logs/`, directories 700 and files 600. Its typed schema contains timestamps, attempts, fixed error codes and aggregate import counts/reasons only. Fingerprints, connection strings and record bodies are excluded. An inconsistent/refused run is logged with its available counts and refusal code. Inspect that result before replaying a failed commit; the `committed` flag and next dry-run establish the actual state.

Review commands (SELECT-only):
```bash
dotnet tools/ErpImport/bin/Release/net10.0/ErpImport.dll --live --consistent --dry-run
```

The user units in `tools/ErpImport/systemd/` are delivered **disabled and uninstalled**. After merge/review, a successful dry-run and explicit approval for daily staging writes, Ghazal can build the Release importer, copy the two units to his user systemd directory and enable the timer himself:
```bash
dotnet build tools/ErpImport/ErpImport.csproj -c Release -p:SkipFrontendBuild=true
mkdir -p ~/.config/systemd/user
cp tools/ErpImport/systemd/gate-erp-sync.{service,timer} ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now gate-erp-sync.timer
systemctl --user list-timers gate-erp-sync.timer
```
`OnCalendar` explicitly uses Asia/Riyadh at 03:15; `Persistent=true` catches a missed run when the user manager starts. A catch-up run may therefore happen during working hours: disable the timer if that read window is not approved. Codex has not installed or enabled it. No env values belong in unit files or the journal.
