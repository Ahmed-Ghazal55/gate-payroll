# Optional alternative: three-hour ERP sync on a VPS (R6a-8)

**Inactive alternative.** The selected workflow is [manual sync, startup catch-up and optional daily Hangfire execution](../ops/HANGFIRE.md); there is no active VPS and no three-hour recurrence. The supplied systemd files remain uninstalled/disabled. Do not follow the activation commands below while Hangfire is active; Ghazal must explicitly choose a different scheduler first.

Hostinger read-only check on 2026-11-05: the VPS list returned **zero instances**; there is no VPS name or OS to report. No server was created, bought, upgraded, or accessed. The steps below are for Ghazal to use after choosing/providing a VPS; they have not been executed remotely.

## Prepare a dedicated user and runtime

On a compatible Linux VPS, Ghazal creates a dedicated non-root `gate-sync` user and installs the ASP.NET Core **10** runtime (the importer references the API assembly). Install OS/runtime updates before restricting outbound traffic. Keep interactive SSH administration under a separately controlled account. Enable the dedicated user's lingering user manager:

```bash
sudo adduser --disabled-password --gecos '' gate-sync
sudo loginctl enable-linger gate-sync
```

Build/publish the Release importer locally, using the existing packages and frontend build process:

```bash
dotnet publish tools/ErpImport/ErpImport.csproj -c Release -p:SkipFrontendBuild=true --no-self-contained -o /tmp/gate-sync-release
```

Copy the **entire** published directory (DLLs, runtime config and dependencies) to the sync user's `~/gate-sync/`. Under that user, create `~/Private/gate-data/` and `~/Private/gate-data/sync-logs/` with mode 700. Ghazal himself copies the two connection files with `scp` and sets mode 600:

```bash
scp erp-readonly.env staging.env gate-sync@VPS:Private/gate-data/
# On the VPS, as gate-sync:
chmod 700 ~/Private ~/Private/gate-data ~/Private/gate-data/sync-logs
chmod 600 ~/Private/gate-data/erp-readonly.env ~/Private/gate-data/staging.env
```

The agent never reads or prints these files. Do not put their contents in shell arguments, units, Git, screenshots or journal output. The importer opens ERP only as `db67927_readonly` with ReadOnly intent and verifies its DB permissions; the target guard accepts only staging `db77738`.

## Network rules and SQL allowlists

Ask both SQL hosting providers to allow the new VPS public IP for ERP and staging SQL Server. Ghazal verifies the two SQL host/IP mappings and adds persistent resolution (for example `/etc/hosts`) before denying outbound DNS. Keep the connection host names so TLS validation still uses the intended host. Ensure the firewall permits only outbound TCP 1433 to those two approved IPs, plus inbound SSH from the administrator's IP. Preserve the SSH management connection while applying rules. Example UFW policy, with the three IP variables supplied by Ghazal:

```bash
sudo ufw default deny incoming
sudo ufw default deny outgoing
sudo ufw allow in proto tcp from "$ADMIN_IP" to any port 22
sudo ufw allow out proto tcp to "$ERP_SQL_IP" port 1433
sudo ufw allow out proto tcp to "$STAGING_SQL_IP" port 1433
sudo ufw enable
```

No HTTP listener or inbound SQL port is needed. DNS, package maintenance and clock setup must be completed before this restricted policy; Ghazal controls any later maintenance window.

## First load is manual; scheduling follows acceptance

Run `~/gate-sync/ErpImport.dll --live --consistent --dry-run` first. New kinds refuse until individually reviewed with repeated `--accept-new-kind KIND`. A large change refuses separately; it needs source-owner review and Ghazal's explicit decision before a one-off `--accept-large-change KIND`. Neither flag belongs in the service or timer. The service uses `--counts-only` so its journal output omits monetary aggregate totals. Current local preview refused an inherited vehicle change; investigate that drift before approving the initial load.

Only after Ghazal reviews the successful preview and says **«حمّل»**, run the approved manual `--commit` command. Verify `sameSnapshotVerification` reports zero new/changed rows and a fresh dry-run is understood. Debt workbook loading has its separate mapping/FIFO reconciliation approval and is never part of the timer.

Then, as the sync user:

```bash
mkdir -p ~/.config/systemd/user
cp gate-erp-sync.service gate-erp-sync.timer ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now gate-erp-sync.timer
systemctl --user list-timers gate-erp-sync.timer
```

The service assumes the published importer at `~/gate-sync/ErpImport.dll`. The timer is `OnCalendar=*-*-* 00/3:15:00 Asia/Riyadh` with `Persistent=true`: 00:15, 03:15, 06:15, 09:15, 12:15, 15:15, 18:15, 21:15 Riyadh. A missed run is caught up after restart. Units remain uninstalled/disabled until Ghazal performs these steps.

## Records, refusals and stopping

Each committed-mode live run appends one `erp_sync_run` containing start/end/status, counts per kind and a fixed refusal code. A refused run writes no domain source documents; the only staging additions are its counts record and an administrator-only approvals notification. Dry-runs (including this task before «حمّل») write private file logs only, never staging telemetry or schema changes. If staging itself is unavailable, the private file log remains the diagnostic fallback and the command fails.

Administrators can read `GET /api/system/erp-sync?limit=20` (`1..100`). Other roles get 403; responses project only the documented count fields. Both modern and legacy inbox paths enforce the administrator audience. No source rows, people, financial amounts, source fingerprints or connection values enter the run record.

```bash
systemctl --user disable --now gate-erp-sync.timer
systemctl --user stop gate-erp-sync.service
journalctl --user -u gate-erp-sync.service --since today --no-pager
```

Private JSON logs are in `~/Private/gate-data/sync-logs/` (directory 700, files 600). Read only counts, totals and fixed codes; never dump connection files or raw worksheets. A refusal exits nonzero and does not schedule an immediate retry or auto-accept an override.
