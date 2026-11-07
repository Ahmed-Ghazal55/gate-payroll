import type {
  UserProfile,
  NotificationItem,
  NotificationSummary,
  SearchResult,
  CompanySponsor,
  Company,
  CommercialRegistration,
  CompanyDocument,
  ExpiringRecord,
  CompanyImportPlan,
  DashboardMetrics,
  Campaign,
  Submission,
  CustomForm,
  PaginatedResult,
  ApiErrorPayload,
  HsInvoice,
  HsDispute,
  HsAllocation,
  HsFact,
  HsProposedJournal,
  HsImportFile,
  HsImportPreviewResult,
  FinanceAccount,
  FinanceJournal,
  BankPayrollFilePreview,
  DebtRecord,
  ApprovalRequest,
  PromissoryNote,
  RiderRecord,
  RiderProfile360,
  RidersDataQuality,
  RidersBaselineSyncResult,
  RiderStatement,
  TargetCoaResponse,
  TrialBalanceResponse,
  IncomeStatementResponse,
  AdvanceRequest,
  AdvanceRequestSummary,
  OrgCatalog,
  OrgMembership,
  OrgDelegation,
  OrgSupervision,
  OrgRouting,
  OrgResponsibleUser,
  ErpSyncJobStatus,
  ErpSyncRun,
  PayReviewResponse,
  PayReviewRow,
  ConfigListGroup,
  ConfigFieldDef,
  ManualJournal,
  NinjaReconcileResponse,
  ImportCatalogStep,
  ImportPreviewResult,
} from './types';
import type { paths, components } from './api-contract';

export type ApiResp<
  P extends keyof paths,
  M extends keyof paths[P] = 'get'
> = paths[P][M] extends { responses: { 200: { content: { 'application/json': infer R } } } }
  ? R
  : paths[P][M] extends { responses: { 200?: { content: { 'application/json': infer R } } } }
  ? R
  : never;

export type NinjaInvoice = ApiResp<'/api/platforms/ninja/invoices/{id}'>;
export type NinjaInvoiceLine = NonNullable<NinjaInvoice['lines']>[number];
export type NinjaInvoicesResponse = ApiResp<'/api/platforms/ninja/invoices'>;

export type PayReviewDraft = ApiResp<'/api/pay/review-draft'>;
export type PayReviewBlocker = PayReviewDraft['blockers'][number];
export type PayReviewInvoice = PayReviewDraft['invoices'][number];
export type PayReviewAccount = PayReviewInvoice['accounts'][number];
export type PayReviewRider = PayReviewAccount['riders'][number];

export type RiderLookupItem = ApiResp<'/api/riders/lookup'>[number];

export type PayCycleItem = ApiResp<'/api/pay/cycles'>['items'][number];
export type PayCyclesResponse = ApiResp<'/api/pay/cycles'>;
export type PayCycleLinesResponse = ApiResp<'/api/pay/cycles/{id}/lines'>;
export type PayCycleLineItem = PayCycleLinesResponse['items'][number];

export type PayCatalog = ApiResp<'/api/pay/catalog'>;
export type PayEligibilityResponse = ApiResp<'/api/pay/apps/{app}/eligibility'>;
export type PayEligibilityItem = PayEligibilityResponse['items'][number];

export type ManualJournalItem = ApiResp<'/api/finance/journals/manual'>['items'][number];
export type ManualJournalLineItem = NonNullable<ManualJournalItem['lines']>[number];

const isMock = typeof process !== 'undefined' && process.env.NEXT_PUBLIC_MOCK_API === '1';

export class ApiError extends Error {
  code?: string;
  status: number;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

// Memory store for mocked data
let mockNotifications: NotificationItem[] = [
  {
    id: 'notif-1',
    title: 'ردود بانتظار المراجعة',
    message: 'وصلت 5 ردود جديدة لحملة «تحديث الآيبان - أكتوبر».',
    category: 'approvals',
    unread: true,
    createdAt: new Date(Date.now() - 15 * 60 * 1000).toISOString(),
    link: '/app/forms/?tab=responses',
    actionText: 'مراجعة الردود',
  },
  {
    id: 'notif-2',
    title: 'اعتماد مسير رواتب',
    message: 'تم اعتماد مسير رواتب شركة البوابة (BW) لشهر 2026-10 بنجاح.',
    category: 'system',
    unread: true,
    createdAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    link: '/app/payroll/',
  },
  {
    id: 'notif-3',
    title: 'تنبيه جودة البيانات',
    message: 'يوجد مندوبان بلا آيبان معتمد في كشوف الحساب النشطة.',
    category: 'general',
    unread: false,
    createdAt: new Date(Date.now() - 24 * 3600 * 1000).toISOString(),
    link: '/app/riders/',
  },
  {
    id: 'notif-4',
    title: 'إشعار إداري خاص',
    message: 'تم إسناد مراجعة استمارات التشغيل الميداني لحسابك.',
    category: 'personal',
    unread: true,
    createdAt: new Date(Date.now() - 5 * 3600 * 1000).toISOString(),
  },
];

let mockCampaigns: Campaign[] = [
  {
    id: 'camp-101',
    title: 'تحديث بيانات الآيبان 2026',
    kind: 'iban',
    status: 'open',
    expiresAt: new Date(Date.now() + 10 * 86400 * 1000).toISOString(),
    createdAt: new Date(Date.now() - 4 * 86400 * 1000).toISOString(),
    path: '/forms/iban-2026',
    responseCount: 24,
    cities: [
      { ar: 'الرياض', en: 'Riyadh' },
      { ar: 'جدة', en: 'Jeddah' },
      { ar: 'الدمام', en: 'Dammam' },
    ],
  },
  {
    id: 'camp-102',
    title: 'اختيار نظام العمل — مناديب الرياض',
    kind: 'work-choice',
    status: 'open',
    expiresAt: new Date(Date.now() + 5 * 86400 * 1000).toISOString(),
    createdAt: new Date(Date.now() - 8 * 86400 * 1000).toISOString(),
    path: '/forms/work-choice-ruh',
    responseCount: 42,
  },
  {
    id: 'camp-103',
    title: 'حملة تسوية سبتمبر السابقة',
    kind: 'work-choice',
    status: 'closed',
    expiresAt: new Date(Date.now() - 2 * 86400 * 1000).toISOString(),
    createdAt: new Date(Date.now() - 20 * 86400 * 1000).toISOString(),
    path: '/forms/sep-closed',
    responseCount: 65,
  },
];

let mockSubmissions: Submission[] = [
  {
    id: 'sub-1',
    campaignId: 'camp-101',
    campaignTitle: 'تحديث بيانات الآيبان 2026',
    name: 'أحمد صالح المحمدي',
    iqama: '2000000111',
    phone: '0501112233',
    city: 'الرياض',
    iban: 'SA0380000000608010167519',
    bank: 'RJHI',
    platforms: ['keeta', 'jahez'],
    status: 'pending',
    version: 1,
    createdAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    history: [],
  },
  {
    id: 'sub-2',
    campaignId: 'camp-101',
    campaignTitle: 'تحديث بيانات الآيبان 2026',
    name: 'سالم فهد القحطاني',
    iqama: '2000000222',
    phone: '0552223344',
    city: 'جدة',
    iban: 'SA4410000001234567890123',
    bank: 'NCBK',
    platforms: ['hungerstation'],
    status: 'pending',
    version: 2,
    isEdited: true,
    createdAt: new Date(Date.now() - 12 * 3600 * 1000).toISOString(),
    updatedAt: new Date(Date.now() - 1 * 3600 * 1000).toISOString(),
    history: [
      {
        version: 1,
        status: 'pending',
        iban: 'SA4410000001234567890000',
        bank: 'NCBK',
        submittedAt: new Date(Date.now() - 12 * 3600 * 1000).toISOString(),
        reviewNote: 'طلب تعديل الحساب البنكي',
      },
    ],
  },
  {
    id: 'sub-3',
    campaignId: 'camp-102',
    campaignTitle: 'اختيار نظام العمل — مناديب الرياض',
    name: 'محمد عبدالله الشمري',
    iqama: '2000000333',
    phone: '0543334455',
    city: 'الرياض',
    workSystem: 'commission',
    status: 'approved',
    version: 1,
    createdAt: new Date(Date.now() - 48 * 3600 * 1000).toISOString(),
    reviewNote: 'تمت مطابقة الهوية واعتماد النظام.',
  },
  {
    id: 'sub-4',
    campaignId: 'camp-102',
    campaignTitle: 'اختيار نظام العمل — مناديب الرياض',
    name: 'عبدالرحمن خالد العتيبي',
    iqama: '2000000444',
    phone: '0564445566',
    city: 'الرياض',
    workSystem: 'tiers',
    status: 'rejected',
    version: 1,
    createdAt: new Date(Date.now() - 72 * 3600 * 1000).toISOString(),
    reviewNote: 'رقم الهوية غير مطابق لبيانات النظام.',
  },
];

let mockForms: CustomForm[] = [
  {
    id: 'form-1',
    title: 'استبيان استلام المركبات والعهد',
    status: 'published',
    responsesCount: 19,
    createdAt: new Date(Date.now() - 5 * 86400 * 1000).toISOString(),
    questions: [
      { id: 'q1', label: 'رقم لوحة المركبة المسلمة', type: 'text', required: true },
      { id: 'q2', label: 'حالة الفحص الفني', type: 'choice', required: true, options: ['ممتازة', 'جيدة', 'تحتاج صيانة'] },
    ],
  },
  {
    id: 'form-2',
    title: 'طلب تحويل السكن الميداني',
    status: 'draft',
    responsesCount: 0,
    createdAt: new Date(Date.now() - 1 * 86400 * 1000).toISOString(),
    questions: [
      { id: 'q1', label: 'المدينة الحالية', type: 'text', required: true },
      { id: 'q2', label: 'سبب النقل', type: 'text', required: true },
    ],
  },
];

async function handleRequest<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  // Use mock store only when NEXT_PUBLIC_MOCK_API is explicitly set to '1' at build time
  if (isMock) {
    return handleMockRequest<T>(endpoint, options);
  }

