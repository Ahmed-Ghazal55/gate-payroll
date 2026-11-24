/** Shared Arabic labels for audit action keys shown to operators. */

const AUDIT_ACTION_LABELS_AR: Record<string, string> = {
  "adjustment.add": "إضافة تسوية",
  "admin.approval_exception_cleared": "إلغاء استثناء اعتماد",
  "admin.login": "تسجيل دخول إداري",
  "admin.login_locked": "قفل حساب بعد محاولات فاشلة",
  "admin.password_changed": "تغيير كلمة المرور",
  "admin.password_reset": "إعادة تعيين كلمة المرور",
  "admin.role_permissions": "تحديث صلاحيات الأدوار",
  "admin.setup": "إعداد النظام",
  "admin.system_admin_permissions_seeded": "تهيئة صلاحيات مسؤول النظام",
  "admin.user_create": "إنشاء مستخدم",
  "admin.user_disable": "تعطيل مستخدم",
  "admin.user_update": "تحديث مستخدم",
  "approval.bypass_granted": "منح تجاوز اعتماد",
  "approval.bypass_revoked": "سحب تجاوز اعتماد",
  "approval.request_created": "إنشاء طلب اعتماد",
  "builder.closed": "إغلاق نموذج مخصّص",
  "builder.created": "إنشاء نموذج مخصّص",
  "builder.deleted": "حذف نموذج مخصّص",
  "builder.published": "نشر نموذج مخصّص",
  "builder.response_deleted": "حذف رد نموذج",
  "builder.response_received": "استلام رد نموذج",
  "builder.updated": "تحديث نموذج مخصّص",
  "campaign.deleted": "حذف حملة نموذج",
  "campaign.updated": "تحديث حملة نموذج",
  "commercial_registration.archived": "أرشفة سجل تجاري",
  "company.archived": "أرشفة شركة",
  "company.certificates_imported": "اعتماد استيراد شهادات الشركات",
  "company.created": "إنشاء شركة جديدة",
  "company.document_created": "رفع وثيقة شركة",
  "company.document_updated": "تحديث وثيقة شركة",
  "company.import_committed": "اعتماد استيراد شهادات الشركات",
  "company.registration_created": "إضافة سجل تجاري",
  "company.registration_updated": "تحديث سجل تجاري",
  "company.seeded": "تهيئة سجل شركة أساسي",
  "company.updated": "تحديث بيانات شركة",
  "company_document.archived": "أرشفة وثيقة شركة",
  "config.field_added": "إضافة حقل إعداد",
  "config.item_approved": "اعتماد بند إعداد",
  "config.item_proposed": "اقتراح بند إعداد",
  "config.item_saved": "حفظ بند إعداد",
  "debt.approved": "اعتماد مديونية",
  "debt.bulk_collect": "تحصيل مديونيات دفعة واحدة",
  "debt.collected": "تحصيل مديونية",
  "debt.created": "إنشاء مديونية",
  "debt.internet_advance_paid": "صرف سلفة نت",
  "debt.opening_approved": "اعتماد رصيد افتتاحي للمديونية",
  "debt.opening_imported": "استيراد أرصدة افتتاحية للمديونيات",
  "debt.opening_reconciled": "مطابقة افتتاح مديونية",
  "debt.opening_rejected": "رفض رصيد افتتاحي للمديونية",
  "debt.rescheduled": "إعادة جدولة مديونية",
  "debt.sheet.inactive_rider_staged": "تجهيز مديونية لمندوب غير نشط",
  "debt.sheet.opening_staged": "تجهيز افتتاح من شيت المديونيات",
  "debt.sponsor_assigned": "تعيين كفيل للمديونية",
  "debt.transferred_sponsor": "نقل مديونية لكفيل آخر",
  "feature.promissory_notes": "ميزة سندات الأمر",
  "finance.adjustment_approved": "اعتماد تسوية مالية",
  "finance.adjustment_drafted": "مسودة تسوية مالية",
  "finance.jahez_fee": "رسوم جاهز",
  "finance.manual_approved": "اعتماد قيد يدوي",
  "finance.manual_cancelled": "إلغاء مسودة قيد يدوي",
  "finance.manual_deleted": "حذف قيد يدوي",
  "finance.manual_drafted": "مسودة قيد يدوي",
  "finance.manual_permissions_seeded": "تهيئة صلاحيات القيود اليدوية",
  "finance.manual_rejected": "رفض قيد يدوي",
  "finance.vault_transfer": "تحويل بين الخزائن",
  "form.close": "إغلاق نموذج",
  "form.publish": "نشر نموذج",
  "inquiry.access_issued": "إصدار صلاحية استعلام",
  "jahez.custody_transferred": "نقل عهدة جاهز",
  "jahez.fee_deposited": "إيداع رسوم جاهز",
  "jahez.handover_received": "استلام تسليم جاهز",
  "jahez.handover_submitted": "تقديم تسليم جاهز",
  "jahez.platform_settled": "تسوية منصة جاهز",
  "jahez.reconciliation_approved": "اعتماد مطابقة جاهز",
  "jahez.reconciliation_built": "بناء مطابقة جاهز",
  "jahez.reconciliation_submitted": "تقديم مطابقة جاهز",
  "jahez.reconciliation_variance_decided": "حسم فرق مطابقة جاهز",
  "journal.created": "إنشاء قيد",
  "journal.posted": "ترحيل قيد",
  "maintenance.rider_indexes.done": "انتهاء صيانة فهارس المناديب",
  "maintenance.rider_indexes.start": "بدء صيانة فهارس المناديب",
  "mobile.device_token_updated": "تحديث رمز جهاز المندوب",
  "mobile.fcm_unregistered": "إلغاء تسجيل إشعارات الجهاز",
  "mobile.firebase_configured": "تهيئة إشعارات التطبيق",
  "mobile.individual_notice": "إشعار فردي للمندوب",
  "mobile.login": "دخول تطبيق المندوب",
  "mobile.logout": "خروج تطبيق المندوب",
  "mobile.notice_acknowledged": "تأكيد استلام إشعار",
  "mobile.notice_read": "قراءة إشعار",
  "mobile.session_revoked": "إلغاء جلسة تطبيق",
  "mobile.statements_notified": "إشعار بكشوف الحساب",
  "notification.created": "إنشاء إشعار",
  "notification.personal_created": "إنشاء إشعار شخصي",
  "notification.system_created": "إنشاء إشعار نظام",
  "operations.rider_accounts_read": "قراءة حسابات المناديب",
  "org.delegation_added": "إضافة تفويض تنظيمي",
  "org.ended": "إنهاء سجل تنظيمي",
  "org.membership_added": "إضافة عضوية تنظيمية",
  "org.routing_added": "إضافة توجيه تنظيمي",
  "org.supervision_added": "إضافة إشراف تنظيمي",
  "pay.assignment_imported": "استيراد توزيع مندوب",
  "pay.config_approved": "اعتماد إعداد أجور",
  "pay.config_deleted": "حذف إعداد أجور",
  "pay.config_drafted": "مسودة إعداد أجور",
  "pay.config_rejected": "رفض إعداد أجور",
  "pay.consolidation_released": "تحرير توحيد مندوب مشترك",
  "pay.cycle_advanced": "تقدّم دورة رواتب",
  "pay.cycle_cancelled": "إلغاء دورة رواتب",
  "pay.cycle_created": "إنشاء دورة رواتب",
  "pay.cycle_reversal_requested": "طلب عكس دورة رواتب",
  "pay.cycle_reversed": "عكس دورة رواتب",
  "pay.debt_confirm": "تأكيد استقطاع ديون",
  "pay.debt_confirm-all": "تأكيد استقطاع ديون للجميع",
  "pay.debt_edit": "تعديل استقطاع ديون",
  "pay.invoice_uploaded": "رفع فاتورة منصة للدورة",
  "pay.operations_imported": "استيراد عمليات المنصة",
  "pay.review_adjust": "تعديل مراجعة مسير",
  "pay.review_approve": "اعتماد مراجعة مسير",
  "pay.review_approved": "اعتماد مراجعة مندوب في المسير",
  "pay.review_approved_all": "اعتماد مراجعة المسير للجميع",
  "pay.review_edit": "تعديل مراجعة مسير",
  "pay.salary_rows_skipped": "تخطي صفوف رواتب",
  "payment.record": "تسجيل دفعة",
  "payroll.approve": "اعتماد راتب",
  "platform.hs.allocation_approved": "اعتماد توزيع هنقرستيشن",
  "platform.hs.allocation_proposed": "اقتراح توزيع هنقرستيشن",
  "platform.hs.credit_reconciliation_pending": "مطابقة دائن هنقرستيشن معلّقة",
  "platform.hs.deduction_classified": "تصنيف خصم هنقرستيشن",
  "platform.hs.deduction_classified_confirmed": "تأكيد تصنيف خصم هنقرستيشن",
  "platform.hs.deduction_source_reversed": "عكس مصدر خصم هنقرستيشن",
  "platform.hs.deduction_tagged": "وسم خصم هنقرستيشن",
  "platform.hs.dispute_created": "إنشاء اعتراض هنقرستيشن",
  "platform.hs.dispute_rejected_by_platform": "رفض اعتراض من المنصة",
  "platform.hs.dispute_resolved": "حل اعتراض هنقرستيشن",
  "platform.hs.facts_approved": "اعتماد وقائع هنقرستيشن",
  "platform.hs.facts_proposed": "اقتراح وقائع هنقرستيشن",
  "platform.hs.invoice_allocation_completed": "اكتمال توزيع فاتورة هنقرستيشن",
  "platform.hs.invoice_approved": "اعتماد فاتورة هنقرستيشن",
  "platform.hs.invoice_discarded": "إهمال فاتورة هنقرستيشن",
  "platform.hs.invoice_held": "إيقاف فاتورة هنقرستيشن",
  "platform.hs.invoice_imported": "استيراد فاتورة هنقرستيشن",
  "platform.hs.invoice_line_allocated": "توزيع بند فاتورة هنقرستيشن",
  "platform.hs.invoice_line_imported": "استيراد بند فاتورة هنقرستيشن",
  "platform.hs.invoice_posted": "ترحيل فاتورة هنقرستيشن",
  "platform.hs.invoice_response_recorded": "تسجيل رد فاتورة هنقرستيشن",
  "platform.hs.invoice_reversed": "عكس فاتورة هنقرستيشن",
  "platform.hs.journal_posted": "ترحيل قيد هنقرستيشن",
  "platform.hs.reversal_approved": "اعتماد عكس فاتورة هنقرستيشن",
  "platform.hs.reversal_requested": "طلب عكس فاتورة هنقرستيشن",
  "platform.keeta.deduction_classified": "تصنيف خصم كيتا",
  "platform.keeta.invoice_approved": "اعتماد فاتورة كيتا",
  "platform.keeta.invoice_discarded": "إهمال فاتورة كيتا",
  "platform.keeta.invoice_imported": "استيراد فاتورة كيتا",
  "platform.keeta.invoice_posted": "ترحيل فاتورة كيتا",
  "platform.ninja.deduction_classified": "تصنيف خصم نينجا",
  "platform.ninja.deductions_classified": "تصنيف خصوم نينجا",
  "platform.ninja.dispute_resolved": "حل اعتراض نينجا",
  "platform.ninja.invoice_approved": "اعتماد فاتورة نينجا",
  "platform.ninja.invoice_discarded": "إهمال فاتورة نينجا",
  "platform.ninja.invoice_imported": "استيراد فاتورة نينجا",
  "platform.ninja.invoice_posted": "ترحيل فاتورة نينجا",
  "platform.posting_gate_changed": "تغيير بوابة الترحيل",
  "policy.approve": "اعتماد سياسة",
  "policy.create": "إنشاء سياسة",
  "promissory_note.created": "إنشاء سند أمر",
  "rider.save": "حفظ بيانات مندوب",
  "rider_ask.cancelled": "إلغاء طلب مندوب",
  "rider_ask.converted": "تحويل طلب مندوب",
  "rider_ask.created": "إنشاء طلب مندوب",
  "rider_ask.declined": "رفض طلب مندوب",
  "rider_ask.rider_replied": "رد المندوب على طلب",
  "rider_service_request.admin_created": "إنشاء طلب خدمة من الإدارة",
  "rider_service_request.admin_replied": "رد الإدارة على طلب خدمة",
  "rider_service_request.cancelled": "إلغاء طلب خدمة",
  "rider_service_request.created": "إنشاء طلب خدمة",
  "rider_service_request.rider_replied": "رد المندوب على طلب خدمة",
  "storage.migrated": "ترحيل التخزين",
  "submission.approved": "اعتماد رد نموذج",
  "submission.bulk_approved": "اعتماد ردود دفعة واحدة",
  "submission.bulk_deleted": "حذف ردود دفعة واحدة",
  "submission.deleted": "حذف رد نموذج",
  "submission.edited": "تعديل رد نموذج",
  "submission.received": "استلام رد نموذج",
  "submission.rejected": "رفض رد نموذج",
  "supplier.paid": "صرف لمورد",
  "treasury.bank_adjust_rejected": "رفض تسوية بنكية",
  "treasury.bank_adjust_requested": "طلب تسوية بنكية",
  "treasury.bank_statement_uploaded": "رفع كشف بنك",
  "treasury.bank_statement_auto_matched": "مطابقة كشف بنك تلقائية",
  "treasury.legacy_cash_allocated": "توزيع نقد قديم",
  "treasury.opening_reconciled": "مطابقة افتتاح خزينة",
  "sal.rider_request_approved": "اعتماد طلب إيقاف أو خصم",
  "sal.rider_request_created": "إنشاء طلب إيقاف أو خصم",
  "sal.rider_request_rejected": "رفض طلب إيقاف أو خصم",
  "sal.salary_self_approved": "اعتماد راتب من نفس المستخدم",
  "payment.document.approve": "اعتماد مستند دفع",
  "payment.document.created": "إنشاء مستند دفع",
  "payment.document.submit": "تقديم مستند دفع",
};

