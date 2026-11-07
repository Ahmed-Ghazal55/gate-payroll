# Import Center catalog (K6)

`GET /api/imports/catalog` (imports.view) returns every import in the order Ghazal set:
companies → riders → opening treasury → opening debts → bulk collect → per platform (invoice, then operations) for Ninja, Keeta, HungerStation, then Chefz from ERP.

Each step: `order, key, group (setup|opening|invoices|operations), labelAr, purposeAr, dependsOn[], permission,
templateUrl, previewUrl, commitUrl, upload (xlsx|json-rows|platform-file|erp), status (ready|template_only|planned|erp), noteAr,
columns[{name, required, type (text|number|choice), identity, choices}]`.

- `status=template_only`: the template is final enough to fill; preview/commit land in R6c-D3 (Codex).
- `status=planned`: no importer yet (Keeta invoice, R6c-D2).
- `upload=platform-file`: upload the platform's original file unchanged; no template.
- `upload=erp`: pulled read-only from Gate ERP after the first «حمّل».

`GET /api/imports/templates/{name}` (imports.view): `companies`, `operations-ninja`, `operations-keeta`, `operations-hungerstation`.
Row 2 is a marked example («مثال — احذف هذا الصف»). Other templates keep their existing URLs; the catalog reads the same column
definitions those endpoints use (`TableExcelApi.Columns`, `TreasuryEntries.OpeningColumns`, `Debts.OpeningTemplateColumns`, `Debts.BulkCollectColumns`).

Operations templates share a core (`period, platformAccountId, iqama, riderName, from, to, days, orders, baseGross, category`) that maps to the
pay-cycle invoice review `accounts[].riders[]`, plus app-specific metrics. Codex D3 finalises the metrics from the real invoices.

R6c-D3: Ninja/Keeta/HungerStation operations now expose ready preview/commit URLs. See [operations review contract](operations-import.md), including the explicit normalized-invoice requirement for historical HS raw files. Metric columns and six pay categories come from ImportCatalog.