  const url = endpoint.startsWith('http') ? endpoint : `/api${endpoint}`;
  const res = await fetch(url, {
    ...options,
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });

  if (res.status === 401) {
    if (typeof window !== 'undefined') {
      const p = window.location.pathname;
      if (!p.startsWith('/app/login') && !p.startsWith('/app/setup')) {
        const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.href = `/app/login/?returnUrl=${returnUrl}`;
      }
    }
    const payload: ApiErrorPayload = await res.json().catch(() => ({ error: 'انتهت الجلسة؛ يرجى تسجيل الدخول مجدداً.' }));
    throw new ApiError(payload.error || 'انتهت الجلسة؛ يرجى تسجيل الدخول مجدداً.', 401, payload.code || 'unauthorized');
  }

  if (!res.ok) {
    const payload: ApiErrorPayload = await res.json().catch(() => ({ error: 'حدث خطأ في معالجة الطلب' }));
    throw new ApiError(payload.error || 'فشل الطلب', res.status, payload.code);
  }

  // If blob response
  if (
    res.headers.get('content-type')?.includes('application/octet-stream') ||
    res.headers.get('content-type')?.includes('application/vnd.openxmlformats') ||
    res.headers.get('content-type')?.includes('text/csv')
  ) {
    return (await res.blob()) as unknown as T;
  }

  return (await res.json()) as T;
}

