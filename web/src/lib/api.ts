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
} from './types';

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
  getNotificationsSummary: () => handleRequest<NotificationSummary>('/notifications/summary'),
  getNotifications: (params?: { category?: string; unread?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.category) qs.set('category', params.category);
    if (params?.unread) qs.set('unread', 'true');
    return handleRequest<NotificationItem[]>(`/notifications?${qs.toString()}`);
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
    return handleRequest<DebtRecord[]>(`/debts${qs}`);
  },

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
  getOperations: (kind: string) =>
    handleRequest<any[]>(`/operations/${encodeURIComponent(kind)}`),

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
};
