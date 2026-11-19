# VPS ERP sync units — retired (S1)

User systemd units `gate-erp-sync.service` / `gate-erp-sync.timer` and the published `ErpImport.dll` sync CLI were removed with S1.

Do not enable timers or republish the importer for mirror sync. Live payroll reads Omar’s ERP with the read-only account; operations and capture use live readers, not a VPS sync schedule.

If a unit is still installed on a machine, disable/stop it manually after Ghazal confirms; this repo no longer ships the unit files.