// Mock resolver for new 1.0 routes
async function handleMockRequest<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method || 'GET').toUpperCase();
  const [path, queryString] = endpoint.split('?');
  const params = new URLSearchParams(queryString || '');
  const body = options.body ? JSON.parse(String(options.body)) : null;

  // Auth & Me
  if (path === '/auth/status') {
    return {
      needsSetup: false,
      environment: 'Development',
    } as unknown as T;
  }

  if (path === '/auth/setup' || path === '/auth/login') {
    return {
      username: body?.username || 'admin',
      role: 'Admin',
      displayNameAr: 'مدير مساحة الرواتب',
      permissions: ['all'],
    } as unknown as T;
  }

  if (path === '/auth/logout') {
    return { ok: true } as unknown as T;
  }

  if (path === '/me' || path === '/auth/me') {
    return {
      username: 'admin',
      role: 'Admin',
      displayNameAr: 'مدير مساحة الرواتب',
      permissions: ['all'],
    } as unknown as T;
  }

  // Dashboard
  if (path === '/dashboard') {
    return {
      riders: 142,
      imports: 18,
      pendingResponses: mockSubmissions.filter(s => s.status === 'pending').length,
      approvedRuns: 12,
      latestAudit: [
        { id: 'aud-1', action: 'اعتماد ردود حملة الآيبان', actor: 'admin', at: new Date(Date.now() - 30 * 60 * 1000).toISOString() },
        { id: 'aud-2', action: 'إنشاء حملة اختيار نظام العمل', actor: 'admin', at: new Date(Date.now() - 2 * 3600 * 1000).toISOString() },
        { id: 'aud-3', action: 'تصدير ملف بنك معتمد (CSV)', actor: 'finance_mgr', at: new Date(Date.now() - 6 * 3600 * 1000).toISOString() },
      ],
    } as unknown as T;
  }

  // Notifications summary
  if (path === '/notifications/summary') {
    const unread = mockNotifications.filter(n => n.unread).length;
    const approvals = mockNotifications.filter(n => n.category === 'approvals' && n.unread).length;
    return {
      unreadCount: unread,
      approvalCount: approvals,
      byCategory: {
        general: mockNotifications.filter(n => n.category === 'general' && n.unread).length,
        personal: mockNotifications.filter(n => n.category === 'personal' && n.unread).length,
        system: mockNotifications.filter(n => n.category === 'system' && n.unread).length,
        approvals: approvals,
      },
    } as unknown as T;
  }

  // Notifications list
  if (path === '/notifications') {
    if (method === 'GET') {
      const cat = params.get('category');
      const unreadOnly = params.get('unread') === 'true';
      let items = [...mockNotifications];
      if (cat && cat !== 'all') {
        items = items.filter(n => n.category === cat);
      }
      if (unreadOnly) {
        items = items.filter(n => n.unread);
      }
      return items as unknown as T;
    }
  }

  // Mark all notifications read
  if (path === '/notifications/read-all' && method === 'POST') {
    mockNotifications = mockNotifications.map(n => ({ ...n, unread: false }));
    return { success: true } as unknown as T;
  }

  // Mark single notification read
  if (path.match(/\/notifications\/[^/]+\/read/) && method === 'POST') {
    const id = path.split('/')[2];
    const item = mockNotifications.find(n => n.id === id);
    if (item) item.unread = false;
    return { success: true } as unknown as T;
  }

  // Global search
  if (path === '/search') {
    const q = (params.get('q') || '').toLowerCase().trim();
    if (!q) return [] as unknown as T;
    const results: SearchResult[] = [];
    if ('أحمد'.includes(q) || '2000000111'.includes(q) || 'مندوب'.includes(q)) {
      results.push({
        id: '2000000111',
        kind: 'rider',
        title: 'أحمد صالح المحمدي (2000000111)',
        subtitle: 'مندوب نشط · شركة البوابة (BW)',
        url: '/app/riders/profile/?iqama=2000000111',
      });
    }
    if ('بوابة'.includes(q) || 'bw'.includes(q)) {
      results.push({
        id: 'BW',
        kind: 'company',
        title: 'شركة البوابة المقبلة للتجارة (BW)',
        subtitle: 'سجل تجاري 7015658094',
        url: '/app/companies/?code=BW',
      });
    }
    if ('آيبان'.includes(q) || 'حملة'.includes(q)) {
      results.push({
        id: 'camp-101',
        kind: 'campaign',
        title: 'تحديث بيانات الآيبان 2026',
        subtitle: 'حملة استقبال مفتوحة',
        url: '/app/forms/?tab=campaigns',
      });
    }
    return results as unknown as T;
  }

  // Companies / Sponsors
  if (path === '/companies' || path === '/finance/sponsors') {
    const sponsors: CompanySponsor[] = [
      { id: '019c18d5-62e1-7000-8000-000000000041', code: 'BW', shortNameAr: 'البوابة', nameAr: 'شركة البوابة المقبلة للتجارة', commercialReg: '7015658094' },
      { id: '019c18d5-62e1-7000-8000-000000000042', code: 'EX', shortNameAr: 'إكسبرس', nameAr: 'شركة إكسبرس جايت', commercialReg: '7034861059' },
      { id: '019c18d5-62e1-7000-8000-000000000040', code: 'TR', shortNameAr: 'التجارية', nameAr: 'مؤسسة البوابة المقبلة التجارية', commercialReg: '7038745530' },
    ];
    return sponsors as unknown as T;
  }

  // Campaigns
  if (path === '/campaigns') {
    if (method === 'GET') {
      return mockCampaigns as unknown as T;
    }
    if (method === 'POST') {
      const newCamp: Campaign = {
        id: 'camp-' + Date.now(),
        title: body.title || 'حملة جديدة',
        kind: body.kind || 'work-choice',
        status: 'open',
        expiresAt: new Date(Date.now() + (body.days || 14) * 86400 * 1000).toISOString(),
        createdAt: new Date().toISOString(),
        path: `/forms/c-${Date.now().toString(36)}`,
        responseCount: 0,
        cities: body.cities,
      };
      mockCampaigns.unshift(newCamp);
      return newCamp as unknown as T;
    }
  }

  // Close campaign
  if (path.match(/\/campaigns\/[^/]+\/close/) && method === 'POST') {
    const id = path.split('/')[2];
    const camp = mockCampaigns.find(c => c.id === id);
    if (camp) camp.status = 'closed';
    return { ok: true } as unknown as T;
  }

  // Delete preview campaign
  if (path.match(/\/campaigns\/[^/]+\/delete-preview/) && method === 'GET') {
    const id = path.split('/')[2];
    const camp = mockCampaigns.find(c => c.id === id);
    if (!camp) throw new ApiError('النموذج غير موجود.', 404);
    return {
      id,
      title: camp.title,
      responseCount: camp.responseCount,
      fingerprint: 'mock-fp-' + id + '-' + camp.responseCount,
    } as unknown as T;
  }

  // Delete campaign
  if (path.match(/\/campaigns\/[^/]+$/) && method === 'DELETE') {
    const id = path.split('/')[2];
    if (!body?.confirm) throw new ApiError('أكد حذف النموذج وردوده نهائيًا.', 400);
    const camp = mockCampaigns.find(c => c.id === id);
    mockCampaigns = mockCampaigns.filter(c => c.id !== id);
    return { ok: true, deletedResponses: camp?.responseCount || 0, message: 'تم حذف النموذج وردوده نهائيًا.' } as unknown as T;
  }

  // Submissions
  if (path === '/submissions' || path === '/responses') {
    if (method === 'GET') {
      const campId = params.get('campaignId');
      const status = params.get('status');
      let items = [...mockSubmissions];
      if (campId) items = items.filter(s => s.campaignId === campId);
      if (status && status !== 'all') {
        if (status === 'edited') items = items.filter(s => s.isEdited);
        else items = items.filter(s => s.status === status);
      }
      return items as unknown as T;
    }
  }

  // Bulk approve submissions
  if (path === '/submissions/bulk-approve' && method === 'POST') {
    const ids: string[] = body.ids || [];
    let count = 0;
    mockSubmissions.forEach(s => {
      if (ids.includes(s.id) && s.status === 'pending') {
        s.status = 'approved';
        count++;
      }
    });
    return { ok: true, count } as unknown as T;
  }

  // Bulk delete submissions
  if (path === '/submissions/bulk-delete' && method === 'POST') {
    const ids: string[] = body.ids || [];
    const count = ids.length;
    mockSubmissions = mockSubmissions.filter(s => !ids.includes(s.id));
    return { ok: true, count } as unknown as T;
  }

  // Single submission review
  if (path.match(/\/submissions\/[^/]+\/review/) && method === 'POST') {
    const id = path.split('/')[2];
    const sub = mockSubmissions.find(s => s.id === id);
    if (sub) {
      if (body.version && sub.version !== body.version) {
        throw new ApiError('تم تعديل الرد من قبل مستخدم آخر؛ أعد تحميل الرد أولاً.', 409, 'concurrency_conflict');
      }
      sub.status = body.status;
      sub.reviewNote = body.note || '';
      sub.updatedAt = new Date().toISOString();
    }
    return { ok: true } as unknown as T;
  }

  // Single submission delete
  if (path.match(/\/submissions\/[^/]+$/) && method === 'DELETE') {
    const id = path.split('/')[2];
    mockSubmissions = mockSubmissions.filter(s => s.id !== id);
    return { ok: true } as unknown as T;
  }

  // Custom forms builder
  if (path === '/forms/builder') {
    if (method === 'GET') {
      return mockForms as unknown as T;
    }
    if (method === 'POST') {
      const newForm: CustomForm = {
        id: 'form-' + Date.now(),
        title: body.title,
        status: 'draft',
        questions: body.questions || [],
        responsesCount: 0,
        createdAt: new Date().toISOString(),
      };
      mockForms.unshift(newForm);
      return newForm as unknown as T;
    }
  }

  // Custom form publish
  if (path.match(/\/forms\/builder\/[^/]+\/publish/) && method === 'POST') {
    const id = path.split('/')[3];
    const form = mockForms.find(f => f.id === id);
    if (form) form.status = 'published';
    return { ok: true } as unknown as T;
  }

  // Custom form delete preview
  if (path.match(/\/forms\/builder\/[^/]+\/delete-preview/) && method === 'GET') {
    const id = path.split('/')[3];
    const form = mockForms.find(f => f.id === id);
    if (!form) throw new ApiError('النموذج غير موجود.', 404);
    return {
      id,
      title: form.title,
      responseCount: form.responsesCount,
      fingerprint: 'mock-form-fp-' + id,
    } as unknown as T;
  }

  // Custom form delete
  if (path.match(/\/forms\/builder\/[^/]+$/) && method === 'DELETE') {
    const id = path.split('/')[3];
    if (!body?.confirm) throw new ApiError('أكد حذف النموذج وردوده نهائيًا.', 400);
    mockForms = mockForms.filter(f => f.id !== id);
    return { ok: true, deletedResponses: 0, message: 'تم حذف النموذج وردوده نهائيًا.' } as unknown as T;
  }

  // Excel export
  if (path === '/excel/export') {
    const content = 'Mock Excel Export File';
    return new Blob([content], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }) as unknown as T;
  }

  return {} as unknown as T;
}

// Exported typed API methods
// The API stores debts with totalAmount/remainingBalance (Debts.cs); older UI code reads
// originalAmount/remainingAmount. Normalise once here so every screen gets numbers, never undefined.
function num(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : 0;
}
export function normalizeDebt(r: Record<string, unknown>): DebtRecord {
  const total = num(r.totalAmount ?? r.originalAmount);
  const remaining = num(r.remainingBalance ?? r.remainingAmount);
  return {
    ...(r as unknown as DebtRecord),
    id: String(r.id ?? ''),
    iqama: String(r.iqama ?? ''),
    category: String(r.category ?? ''),
    originalAmount: total,
    remainingAmount: remaining,
    monthlyInstallment: num(r.monthlyInstallment ?? r.monthlyDeduction),
    status: String(r.status ?? 'pending_approval'),
    createdAt: String(r.createdAt ?? r.balanceAsOf ?? ''),
  };
}

