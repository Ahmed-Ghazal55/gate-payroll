/** Shared Arabic labels for audit action keys shown to operators. */

const AUDIT_ACTION_LABELS_AR: Record<string, string> = {
  'company.seeded': 'تهيئة سجل شركة أساسي',
  'company.created': 'إنشاء شركة جديدة',
  'company.updated': 'تحديث بيانات شركة',
  'company.archived': 'أرشفة شركة',
  'company.registration_created': 'إضافة سجل تجاري',
  'company.registration_updated': 'تحديث سجل تجاري',
  'commercial_registration.archived': 'أرشفة سجل تجاري',
  'company.document_created': 'رفع وثيقة شركة',
  'company.document_updated': 'تحديث وثيقة شركة',
  'company_document.archived': 'أرشفة وثيقة شركة',
  'company.import_committed': 'اعتماد استيراد شهادات الشركات',
  'operations.rider_accounts_read': 'قراءة حسابات المناديب',
  'notification.system_created': 'إنشاء إشعار نظام',
  'org.membership_added': 'إضافة عضوية تنظيمية',
  'feature.promissory_notes': 'ميزة سندات الأمر',
  'rider.save': 'حفظ بيانات مندوب',
  'admin.user_create': 'إنشاء مستخدم',
  'admin.setup': 'إعداد النظام',
  'admin.role_permissions': 'صلاحيات الأدوار',
  'admin.user_update': 'تحديث مستخدم',
  'admin.user_disable': 'تعطيل مستخدم',
  'advance.review': 'مراجعة سلفة',
  'debt.created': 'إنشاء مديونية',
  'debt.approved': 'اعتماد مديونية',
  'journal.posted': 'ترحيل قيد',
  'journal.created': 'إنشاء قيد',
};

const ACTOR_LABELS_AR: Record<string, string> = {
  system: 'النظام',
  'system-migration': 'ترحيل النظام',
};

export function auditActionLabelAr(action: string | null | undefined): string {
  const key = (action || '').trim();
  if (!key) return 'عملية مسجلة';
  if (AUDIT_ACTION_LABELS_AR[key]) return AUDIT_ACTION_LABELS_AR[key];
  if (/[\u0600-\u06FF]/.test(key) && !/[A-Za-z]/.test(key)) return key;
  // Fall back: last segment of dotted key, still prefer Arabic generic
  const last = key.includes('.') ? key.split('.').pop()! : key;
  if (AUDIT_ACTION_LABELS_AR[key] || AUDIT_ACTION_LABELS_AR[last]) {
    return AUDIT_ACTION_LABELS_AR[key] || AUDIT_ACTION_LABELS_AR[last];
  }
  return 'عملية إدارية مسجلة';
}

export function auditActorLabelAr(actor: string | null | undefined): string {
  const raw = (actor || '').trim();
  if (!raw) return 'غير معروف';
  if (ACTOR_LABELS_AR[raw]) return ACTOR_LABELS_AR[raw];
  return raw;
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
