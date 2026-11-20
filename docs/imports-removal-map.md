# Imports centre removal map (H2 / U-W4-01)

Source: `GET /api/imports/catalog` (`Payroll.Api/ImportCatalog.cs`) and `web/src/app/imports/page.tsx`.
Rule: same preview → approve endpoints; button visibility = endpoint permission (`can` / admin) or page `allowedActions` when the list API exposes an import action.

| Upload (catalog key) | Label (AR) | Permission | Template | Preview | Commit | Upload kind | New home page | Button label | Endpoint kept? |
|---|---|---|---|---|---|---|---|---|---|
| `companies` | الشركات (السجلات التجارية) | `companies.manage` (API: administrator only) | `/api/imports/templates/companies` | `/api/companies/import/preview` | `/api/companies/import/commit` | json-rows | `/companies` | استيراد الشركات | yes (preview/commit); template via catalog templates until moved |
| `riders` | المناديب | `riders.edit` (excel routes: `imports.view` / `imports.commit`) | `/api/excel/riders/template` | `/api/excel/riders/preview` | `/api/excel/riders/commit` | xlsx | `/workforce` | استيراد ملف المناديب | yes |
| `treasury_opening` | الأرصدة الافتتاحية للخزائن والبنوك | `treasury.opening` | `/api/finance/opening/template` | `/api/finance/opening/preview` | `/api/finance/opening/commit` | xlsx | `/accounting` | استيراد الأرصدة الافتتاحية | yes |
| `opening_debts` | المديونيات الافتتاحية | `debts.import_opening` (+ commit needs `treasury.opening`) | `/api/debts/import-opening/template` | `/api/debts/import-opening/preview` | `/api/debts/import-opening/commit` | xlsx | `/debts` | استيراد المديونيات الافتتاحية | yes |
| `bulk_collect` | التحصيل المجمع للمديونيات | `debts.collect` | `/api/debts/bulk-collect/template` | `/api/debts/bulk-collect/preview-file` | `/api/debts/bulk-collect/commit` | xlsx | `/debts` | تحصيل مجمع من ملف | yes |
| `ninja_invoice` | فاتورة نينجا | `pay.manage` | — | `/api/platforms/ninja/invoices` | `/api/platforms/ninja/invoices?commit=1` | platform-file | `/payroll?tab=ninja_upload` | رفع فاتورة نينجا (existing screen) | yes |
| `ninja_operations` | تشغيل نينجا للمناديب | `pay.manage` | `/api/imports/templates/operations-ninja` | `/api/pay/apps/ninja/operations/import/preview` | `/api/pay/apps/ninja/operations/import/commit` | xlsx | `/payroll` (Ninja) | استيراد تشغيل نينجا | yes |
| `keeta_invoice` | فاتورة كيتا | `pay.manage` | — | `/api/platforms/keeta/invoices` | `/api/platforms/keeta/invoices?commit=1` | platform-file | `/payroll?tab=keeta_upload` / platforms Keeta settlements | رفع فاتورة كيتا (existing screen) | yes |
| `keeta_operations` | تشغيل كيتا للمناديب | `pay.manage` | `/api/imports/templates/operations-keeta` | `/api/pay/apps/keeta/operations/import/preview` | `/api/pay/apps/keeta/operations/import/commit` | xlsx | `/payroll` (Keeta) | استيراد تشغيل كيتا | yes |
| `hungerstation_invoice` | فاتورة هنقرستيشن | `platforms.hs.manage` | — | `/api/platforms/hs/invoices/import/preview` | `/api/platforms/hs/invoices/import/commit` | platform-file | `/payroll?tab=platforms&app=hungerstation&sub=settlements` | **gap (salaries):** no upload UI on main or this branch; preview/commit endpoints exist but are unused — do not invent a screen here | yes (API only) |
| `hungerstation_operations` | تشغيل هنقرستيشن للمناديب | `pay.manage` | `/api/imports/templates/operations-hungerstation` | `/api/pay/apps/hs/operations/import/preview` | `/api/pay/apps/hs/operations/import/commit` | xlsx | `/payroll` (HS) | استيراد تشغيل هنقرستيشن | yes |
| `chefz_operations` | تشغيل شفز | `pay.view` | — | `/api/pay/apps/chefz/operations/review` | — | erp (no file) | `/payroll?tab=platforms&app=thechefz&sub=settlements` | مراجعة تشغيل شفز | yes (review only) |

## Gaps (do not invent in this unit)
| Item | Finding |
|---|---|
| HungerStation invoice upload UI | Catalog key `hungerstation_invoice` had no upload screen on main; `previewHsImport` / commit stay API-only. Track under salaries later — not built here. |

## Not in catalog (checked)
| Item | Finding |
|---|---|
| Jahez invoice/ops file | No catalog step; Jahez money UI is treasury (`/treasury?tab=jahez`), not an imports-centre upload. |
| Free-form `/api/imports/inspect|preview|commit` | Old `Frontend/` + HTTP tests only; not a catalog step. Keep while old UI is served; migrate `.mjs` tests off it when removing free-upload. |
| Assignments import `/api/pay/apps/{app}/assignments/import/*` | Separate pay-settings flow on payroll; not part of the setup wizard. |

## Centre-only to delete after moves
| Item | Action |
|---|---|
| `web/src/app/imports/` | delete |
| Sidebar «استيراد الملفات» | delete |
| Payroll blocker `import_operations` → `/app/imports` | retarget to payroll ops import button / Ninja ops |
| `GET /api/imports/catalog` | delete when no page caller |
| `GET /api/imports/templates/{name}` | keep while pages use those URLs (companies + ops templates) |
| `/api/imports/inspect|sheet|preview|commit` + `GET /api/imports` | keep if `Frontend/` still served; else delete after test migration |

## Old Frontend
`Frontend/` still ships as `wwwroot/index.html` + `app.js` (release packaging / `check_publish.py`). Not dead → do **not** delete the folder in this unit. Its `#/imports` free-upload still calls `/api/imports/*`.