export const api = {
  // Auth
  getAuthStatus: () => handleRequest<{ needsSetup: boolean; environment: string }>('/auth/status'),
  setup: (data: { username: string; password: string; bootstrapKey?: string }) =>
    handleRequest<UserProfile>('/auth/setup', { method: 'POST', body: JSON.stringify(data) }),
  login: (data: { username: string; password: string }) =>
    handleRequest<UserProfile>('/auth/login', { method: 'POST', body: JSON.stringify(data) }),
  logout: () => handleRequest<{ ok: boolean }>('/auth/logout', { method: 'POST' }),

  getMe: () => handleRequest<UserProfile>('/me'),
  getDashboard: () => handleRequest<DashboardMetrics>('/dashboard'),
  getNotificationsSummary: async () => {
    const raw = await handleRequest<Record<string, number>>('/notifications/summary');
    const general = raw.general || 0;
    const personal = raw.personal || 0;
    const system = raw.system || 0;
    const approvals = raw.approvals || 0;
    const total = general + personal + system + approvals;
    return {
      unreadCount: total,
      approvalCount: approvals,
      byCategory: {
        general,
        personal,
        system,
        approvals,
      },
    } as NotificationSummary;
  },
  getNotifications: async (params?: { category?: string; unread?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.category) qs.set('category', params.category);
    if (params?.unread) qs.set('unread', 'true');
    const res = await handleRequest<any[]>(`/notifications?${qs.toString()}`);
    return (res || []).map((n) => ({
      ...n,
      title: n.titleAr || n.title || 'إشعار جديد',
      message: n.bodyAr || n.message || '',
      category: n.category || 'general',
      unread: Boolean(n.unread),
      requiresAction: Boolean(n.requiresAction),
      actionStatus: n.actionStatus || 'none',
      link: n.link || (n.taskKind === 'advance_request' ? '/debts/?tab=advance_requests' : undefined),
      actionText: n.actionText || (n.taskKind === 'advance_request' ? 'عرض طلب السلفة' : undefined),
    })) as NotificationItem[];
  },
  markNotificationRead: (id: string) => handleRequest<{ success: boolean }>(`/notifications/${id}/read`, { method: 'POST' }),
  markAllNotificationsRead: () => handleRequest<{ success: boolean }>('/notifications/read-all', { method: 'POST' }),
  search: (query: string) => handleRequest<SearchResult[]>(`/search?q=${encodeURIComponent(query)}`),
  getCompanies: () => handleRequest<CompanySponsor[]>('/companies'),

  // Companies & Registrations (Track L2 / M1.2)
  getCompaniesList: () => handleRequest<Company[]>('/companies'),
  getExpiringCompanies: (days = 60) => handleRequest<ExpiringRecord[]>(`/companies/expiring?days=${days}`),
  getCompany: (id: string) => handleRequest<Company>(`/companies/${id}`),
  getCompanyRegistrations: (id: string) => handleRequest<CommercialRegistration[]>(`/companies/${id}/registrations`),
  getCompanyDocuments: (id: string) => handleRequest<CompanyDocument[]>(`/companies/${id}/documents`),
  uploadCompanyDocument: (companyId: string, doc: { type: string; number?: string; expiryDate?: string; fileName: string; dataBase64: string }) =>
    handleRequest<CompanyDocument>(`/companies/${companyId}/documents`, { method: 'POST', body: JSON.stringify(doc) }),
  deleteCompanyDocument: (companyId: string, documentId: string) =>
    handleRequest<{ ok: boolean }>(`/companies/${companyId}/documents/${documentId}`, { method: 'DELETE' }),
  previewCompanyImport: (companies: any[]) =>
    handleRequest<CompanyImportPlan>('/companies/import/preview', { method: 'POST', body: JSON.stringify({ companies }) }),
  commitCompanyImport: (payload: { rows: any[]; fingerprint: string; confirm: boolean }) =>
    handleRequest<{ counts: any }>('/companies/import/commit', { method: 'POST', body: JSON.stringify(payload) }),
  getAuditLogs: () => handleRequest<Array<{ id: string; action: string; actor: string; at: string }>>('/audit'),

  // Campaigns
  getCampaigns: () => handleRequest<Campaign[]>('/campaigns'),
  createCampaign: (data: Partial<Campaign> & { days?: number }) => handleRequest<Campaign>('/campaigns', { method: 'POST', body: JSON.stringify(data) }),
  closeCampaign: (id: string) => handleRequest<{ ok: boolean }>(`/campaigns/${id}/close`, { method: 'POST' }),
  updateCampaign: (id: string, input: { expiresAt?: string; status?: 'open' | 'closed'; title?: string }) =>
    handleRequest<{ ok: boolean }>(`/campaigns/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  getCampaignDeletePreview: (id: string) =>
    handleRequest<{ id: string; title: string; responseCount: number; fingerprint: string }>(
      `/campaigns/${id}/delete-preview`
    ),
  deleteCampaign: (id: string, fingerprint: string) =>
    handleRequest<{ ok: boolean; deletedResponses: number; message: string }>(
      `/campaigns/${id}`,
      {
        method: 'DELETE',
        body: JSON.stringify({ confirm: true, fingerprint }),
      }
    ),

  // Submissions & Responses (Unified inbox with fallback)
  getSubmissions: async (params?: { campaignId?: string; status?: string }) => {
    const qs = new URLSearchParams();
    if (params?.campaignId) qs.set('campaignId', params.campaignId);
    if (params?.status) qs.set('status', params.status);
    const query = qs.toString() ? `?${qs.toString()}` : '';

    try {
      // The unified inbox nests the answer fields under `answers`; flatten them into the
      // Submission shape the screens read (row fields win over answer fields).
      const rows = await handleRequest<Array<Record<string, unknown> & { answers?: Record<string, unknown> }>>(`/responses${query}`);
      return rows.map(({ answers, ...row }) => ({
        ...(answers || {}),
        ...row,
        createdAt: (row.submittedAt as string) || (row.createdAt as string),
      })) as unknown as Submission[];
    } catch (err: any) {
      if (err.status === 404) {
        return await handleRequest<Submission[]>(`/submissions${query}`);
      }
      throw err;
    }
  },
  bulkApproveSubmissions: (campaignId: string | undefined, ids: string[]) =>
    handleRequest<{ ok: boolean; count: number }>('/submissions/bulk-approve', {
      method: 'POST',
      body: JSON.stringify({ campaignId, ids }),
    }),
  bulkDeleteSubmissions: (campaignId: string | undefined, ids: string[]) =>
    handleRequest<{ ok: boolean; count: number }>('/submissions/bulk-delete', {
      method: 'POST',
      body: JSON.stringify({ campaignId, ids, confirm: true }),
    }),
  reviewSubmission: (
    id: string,
    data: {
      status: 'approved' | 'rejected';
      note?: string;
      version: number;
      identityReviewed?: boolean;
      ibanOwnershipReviewed?: boolean;
    }
  ) =>
    handleRequest<{ ok: boolean }>(`/submissions/${id}/review`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  deleteSubmission: (id: string) => handleRequest<{ ok: boolean }>(`/submissions/${id}`, { method: 'DELETE' }),

  // Custom Form Builder
  getCustomForms: () => handleRequest<CustomForm[]>('/forms/builder'),
  createCustomForm: (data: Partial<CustomForm>) => handleRequest<CustomForm>('/forms/builder', { method: 'POST', body: JSON.stringify(data) }),
  publishCustomForm: (id: string) => handleRequest<{ ok: boolean }>(`/forms/builder/${id}/publish`, { method: 'POST' }),
  closeCustomForm: (id: string) => handleRequest<{ ok: boolean }>(`/forms/builder/${id}/close`, { method: 'POST' }),
  getCustomFormDeletePreview: (id: string) =>
    handleRequest<{ id: string; title: string; responseCount: number; fingerprint: string }>(
      `/forms/builder/${id}/delete-preview`
    ),
  deleteCustomForm: (id: string, fingerprint: string) =>
    handleRequest<{ ok: boolean; deletedResponses: number; message: string }>(
      `/forms/builder/${id}`,
      {
        method: 'DELETE',
        body: JSON.stringify({ confirm: true, fingerprint }),
      }
    ),

  // Table Export (Strictly adhering to Frontend/src/table-tools.ts:33-38)
  exportExcel: async (payload: { headers: string[]; rows: (string | number)[][]; numericColumns: number[]; filename?: string }) => {
    if (isMock) {
      return new Blob(['Mock Excel Binary'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    }
    const res = await fetch('/api/excel/export', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        headers: payload.headers,
        rows: payload.rows,
        numericColumns: payload.numericColumns,
      }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'تعذر تصدير Excel' }));
      throw new ApiError(err.error || 'تعذر تصدير Excel', res.status);
    }
    return res.blob();
  },

  // ==========================================
  // HungerStation Platform Review (R3 Part A)
  // ==========================================
  getHsInvoices: (params?: { companyId?: string; period?: string; status?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.companyId) q.set('companyId', params.companyId);
    if (params?.period) q.set('period', params.period);
    if (params?.status) q.set('status', params.status);
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<PaginatedResult<HsInvoice>>(`/platforms/hs/invoices${qs}`);
  },

  previewHsImport: (input: { period: string; files: HsImportFile[] }) =>
    handleRequest<HsImportPreviewResult>('/platforms/hs/invoices/import/preview', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  commitHsImport: (input: { period: string; files: HsImportFile[]; rows: any[]; fingerprint: string; confirm: boolean }) =>
    handleRequest<{ ok: boolean; count: number; invoiceId?: string }>('/platforms/hs/invoices/import/commit', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  approveHsInvoice: (id: string, input: { rows: any[]; fingerprint: string; confirm: boolean }) =>
    handleRequest<HsInvoice>(`/platforms/hs/invoices/${encodeURIComponent(id)}/approve`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  getHsDisputes: (params?: { companyId?: string; period?: string; status?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.companyId) q.set('companyId', params.companyId);
    if (params?.period) q.set('period', params.period);
    if (params?.status) q.set('status', params.status);
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<PaginatedResult<HsDispute>>(`/platforms/hs/disputes${qs}`);
  },

  createHsDispute: (invoiceId: string, input: { lineId: string; category: string; amount: number; reason: string }) =>
    handleRequest<HsDispute>(`/platforms/hs/invoices/${encodeURIComponent(invoiceId)}/dispute`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  resolveHsDispute: (
    invoiceId: string,
    input: { mode: 'dispute'; disputeId: string; outcome: 'platform_credit' | 'platform_rejected'; platformReference: string; reason: string }
  ) =>
    handleRequest<HsDispute>(`/platforms/hs/invoices/${encodeURIComponent(invoiceId)}/resolve`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  getHsAllocations: (params?: { companyId?: string; period?: string; status?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.companyId) q.set('companyId', params.companyId);
    if (params?.period) q.set('period', params.period);
    if (params?.status) q.set('status', params.status);
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<PaginatedResult<HsAllocation>>(`/platforms/hs/allocations${qs}`);
  },

  splitHsAllocation: (
    lineId: string,
    input: { companyAmount?: number; riderAmount?: number; riderId?: string; riderPercent?: number }
  ) =>
    handleRequest<HsAllocation>(`/platforms/hs/allocations/${encodeURIComponent(lineId)}`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  approveHsAllocation: (id: string) =>
    handleRequest<HsAllocation>(`/platforms/hs/allocations/${encodeURIComponent(id)}/approve`, {
      method: 'POST',
    }),

  getHsFacts: (params: { kind: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams({ kind: params.kind });
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return handleRequest<PaginatedResult<HsFact>>(`/platforms/hs/facts?${q.toString()}`);
  },

  getHsProposedJournals: (params?: { invoiceId?: string; kind?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.invoiceId) q.set('invoiceId', params.invoiceId);
    if (params?.kind) q.set('kind', params.kind);
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<any>(`/platforms/hs/proposed-journals${qs}`);
  },

  // ==========================================
  // Treasury & Banks (R3 Part B1)
  // ==========================================
  getFinanceAccounts: (sponsorId?: string) => {
    const qs = sponsorId ? `?sponsorId=${encodeURIComponent(sponsorId)}` : '';
    return handleRequest<FinanceAccount[]>(`/finance/accounts${qs}`);
  },

  getFinanceJournals: (params?: { sponsorId?: string; iqama?: string; accountCode?: string }) => {
    const q = new URLSearchParams();
    if (params?.sponsorId) q.set('sponsorId', params.sponsorId);
    if (params?.iqama) q.set('iqama', params.iqama);
    if (params?.accountCode) q.set('accountCode', params.accountCode);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<FinanceJournal[]>(`/finance/journals${qs}`);
  },

  previewBankFile: (input: { period: string; sponsorId?: string }) =>
    handleRequest<BankPayrollFilePreview>('/finance/bank-file/preview', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  exportBankFile: async (input: { period: string; sponsorId?: string }) => {
    if (isMock) {
      return new Blob(['Mock Bank CSV File'], { type: 'text/csv' });
    }
    const res = await fetch('/api/finance/bank-file/export', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'تعذر تصدير ملف البنك' }));
      throw new ApiError(err.error || 'تعذر تصدير ملف البنك', res.status);
    }
    return res.blob();
  },

  transferVault: (input: {
    sourceAccountCode: string;
    targetAccountCode: string;
    amount: number;
    description: string;
    sponsorId?: string;
  }) =>
    handleRequest<{ ok: boolean; journalId: string }>('/finance/vault-transfer', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  // ==========================================
  // Debts & Advances (R3 Part B2)
  // ==========================================
  getDebts: (params?: { sponsorId?: string; category?: string; status?: string; iqama?: string }) => {
    const q = new URLSearchParams();
    if (params?.sponsorId) q.set('sponsorId', params.sponsorId);
    if (params?.category) q.set('category', params.category);
    if (params?.status) q.set('status', params.status);
    if (params?.iqama) q.set('iqama', params.iqama);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<Array<Record<string, unknown>>>(`/debts${qs}`).then((rows) => rows.map(normalizeDebt));
  },

  decideOpeningDebts: (input: { ids: string[]; decision: 'approved' | 'rejected'; note?: string }) =>
    handleRequest<{ decision: string; processed: number; skipped: number; total: number }>('/debts/opening/decide', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  getRiderStatement: (iqama: string, from?: string, to?: string, sponsorId?: string) => {
    const q = new URLSearchParams();
    if (from) q.set('from', from);
    if (to) q.set('to', to);
    if (sponsorId) q.set('sponsorId', sponsorId);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<RiderStatement>(`/finance/statement/rider/${encodeURIComponent(iqama)}${qs}`);
  },

  getRiderStatementSimple: (iqama: string, from: string, to: string, sponsorId?: string) => {
    const q = new URLSearchParams({ from, to });
    if (sponsorId) q.set('sponsorId', sponsorId);
    return handleRequest<any>(`/finance/statement/rider/${encodeURIComponent(iqama)}/simple?${q.toString()}`);
  },

  getApprovals: (params?: { department?: string; status?: string }) => {
    const q = new URLSearchParams();
    if (params?.department) q.set('department', params.department);
    if (params?.status) q.set('status', params.status);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<ApprovalRequest[]>(`/approvals${qs}`);
  },

  decideApproval: (id: string, input: { decision: 'approved' | 'rejected' | 'suspended'; note?: string }) =>
    handleRequest<{ ok: boolean }>(`/approvals/${id}/decide`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  getPromissoryNotes: () =>
    handleRequest<{ featureEnabled: boolean; items: PromissoryNote[] }>('/promissory-notes'),

  // ==========================================
  // Accounting & Reports (R3 Part B3)
  // ==========================================
  getTargetCoa: () => handleRequest<TargetCoaResponse>('/finance/coa/target'),

  getTrialBalance: (params?: { sponsorId?: string; from?: string; to?: string; view?: string }) => {
    const q = new URLSearchParams();
    if (params?.sponsorId) q.set('sponsorId', params.sponsorId);
    if (params?.from) q.set('from', params.from);
    if (params?.to) q.set('to', params.to);
    if (params?.view) q.set('view', params.view);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<TrialBalanceResponse>(`/finance/trial-balance${qs}`);
  },

  getIncomeStatement: (params?: { sponsorId?: string; from?: string; to?: string; view?: string }) => {
    const q = new URLSearchParams();
    if (params?.sponsorId) q.set('sponsorId', params.sponsorId);
    if (params?.from) q.set('from', params.from);
    if (params?.to) q.set('to', params.to);
    if (params?.view) q.set('view', params.view);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<IncomeStatementResponse>(`/finance/income-statement${qs}`);
  },

  // Riders Directory APIs (Track L & Spec 09)
  getRiders: (params?: { search?: string; status?: string; sponsorId?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.search) q.set('search', params.search);
    if (params?.status) q.set('status', params.status);
    if (params?.sponsorId) q.set('sponsorId', params.sponsorId);
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<RiderRecord[]>(`/riders${qs}`);
  },

  getRiderProfile360: (iqama: string) =>
    handleRequest<RiderProfile360>(`/riders/${encodeURIComponent(iqama)}/profile-360`),

  getRidersDataQuality: () =>
    handleRequest<RidersDataQuality>('/riders/data-quality'),

  syncRidersBaseline: (payload: { mode: 'preview' | 'commit'; useLocalBaseline?: boolean; fingerprint?: string; riders?: any[] }) =>
    handleRequest<RidersBaselineSyncResult>('/riders/sync-baseline', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  enrichRidersMissing: () =>
    handleRequest<{ enrichedExisting: number; addedFromSources: number; totalRidersNow: number }>('/riders/enrich-missing', {
      method: 'POST',
    }),

  uploadRiderDocument: (
    iqama: string,
    input: {
      documentTypeCode: string;
      documentNumber?: string;
      issueDate?: string;
      expiryDate?: string;
      fileName?: string;
      dataBase64?: string;
      notes?: string;
    }
  ) =>
    handleRequest<any>(`/riders/${encodeURIComponent(iqama)}/documents`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  // Operations & Assets APIs (Codex R4.6)
  getOperations: (kind: string, params?: { page?: number; pageSize?: number; q?: string }) => {
    const p = new URLSearchParams();
    if (params?.page) p.set('page', String(params.page));
    if (params?.pageSize) p.set('pageSize', String(params.pageSize));
    if (params?.q) p.set('q', params.q);
    const qs = p.toString() ? `?${p.toString()}` : '';
    return handleRequest<any>(`/operations/${encodeURIComponent(kind)}${qs}`);
  },

  getRiderAccounts: (iqama: string, params?: { page?: number; pageSize?: number }) => {
    const p = new URLSearchParams();
    if (params?.page) p.set('page', String(params.page));
    if (params?.pageSize) p.set('pageSize', String(params.pageSize));
    const qs = p.toString() ? `?${p.toString()}` : '';
    return handleRequest<any>(`/riders/${encodeURIComponent(iqama)}/accounts${qs}`);
  },

  getRiderMonthlyStatement: (iqama: string, period: string) =>
    handleRequest<any>(`/finance/statement/rider/${encodeURIComponent(iqama)}/monthly?period=${encodeURIComponent(period)}`),

  // ==========================================
  // Pay Framework APIs (0.14.0 / R4.1 - R4.5)
  // ==========================================
  getPayCatalog: () => handleRequest<any>('/pay/catalog'),

  getPayTerms: (app: string, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/terms${q}`);
  },
  createPayTerms: (app: string, input: any, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/terms${q}`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  },
  approvePayTerms: (app: string, id: string) =>
    handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/terms/${encodeURIComponent(id)}/approve`, { method: 'POST' }),

  getPaySchemes: (app: string, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/schemes${q}`);
  },
  createPayScheme: (app: string, input: any, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/schemes${q}`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  },
  approvePayScheme: (app: string, id: string) =>
    handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/schemes/${encodeURIComponent(id)}/approve`, { method: 'POST' }),

  getPayEligibility: (app: string, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/eligibility${q}`);
  },
  createPayEligibility: (app: string, input: any, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/eligibility${q}`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  },
  approvePayEligibility: (app: string, id: string) =>
    handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/eligibility/${encodeURIComponent(id)}/approve`, { method: 'POST' }),

  copyPayMonth: (app: string, from: string, to: string) =>
    handleRequest<{ drafts: any[]; created: number; unchanged: number }>(
      `/pay/apps/${encodeURIComponent(app)}/copy?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      { method: 'POST' }
    ),

  getGlobalPaySettings: () => handleRequest<any>('/pay/settings'),
  createGlobalPaySettings: (input: any) =>
    handleRequest<any>('/pay/settings', { method: 'POST', body: JSON.stringify(input) }),
  approveGlobalPaySettings: (id: string) =>
    handleRequest<any>(`/pay/settings/${encodeURIComponent(id)}/approve`, { method: 'POST' }),

  getPayAssignments: (app: string, period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/assignments${q}`);
  },
  previewPayAssignments: (app: string, input: any) =>
    handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/assignments/import/preview`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  commitPayAssignments: (app: string, input: any) =>
    handleRequest<any>(`/pay/apps/${encodeURIComponent(app)}/assignments/import/commit`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  getPayCycles: (period?: string) => {
    const q = period ? `?period=${encodeURIComponent(period)}` : '';
    return handleRequest<PayCyclesResponse>(`/pay/cycles${q}`);
  },
  createPayCycle: (input: Record<string, unknown>) =>
    handleRequest<PayCycleItem>('/pay/cycles', { method: 'POST', body: JSON.stringify(input) }),
  advancePayCycle: (id: string, input: { stage?: number; action?: string }) =>
    handleRequest<PayCycleItem>(`/pay/cycles/${encodeURIComponent(id)}/advance`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  getPayCycleLines: (id: string, params?: { app?: string; rider?: string }) => {
    const q = new URLSearchParams();
    if (params?.app) q.set('app', params.app);
    if (params?.rider) q.set('rider', params.rider);
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<PayCycleLinesResponse>(`/pay/cycles/${encodeURIComponent(id)}/lines${qs}`);
  },

  // ==========================================
  // Legacy Production Payroll Run APIs (F2)
  // ==========================================
  getLegacyPayrollPreview: (period: string) =>
    handleRequest<any[]>(`/payroll/preview?period=${encodeURIComponent(period)}`),
  getLegacyPayrollRuns: () =>
    handleRequest<any[]>('/payroll/runs'),
  approveLegacyPayrollRun: (input: { iqama: string; period: string; sponsorId: string; fingerprint: string }) =>
    handleRequest<any>('/payroll/approve', { method: 'POST', body: JSON.stringify(input) }),
  createAdjustment: (input: { iqama: string; period: string; amount: number; sourceRef: string; category: string; description: string }) =>
    handleRequest<any>('/adjustments', { method: 'POST', body: JSON.stringify(input) }),
  createPayment: (input: { iqama: string; period: string; amount: number; sourceRef: string; sponsorId?: string; paymentVaultCode?: string; onBehalf?: boolean; onBehalfNote?: string }) => {
    const payload: any = { ...input };
    if (!payload.paymentVaultCode) delete payload.paymentVaultCode;
    if (!payload.sponsorId) delete payload.sponsorId;
    return handleRequest<any>('/payments', { method: 'POST', body: JSON.stringify(payload) });
  },
  getLedger: (iqama: string) =>
    handleRequest<{ balance: number; rows: Array<{ period: string; kind: string; amount: number; sourceRef: string }> }>(`/ledger/${encodeURIComponent(iqama)}`),
  getFinanceCatalog: () =>
    handleRequest<{ sponsors: any[]; vaults: any[] }>('/finance/catalog'),

  // Admin & Security APIs
  getUsers: () => handleRequest<any[]>('/users'),
  createUser: (input: any) =>
    handleRequest<any>('/users', { method: 'POST', body: JSON.stringify(input) }),
  updateUser: (id: string, input: any) =>
    handleRequest<any>(`/users/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(input) }),
  getRolesCatalog: () => handleRequest<any>('/roles-catalog'),
  getFirebaseConfig: () => handleRequest<any>('/rider-mobile/firebase-config'),
  saveFirebaseConfig: (input: any) =>
    handleRequest<any>('/rider-mobile/firebase-config', { method: 'POST', body: JSON.stringify(input) }),

  // Operational Data Imports APIs (/api/imports/*)
  inspectImport: (data: { filename: string; dataBase64: string }) =>
    handleRequest<{ id: string; filename: string; sheets: string[] }>('/imports/inspect', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  getImportSheet: (id: string, sheet: string, headerRow?: number) => {
    const q = new URLSearchParams({ sheet });
    if (headerRow) q.set('headerRow', String(headerRow));
    return handleRequest<{ sheet: string; headers: string[]; rowsCount: number; sampleRows: any[] }>(
      `/imports/${encodeURIComponent(id)}/sheet?${q.toString()}`
    );
  },
  previewImport: (id: string, spec: any) =>
    handleRequest<any>(`/imports/${encodeURIComponent(id)}/preview`, {
      method: 'POST',
      body: JSON.stringify(spec),
    }),
  commitImport: (id: string, spec: any) =>
    handleRequest<any>(`/imports/${encodeURIComponent(id)}/commit`, {
      method: 'POST',
      body: JSON.stringify(spec),
    }),
  getImportBatches: () => handleRequest<any[]>('/imports'),

  // Legacy Policies
  getPolicies: () => handleRequest<any[]>('/policies'),

  // Rider Mobile App APIs (Track M)
  getRiderBroadcasts: () => handleRequest<any[]>('/rider-mobile/broadcasts'),
  getRiderSessions: (f: { status?: string; iqama?: string } = {}) => {
    const q = new URLSearchParams();
    if (f.status) q.set('status', f.status);
    if (f.iqama) q.set('iqama', f.iqama);
    const qs = q.toString();
    return handleRequest<import('../components/riders/RiderSessionsPanel').RiderSession[] | { items: import('../components/riders/RiderSessionsPanel').RiderSession[] }>(
      '/rider-mobile/sessions' + (qs ? '?' + qs : '')
    );
  },
  revokeRiderSession: (id: string, reason: string) =>
    handleRequest<unknown>(`/rider-mobile/sessions/${encodeURIComponent(id)}/revoke`, { method: 'POST', body: JSON.stringify({ reason }) }),
  revokeAllRiderSessions: (iqama: string, reason: string) =>
    handleRequest<{ revoked: number }>(`/rider-mobile/sessions/revoke-all?iqama=${encodeURIComponent(iqama)}`, { method: 'POST', body: JSON.stringify({ reason }) }),
  sendRiderBroadcast: (input: { category?: string; titleAr: string; bodyAr: string; iqama?: string; actionUrl?: string; requiresAck?: boolean }) =>
    handleRequest<any>('/rider-mobile/broadcast', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  // ========================================================
  // R5 ADVANCES & ORG DIRECTORY APIS (BR §25)
  // ========================================================

  // Advance Requests
  createAdvanceRequest: (body: {
    riderIqama: string;
    type: 'cash' | 'bank' | 'internet';
    amount: number;
    reason: string;
    vaultCode?: string;
    fulfilment?: 'cash' | 'bank' | 'recharge_card';
    networkCode?: string;
    packageCode?: string;
  }) =>
    handleRequest<AdvanceRequest>('/advance-requests', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  getAdvanceRequests: (params?: { role?: 'mine' | 'review' | 'payout'; status?: string }) => {
    const q = new URLSearchParams();
    if (params?.role) q.set('role', params.role);
    if (params?.status) q.set('status', params.status);
    const qs = q.toString();
    return handleRequest<AdvanceRequest[]>(`/advance-requests${qs ? `?${qs}` : ''}`);
  },
  getAdvanceRequest: (id: string) => handleRequest<AdvanceRequest>(`/advance-requests/${encodeURIComponent(id)}`),
  getRiderAsks: (status?: string) =>
    handleRequest<import('../components/debts/RiderAsksCard').RiderAsk[]>('/advance-requests/rider-asks' + (status ? `?status=${encodeURIComponent(status)}` : '')),
  convertRiderAsk: (id: string, body: { type?: 'cash' | 'bank'; amount?: number; reason?: string }) =>
    handleRequest<unknown>(`/advance-requests/rider-asks/${encodeURIComponent(id)}/convert`, { method: 'POST', body: JSON.stringify(body) }),
  declineRiderAsk: (id: string, note: string) =>
    handleRequest<unknown>(`/advance-requests/rider-asks/${encodeURIComponent(id)}/decline`, { method: 'POST', body: JSON.stringify({ note }) }),
  getAdvanceRequestSummary: (id: string) => handleRequest<AdvanceRequestSummary>(`/advance-requests/${encodeURIComponent(id)}/summary`),
  reviewAdvanceRequest: (
    id: string,
    body: {
      action: 'approve' | 'reject' | 'propose';
      note?: string;
      monthlyInstallment?: number;
      coverageReason?: string;
      proposed?: { type?: 'cash' | 'bank'; amount?: number; vaultCode?: string };
      revision?: number;
    }
  ) =>
    handleRequest<AdvanceRequest>(`/advance-requests/${encodeURIComponent(id)}/review`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  respondToAdvanceProposal: (id: string, body: { action: 'accept' | 'reject'; note?: string }) =>
    handleRequest<AdvanceRequest>(`/advance-requests/${encodeURIComponent(id)}/respond`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  payoutAdvanceRequest: (id: string, body: { reference: string; note?: string }) =>
    handleRequest<AdvanceRequest>(`/advance-requests/${encodeURIComponent(id)}/payout`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Org Directory (Departments, Ranks, Delegations, Supervision, Routing)
  getOrgCatalog: () => handleRequest<OrgCatalog>('/org/catalog'),
  getOrgMemberships: () => handleRequest<OrgMembership[]>('/org/memberships'),
  createOrgMembership: (body: { userId: string; department: string; rank: string; validFrom?: string; validTo?: string }) =>
    handleRequest<OrgMembership>('/org/memberships', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  endOrgMembership: (id: string, body?: { validTo?: string }) =>
    handleRequest<OrgMembership>(`/org/memberships/${encodeURIComponent(id)}/end`, {
      method: 'POST',
      body: JSON.stringify(body || {}),
    }),
  getOrgDelegations: () => handleRequest<OrgDelegation[]>('/org/delegations'),
  createOrgDelegation: (body: { fromUser: string; toUser: string; operation: string; validFrom?: string; until: string; reason?: string }) =>
    handleRequest<OrgDelegation>('/org/delegations', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  endOrgDelegation: (id: string, body?: { validTo?: string }) =>
    handleRequest<OrgDelegation>(`/org/delegations/${encodeURIComponent(id)}/end`, {
      method: 'POST',
      body: JSON.stringify(body || {}),
    }),
  getOrgSupervision: (params?: { mine?: number }) => {
    const q = new URLSearchParams();
    if (params?.mine !== undefined) q.set('mine', String(params.mine));
    const qs = q.toString();
    return handleRequest<OrgSupervision[]>(`/org/supervision${qs ? `?${qs}` : ''}`);
  },
  createOrgSupervision: (body: { supervisorUserId: string; riderIqama: string; validFrom?: string; validTo?: string }) =>
    handleRequest<OrgSupervision>('/org/supervision', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  endOrgSupervision: (id: string, body?: { validTo?: string }) =>
    handleRequest<OrgSupervision>(`/org/supervision/${encodeURIComponent(id)}/end`, {
      method: 'POST',
      body: JSON.stringify(body || {}),
    }),
  getOrgRouting: () => handleRequest<OrgRouting[]>('/org/routing'),
  createOrgRouting: (body: { operation: string; department: string; minRank: string; users: string[]; validFrom?: string; validTo?: string }) =>
    handleRequest<OrgRouting>('/org/routing', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  endOrgRouting: (id: string, body?: { validTo?: string }) =>
    handleRequest<OrgRouting>(`/org/routing/${encodeURIComponent(id)}/end`, {
      method: 'POST',
      body: JSON.stringify(body || {}),
    }),
  getOrgResponsible: (operation: string) => {
    const q = new URLSearchParams({ operation });
    return handleRequest<OrgResponsibleUser[]>(`/org/responsible?${q.toString()}`);
  },
  getHealth: () => handleRequest<{ ok: boolean; jobs?: { enabled: boolean; lastRun?: string; nextRun?: string } }>('/health'),
  runErpSync: (dryRun: boolean) =>
    handleRequest<{ jobId: string }>(dryRun ? '/system/jobs/erp-sync/dry-run' : '/system/jobs/erp-sync/run', {
      method: 'POST',
    }),
  getErpSyncJobStatus: (jobId: string) =>
    handleRequest<ErpSyncJobStatus>(`/system/jobs/erp-sync/${encodeURIComponent(jobId)}`),
  getRecentErpSyncRuns: (limit: number = 5) =>
    handleRequest<ErpSyncRun[]>(`/system/erp-sync?limit=${limit}`),
  getPayCycleReview: (cycleId: string, params?: { review?: string; status?: string }) => {
    const q = new URLSearchParams();
    if (params?.review) q.set('review', params.review);
    if (params?.status) q.set('status', params.status);
    const qs = q.toString();
    return handleRequest<PayReviewResponse>(`/pay/cycles/${encodeURIComponent(cycleId)}/review${qs ? `?${qs}` : ''}`);
  },
  reviewPayCycleRider: (cycleId: string, iqama: string, body: { action: 'approve' } | { action: 'edit'; key: string; amount: number; reason: string } | { action: 'adjust'; amount: number; reason: string }) =>
    handleRequest<PayReviewRow>(`/pay/cycles/${encodeURIComponent(cycleId)}/review/${encodeURIComponent(iqama)}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  approveAllPayCycleReview: (cycleId: string) =>
    handleRequest<{ approved: number; skipped: number }>(`/pay/cycles/${encodeURIComponent(cycleId)}/review/approve-all`, {
      method: 'POST',
    }),
  getConfigLists: () => handleRequest<ConfigListGroup[]>('/config/lists'),
  getConfigListGroup: (group: string) => handleRequest<ConfigListGroup>(`/config/lists/${encodeURIComponent(group)}`),
  createConfigListItem: (group: string, body: { code: string; nameAr: string; values?: Record<string, any>; active?: boolean }) =>
    handleRequest<{ ok: boolean }>(`/config/lists/${encodeURIComponent(group)}/items`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  approveConfigListItem: (group: string, code: string) =>
    handleRequest<{ ok: boolean }>(`/config/lists/${encodeURIComponent(group)}/items/${encodeURIComponent(code)}/approve`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  addConfigListField: (group: string, body: ConfigFieldDef) =>
    handleRequest<{ ok: boolean }>(`/config/lists/${encodeURIComponent(group)}/fields`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Manual Journals (BR §28 / C6)
  getManualJournals: (params?: { status?: string; page?: number; limit?: number }) => {
    const q = new URLSearchParams();
    if (params?.status) q.set('status', params.status);
    if (params?.page) q.set('page', String(params.page));
    if (params?.limit) q.set('limit', String(params.limit));
    const qs = q.toString();
    return handleRequest<paths['/api/finance/journals/manual']['get']['responses']['200']['content']['application/json']>(
      `/finance/journals/manual${qs ? `?${qs}` : ''}`
    );
  },
  createManualJournal: (body: {
    companyId: string;
    date: string;
    reference: string;
    description: string;
    lines: Array<{ accountCode: string; debit: number; credit: number; note?: string }>;
    attachment?: { fileName: string; dataBase64: string };
    odooVoucherNo?: string;
  }) =>
    handleRequest<ManualJournalItem>('/finance/journals/manual', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  approveManualJournal: (id: string) =>
    handleRequest<paths['/api/finance/journals/manual/{id}/approve']['post']['responses']['200']['content']['application/json']>(
      `/finance/journals/manual/${encodeURIComponent(id)}/approve`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  rejectManualJournal: (id: string, body: { reason: string }) =>
    handleRequest<paths['/api/finance/journals/manual/{id}/reject']['post']['responses']['200']['content']['application/json']>(
      `/finance/journals/manual/${encodeURIComponent(id)}/reject`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deleteManualJournal: (id: string) =>
    handleRequest<paths['/api/finance/journals/manual/{id}']['delete']['responses']['200']['content']['application/json']>(
      `/finance/journals/manual/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    }),
  reverseManualJournal: (id: string, body: { reason: string; date: string }) =>
    handleRequest<paths['/api/finance/journals/manual/{id}/reverse']['post']['responses']['200']['content']['application/json']>(
      `/finance/journals/manual/${encodeURIComponent(id)}/reverse`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Ninja Invoices Reconciliation (BR §26, §31 / N2/N3)
  reconcileNinjaInvoices: (body: { period: string; files: Array<{ fileName: string; dataBase64: string }> }) =>
    handleRequest<NinjaReconcileResponse>('/platforms/ninja/invoices/reconcile', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Ninja Invoices & Operations Lifecycle (BR §26, §31, §32 / C1-C5)
  getNinjaInvoices: (params?: { page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params?.page) q.set('page', String(params.page));
    if (params?.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString() ? `?${q.toString()}` : '';
    return handleRequest<NinjaInvoicesResponse>(`/platforms/ninja/invoices${qs}`);
  },
  getNinjaInvoice: (id: string) => handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}`),
  uploadNinjaInvoice: (formData: FormData, commit = false): Promise<NinjaInvoice> => {
    const qs = commit ? '?commit=1' : '';
    const url = `/api/platforms/ninja/invoices${qs}`;
    return fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      body: formData,
    }).then(async (res) => {
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        const errMsg = typeof data.error === 'string' ? data.error : (data?.error?.message || data?.message || 'فشل رفع فاتورة نينجا');
        const errCode = typeof data.code === 'string' ? data.code : (data?.error?.code || 'error');
        throw new ApiError(errMsg, res.status, errCode);
      }
      return res.json() as Promise<NinjaInvoice>;
    });
  },
  approveNinjaInvoice: (id: string) =>
    handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}/approve`, { method: 'POST' }),
  postNinjaInvoice: (id: string) =>
    handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}/post`, { method: 'POST' }),
  classifyNinjaInvoice: (id: string, body: Record<string, unknown>) =>
    handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}/classify`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  disputeNinjaInvoice: (id: string, body: { reason: string }) =>
    handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}/dispute`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  resolveNinjaDispute: (id: string, body: { disputeId: string; reason: string }) =>
    handleRequest<NinjaInvoice>(`/platforms/ninja/invoices/${encodeURIComponent(id)}/resolve`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Pay Review Draft (C3 / A5)
  getPayReviewDraft: (params: { app: string; period: string; companyId: string }) => {
    const q = new URLSearchParams({
      app: params.app,
      period: params.period,
      companyId: params.companyId,
    });
    return handleRequest<PayReviewDraft>(`/pay/review-draft?${q.toString()}`);
  },

  // Riders Lookup (C6 / A7)
  getRidersLookup: (query: string, limit = 20) =>
    handleRequest<RiderLookupItem[]>(`/riders/lookup?q=${encodeURIComponent(query)}&limit=${encodeURIComponent(String(limit))}`),

  // Pay Settings Lifecycle (C4 / A6)
  getPayAppDefaults: (app: string) => handleRequest<PayCatalog['defaults']>(`/pay/apps/${encodeURIComponent(app)}/defaults`),
  getAppEligibility: (app: string) => handleRequest<PayEligibilityResponse>(`/pay/apps/${encodeURIComponent(app)}/eligibility`),
  createAppEligibility: (app: string, input: Record<string, unknown>, period?: string) =>
    handleRequest<PayEligibilityItem>(`/pay/apps/${encodeURIComponent(app)}/eligibility${period ? `?period=${encodeURIComponent(period)}` : ''}`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  approveAppEligibility: (app: string, id: string) =>
    handleRequest<PayEligibilityItem>(`/pay/apps/${encodeURIComponent(app)}/eligibility/${encodeURIComponent(id)}/approve`, { method: 'POST' }),
  rejectPaySetting: (kind: string, app: string, id: string, reason: string) => {
    const url = kind === 'pay_setting'
      ? `/pay/settings/${encodeURIComponent(id)}/reject`
      : `/pay/apps/${encodeURIComponent(app)}/${kind.replace('pay_', '')}/${encodeURIComponent(id)}/reject`;
    return handleRequest<PayEligibilityItem>(url, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  deletePaySetting: (kind: string, app: string, id: string) => {
    const url = kind === 'pay_setting'
      ? `/pay/settings/${encodeURIComponent(id)}`
      : `/pay/apps/${encodeURIComponent(app)}/${kind.replace('pay_', '')}/${encodeURIComponent(id)}`;
    return handleRequest<{ id: string; deleted?: boolean }>(url, { method: 'DELETE' });
  },

  // Import Center (K6 / R6c)
  getImportsCatalog: () => handleRequest<ImportCatalogStep[]>('/imports/catalog'),
  downloadImportTemplate: async (templateUrl: string, fallbackFileName = 'template.xlsx'): Promise<void> => {
    const url = templateUrl.startsWith('http') ? templateUrl : (templateUrl.startsWith('/api') ? templateUrl : `/api${templateUrl}`);
    const res = await fetch(url, {
      method: 'GET',
      credentials: 'same-origin',
    });
    if (res.status === 401) {
      throw new ApiError('انتهت الجلسة أو غير مصرح لك بتحميل النموذج (401).', 401, 'unauthorized');
    }
    if (res.status === 403) {
      throw new ApiError('ليس لديك صلاحية تحميل هذا النموذج (403).', 403, 'forbidden');
    }
    if (!res.ok) {
      const payload: ApiErrorPayload = await res.json().catch(() => ({ error: 'فشل تحميل النموذج' }));
      throw new ApiError(payload.error || 'تعذر تحميل النموذج من الخادم.', res.status, payload.code);
    }

    let filename = fallbackFileName;
    const disposition = res.headers.get('content-disposition');
    if (disposition) {
      const match = /filename\*?=(?:UTF-8'')?["']?([^"';\n]+)["']?/i.exec(disposition);
      if (match && match[1]) {
        try {
          filename = decodeURIComponent(match[1].trim());
        } catch {
          filename = match[1].trim();
        }
      }
    }

    const blob = await res.blob();
    const blobUrl = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.style.display = 'none';
    a.href = blobUrl;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(blobUrl);
    document.body.removeChild(a);
  },
  postImportPreview: (previewUrl: string, payload: any) => {
    const endpoint = previewUrl.startsWith('/api') ? previewUrl.substring(4) : previewUrl;
    return handleRequest<ImportPreviewResult>(endpoint, {
      method: 'POST',
      body: JSON.stringify(payload),
    });
  },
  postImportCommit: (commitUrl: string, payload: any) => {
    const endpoint = commitUrl.startsWith('/api') ? commitUrl.substring(4) : commitUrl;
    return handleRequest<any>(endpoint, {
      method: 'POST',
      body: JSON.stringify(payload),
    });
  },
};