const ACTOR_LABELS_AR: Record<string, string> = {
  system: 'النظام',
  'system-migration': 'ترحيل النظام',
  'public-form': 'نموذج عام',
};

const AUDIT_ACTION_PREFIX_LABELS_AR: Array<{ prefix: string; label: string }> = [
  { prefix: 'excel.import.', label: 'استيراد ملف إكسل' },
  { prefix: 'pay.debt_', label: 'استقطاع ديون الدورة' },
  { prefix: 'pay.review_', label: 'مراجعة مسير الدورة' },
  { prefix: 'submission.', label: 'عملية على رد نموذج' },
  { prefix: 'approval.', label: 'عملية اعتماد' },
  { prefix: 'platform.', label: 'عملية منصة' },
  { prefix: 'sal.', label: 'عملية رواتب (مسار التبويبات)' },
  { prefix: 'treasury.', label: 'عملية خزينة' },
  { prefix: 'payment.', label: 'عملية مدفوعات' },
  { prefix: 'mudad.', label: 'عملية مدد' },
  { prefix: 'task.', label: 'مهمة موظفين' },
  { prefix: 'notify.', label: 'إشعار نظام' },
  { prefix: 'recurring_', label: 'مستفيد دوري' },
  { prefix: 'jahez.day_', label: 'يوم مطابقة جاهز' },
  { prefix: 'debt.', label: 'عملية مديونية' },
  { prefix: 'admin.notify_', label: 'إعدادات إشعارات' },
  { prefix: 'advance.', label: 'عملية سلفة' },
];

