/** Per-page import step configs (same URLs as ImportCatalog; no centre). */

export type PageImportUpload = 'xlsx' | 'json-rows';

export type PageImportStep = {
  key: string;
  labelAr: string;
  purposeAr: string;
  /** Endpoint permission code; admin always allowed. */
  permission: string;
  /** Extra permission required to show the button (e.g. financial_edit for riders commit). */
  extraPermission?: string;
  /** When true, only Administrator (API gate for companies import). */
  adminOnly?: boolean;
  templateUrl: string | null;
  previewUrl: string;
  commitUrl: string;
  upload: PageImportUpload;
  noteAr?: string;
};

export const COMPANIES_IMPORT: PageImportStep = {
  key: 'companies',
  labelAr: 'استيراد الشركات',
  purposeAr: 'رفع صفوف السجلات التجارية (معاينة ثم اعتماد).',
  permission: 'companies.manage',
  adminOnly: true,
  templateUrl: '/api/imports/templates/companies',
  previewUrl: '/api/companies/import/preview',
  commitUrl: '/api/companies/import/commit',
  upload: 'json-rows',
  noteAr: 'الصفوف تُرسل JSON بنفس أسماء الأعمدة؛ حتى 100 شركة. لمسؤول النظام فقط.',
};

export const RIDERS_IMPORT: PageImportStep = {
  key: 'riders',
  labelAr: 'استيراد ملف المناديب',
  purposeAr: 'إكمال بيانات ناقصة من ملف Excel (القائمة تُقرأ من نظام عمر).',
  permission: 'riders.edit',
  extraPermission: 'riders.financial_edit',
  templateUrl: '/api/excel/riders/template',
  previewUrl: '/api/excel/riders/preview',
  commitUrl: '/api/excel/riders/commit',
  upload: 'xlsx',
};

export const TREASURY_OPENING_IMPORT: PageImportStep = {
  key: 'treasury_opening',
  labelAr: 'استيراد الأرصدة الافتتاحية',
  purposeAr: 'رصيد كل صندوق وبنك أول المدة قبل أي صرف.',
  permission: 'treasury.opening',
  templateUrl: '/api/finance/opening/template',
  previewUrl: '/api/finance/opening/preview',
  commitUrl: '/api/finance/opening/commit',
  upload: 'xlsx',
};

export const OPENING_DEBTS_IMPORT: PageImportStep = {
  key: 'opening_debts',
  labelAr: 'استيراد المديونيات الافتتاحية',
  purposeAr: 'رصيد مديونية كل مندوب بنهاية الشهر بالبند والسجل الدائن والقسط.',
  permission: 'debts.import_opening',
  extraPermission: 'treasury.opening',
  templateUrl: '/api/debts/import-opening/template',
  previewUrl: '/api/debts/import-opening/preview',
  commitUrl: '/api/debts/import-opening/commit',
  upload: 'xlsx',
};

export const BULK_COLLECT_IMPORT: PageImportStep = {
  key: 'bulk_collect',
  labelAr: 'تحصيل مجمع من ملف',
  purposeAr: 'سداد نقدي أو إعفاء على مديونيات قائمة من ملف Excel.',
  permission: 'debts.collect',
  templateUrl: '/api/debts/bulk-collect/template',
  previewUrl: '/api/debts/bulk-collect/preview-file',
  commitUrl: '/api/debts/bulk-collect/commit',
  upload: 'xlsx',
};

export const NINJA_OPS_IMPORT: PageImportStep = {
  key: 'ninja_operations',
  labelAr: 'استيراد تشغيل نينجا',
  purposeAr: 'طلبات وأيام المناديب مطابقة لسطور فاتورة نينجا.',
  permission: 'pay.manage',
  templateUrl: '/api/imports/templates/operations-ninja',
  previewUrl: '/api/pay/apps/ninja/operations/import/preview',
  commitUrl: '/api/pay/apps/ninja/operations/import/commit',
  upload: 'xlsx',
};

export const KEETA_OPS_IMPORT: PageImportStep = {
  key: 'keeta_operations',
  labelAr: 'استيراد تشغيل كيتا',
  purposeAr: 'الطلبات والأيام لكل مندوب على كل حساب كيتا.',
  permission: 'pay.manage',
  templateUrl: '/api/imports/templates/operations-keeta',
  previewUrl: '/api/pay/apps/keeta/operations/import/preview',
  commitUrl: '/api/pay/apps/keeta/operations/import/commit',
  upload: 'xlsx',
};

export const HS_OPS_IMPORT: PageImportStep = {
  key: 'hungerstation_operations',
  labelAr: 'استيراد تشغيل هنقرستيشن',
  purposeAr: 'الطلبات والساعات لكل مندوب على كل حساب هنقرستيشن.',
  permission: 'pay.manage',
  templateUrl: '/api/imports/templates/operations-hungerstation',
  previewUrl: '/api/pay/apps/hs/operations/import/preview',
  commitUrl: '/api/pay/apps/hs/operations/import/commit',
  upload: 'xlsx',
};

export function canShowImportStep(
  step: PageImportStep,
  opts: { isAdmin: boolean; can: (code: string) => boolean }
): boolean {
  if (opts.isAdmin) return true;
  if (step.adminOnly) return false;
  return opts.can(step.permission);
}

export function canCommitImportStep(
  step: PageImportStep,
  opts: { isAdmin: boolean; can: (code: string) => boolean }
): boolean {
  if (opts.isAdmin) return true;
  if (!opts.can(step.permission)) return false;
  if (step.extraPermission && !opts.can(step.extraPermission)) return false;
  return true;
}
