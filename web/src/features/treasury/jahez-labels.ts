const JAHEZ_STATUS_AR: Record<string, string> = {
  draft: 'مسودة',
  pending_approval: 'بانتظار الاعتماد',
  approved: 'معتمد',
  posted: 'مرحّل',
  rejected: 'مرفوض',
};

const JAHEZ_TOTALS_AR: Record<string, string> = {
  deliveryFees: 'رسوم التوصيل',
  cashCollections: 'التحصيلات النقدية',
  driverAdjustments: 'تسويات المناديب',
  bonusesTips: 'المكافآت والإكراميات',
  paidOnBehalf: 'مدفوع بالنيابة',
  commission: 'العمولة',
  companyDue: 'مستحق الشركة',
  expectedNet: 'الصافي المتوقع',
  variance: 'الفرق',
};

export function jahezReconciliationStatusLabelAr(status: string | null | undefined): string {
  const key = (status || '').trim();
  if (!key) return 'غير معروف';
  return JAHEZ_STATUS_AR[key] || 'حالة غير معروفة';
}

export function jahezTotalsKeyLabelAr(key: string | null | undefined): string {
  const k = (key || '').trim();
  if (!k) return 'قيمة';
  return JAHEZ_TOTALS_AR[k] || 'قيمة';
}