export function auditActionLabelAr(action: string | null | undefined): string {
  const key = (action || '').trim();
  if (!key) return 'عملية مسجلة';
  if (AUDIT_ACTION_LABELS_AR[key]) return AUDIT_ACTION_LABELS_AR[key];
  if (/[\u0600-\u06FF]/.test(key) && !/[A-Za-z]/.test(key)) return key;
  for (const row of AUDIT_ACTION_PREFIX_LABELS_AR) {
    if (key.startsWith(row.prefix)) return row.label;
  }
  if (key.endsWith('.archived')) return 'أرشفة سجل';
  return 'عملية إدارية مسجلة';
}

export function auditActorLabelAr(actor: string | null | undefined): string {
  const raw = (actor || '').trim();
  if (!raw) return 'غير معروف';
  if (ACTOR_LABELS_AR[raw]) return ACTOR_LABELS_AR[raw];
  return raw;
}

/** Show «@» only for real usernames, not Arabic system labels. */
export function formatAuditActorDisplay(actor: string | null | undefined): string {
  const raw = (actor || '').trim();
  const label = auditActorLabelAr(raw || 'system');
  if (!raw || ACTOR_LABELS_AR[raw]) return label;
  return `@${label}`;
}

/** Gregorian Arabic date-time for audit rows (avoids raw ISO and Hijri mismatch). */
export function formatAuditAt(value: string | null | undefined): string {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  try {
    return new Intl.DateTimeFormat('ar-EG', {
      calendar: 'gregory',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(d);
  } catch {
    return d.toISOString().slice(0, 16).replace('T', ' ');
  }
}

export function shortAuditId(id: string | null | undefined): string {
  const raw = (id || '').trim();
  if (!raw) return '—';
  if (raw.length <= 10) return raw;
  return `${raw.slice(0, 6)}…`;
}
