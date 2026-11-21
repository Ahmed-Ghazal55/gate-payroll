/** Arabic labels for payroll readiness blocker codes (API keys stay internal). */

const PAYROLL_BLOCKER_LABELS_AR: Record<string, string> = {
  invoice_missing: 'فاتورة المنصة غير مرفوعة',
  pay_terms_missing: 'شروط عقد المنصة غير معتمدة',
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