/** Arabic labels for payroll readiness blocker codes (API keys stay internal). */

const PAYROLL_BLOCKER_LABELS_AR: Record<string, string> = {
  invoice_missing: 'فاتورة المنصة غير مرفوعة',
  pay_terms_missing: 'شروط عقد المنصة غير معتمدة',
  vehicle_type_platform_mismatch: 'عدم تطابق نوع مركبة المندوب مع يوزر المنصة',
  pay_terms: 'شروط عقد المنصة غير مكتملة',
  pay_eligibility_missing: 'شروط الأهلية غير معتمدة',
  pay_eligibility: 'شروط الأهلية غير مكتملة',
  global_settings_missing: 'الإعدادات العامة غير معتمدة',
  scheme_assignment_missing: 'تعيين النظام الداخلي ناقص',
  ValidateRiders: 'تحقق مناديب التشغيل غير مكتمل',
};

export function payrollBlockerLabelAr(code: string | null | undefined): string {
  const key = (code || '').trim();
  if (!key) return 'مانع غير معرّف';
  return PAYROLL_BLOCKER_LABELS_AR[key] ?? key;
}

/** User-facing Arabic for org routing operation codes (values remain API codes in state). */
const ORG_OPERATION_LABELS_AR: Record<string, string> = {
  'advance.review': 'مراجعة السلف',
  'advance.payout:MAIN.BW.DEBT': 'صرف سلف نقدي — كفيل البوابة',
  'advance.payout:BANK.BW': 'صرف سلف بنكي — كفيل البوابة',
};

export function orgOperationLabelAr(operation: string | null | undefined): string {
  const key = (operation || '').trim();
  if (!key) return '—';
  if (ORG_OPERATION_LABELS_AR[key]) return ORG_OPERATION_LABELS_AR[key];
  if (key.startsWith('advance.payout:')) {
    const vault = key.slice('advance.payout:'.length);
    return `صرف سلف — ${vault}`;
  }
  if (key === 'advance.review') return 'مراجعة السلف';
  return key;
}

/** Fallback Arabic when API roleNameAr is missing or still carries an English gloss. */
const ROLE_LABELS_AR: Record<string, string> = {
  Admin: 'مدير النظام العام',
  SuperAdmin: 'المدير العام / الإدارة العليا',
  SystemAdmin: 'مسؤول النظام',
  Accountant: 'محاسب مالي ومسؤول خزينة',
  Reviewer: 'مراجع ومدقق',
  Manager: 'مدير تشغيل',
  FinancialManager: 'المدير المالي',
  FinancialAccountant: 'رئيس الحسابات والخزينة المركزية',
};

export function roleLabelAr(role: string | null | undefined, roleNameAr?: string | null): string {
  const code = (role || '').trim();
  const named = (roleNameAr || '').trim();
  if (named) {
    // Drop leftover English glosses like «(Super Admin)» / «(RBAC)» if any remain.
    const cleaned = named.replace(/\s*\([A-Za-z][^)]*\)\s*/g, '').trim();
    if (cleaned) return cleaned;
  }
  if (ROLE_LABELS_AR[code]) return ROLE_LABELS_AR[code];
  return code || '—';
}

export function permissionCodesLabelAr(
  codes: string[] | null | undefined,
  catalog?: Array<{ code: string; nameAr: string }> | null
): string {
  if (!codes || codes.length === 0) return 'حسب الدور الأساسي';
  const byCode = new Map((catalog || []).map((p) => [p.code, p.nameAr]));
  return codes
    .map((c) => {
      const named = byCode.get(c);
      if (named) return named.replace(/\s*\([A-Za-z][^)]*\)\s*/g, '').trim() || named;
      // Never show raw English fragments like «checker» from API codes.
      if (c.includes('maker_checker') || c.includes('bypass_maker_checker')) return 'تجاوز شرط الشخصين';
      return c;
    })
    .join('، ');
}