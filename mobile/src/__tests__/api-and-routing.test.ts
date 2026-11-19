// @vitest-environment happy-dom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { routeNotificationLink, extractNotificationPeriod } from '../push';
import { api, esc, money } from '../api';
import { setSession, getSession } from '../state';
import { setLanguage, getLanguage, t, validateSaudiIban } from '../i18n';
import { renderStatementHtml } from '../screens/statement';
import { renderDebtsHtml } from '../screens/debts';
import { renderNotificationsHtml } from '../screens/notifications';
import { renderRequestsHtml } from '../screens/requests';

describe('Notification Link Routing & Deep Links', () => {
  it('routes requests, ask, and in-app form links to requests tab', () => {
    expect(routeNotificationLink('/rider-app/requests')).toBe('requests');
    expect(routeNotificationLink('ask:12345')).toBe('requests');
    expect(routeNotificationLink('/advance-requests')).toBe('requests');
    expect(routeNotificationLink('/forms/some-campaign')).toBe('requests');
    expect(routeNotificationLink('/questionnaires/some-form')).toBe('requests');
  });

  it('routes statement and payslip links to statement tab and extracts period', () => {
    expect(routeNotificationLink('/rider-app/statement?period=2026-10')).toBe('statement');
    expect(routeNotificationLink('/payslip')).toBe('statement');
    expect(extractNotificationPeriod('/rider-app/statement?period=2026-10')).toBe('2026-10');
    expect(extractNotificationPeriod('/rider-app/requests')).toBeUndefined();
  });

  it('routes debt links to debts tab', () => {
    expect(routeNotificationLink('/rider-app/debts')).toBe('debts');
    expect(routeNotificationLink('debt:fine')).toBe('debts');
  });

  it('routes session links to devices tab', () => {
    expect(routeNotificationLink('/rider-app/devices')).toBe('devices');
    expect(routeNotificationLink('/sessions/mine')).toBe('devices');
  });

  it('defaults to notifications tab for general broadcast links without target', () => {
    expect(routeNotificationLink('')).toBe('notifications');
    expect(routeNotificationLink(undefined)).toBe('notifications');
  });
});

describe('3-Language Switcher (ar / en / ur) and RTL/LTR Direction', () => {
  it('switches language and updates document dir between RTL (ar, ur) and LTR (en)', () => {
    setLanguage('ar');
    expect(getLanguage()).toBe('ar');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(t('forYou')).toBe('لك');
    expect(t('onYou')).toBe('عليك');

    setLanguage('en');
    expect(getLanguage()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(t('forYou')).toContain('For you');
    expect(t('onYou')).toContain('On you');

    setLanguage('ur');
    expect(getLanguage()).toBe('ur');
    expect(document.documentElement.lang).toBe('ur');
    expect(document.documentElement.dir).toBe('rtl');
    expect(t('forYou')).toContain('آپ کے حق میں');
    expect(t('onYou')).toContain('آپ کے ذمے');

    setLanguage('ar');
  });
});

describe('Shared Saudi IBAN Validation (RiderForm parity)', () => {
  it('validates Mod-97 Saudi IBAN, normalizes spaces/Arabic digits, and rejects retired banks', () => {
    const validRajhi = validateSaudiIban('IBAN: sa03 8000 0000 6080 1016 7519');
    expect(validRajhi.valid).toBe(true);
    expect(validRajhi.bankCode).toBe('80');
    expect(validRajhi.bank?.swift).toBe('RJHI');

    const badChecksum = validateSaudiIban('SA0080000000608010167519');
    expect(badChecksum.valid).toBe(false);
    expect(badChecksum.errorKey).toBe('invalidIban');

    // Generate synthetic retired bank 40 IBAN with valid mod97
    let r = 0;
    const bodyWithoutCheck = '40000000000000000004';
    for (const c of bodyWithoutCheck + '281000') r = (r * 10 + Number(c)) % 97;
    const retiredIban = 'SA' + String(98 - r).padStart(2, '0') + bodyWithoutCheck;
    const retired = validateSaudiIban(retiredIban);
    expect(retired.valid).toBe(false);
    expect(retired.errorKey).toBe('bankRetired');
  });
});

describe('Statement, Debts, Notifications, and Requests Screens', () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
    setLanguage('ar');
    await setSession({
      accessToken: 'rmob_test_token',
      iqama: '2000000999',
      name: 'مندوب تجريبي'
    });
  });

  it('renders short 3-language statement with لك / عليك markers, home summary, and no company names', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        status: 'approved',
        approvedOnly: true,
        net: 2450,
        carried: -150,
        closing: 2300,
        cards: [{ app: 'keeta', platform: 'Keeta', gross: 3000, companyCommission: 350, vehicleCharge: 200 }],
        deductions: [{ category: 'debt_installment', amount: -200, labelAr: 'قسط مديونية' }],
        shortLines: [
          {
            key: 'carried',
            labelAr: 'رصيد مرحّل من شهر سابق',
            labelEn: 'Carried balance from previous month',
            labelUr: 'پچھلے مہینے کا منتقل شدہ بیلنس',
            amount: -150,
            absAmount: 150,
            runningBalance: -150
          },
          {
            key: 'gross',
            labelAr: 'مستحقات التوصيل — كيتا',
            labelEn: 'Delivery earnings — Keeta',
            labelUr: 'ڈیلیوری کی آمدنی — کیٹا',
            amount: 3000,
            absAmount: 3000,
            runningBalance: 2850
          },
          {
            key: 'ded',
            labelAr: 'قسط مديونية',
            labelEn: 'Debt instalment',
            labelUr: 'قرض کی قسط',
            amount: -550,
            absAmount: 550,
            runningBalance: 2300
          }
        ]
      })
    } as any);

    const latestSummary = { available: true, period: '2026-10', closing: 2300 };
    const htmlAr = await renderStatementHtml('2026-10', latestSummary);
    expect(htmlAr).toContain('id="mob-home-summary"');
    expect(htmlAr).toContain('id="mob-statement-lines"');
    expect(htmlAr).toContain('data-marker="for_you"');
    expect(htmlAr).toContain('data-marker="on_you"');
    expect(htmlAr).toContain('لك');
    expect(htmlAr).toContain('عليك');
    expect(htmlAr).toContain('لا يُعد مستنداً رسمياً أو قانونياً');
    expect(htmlAr).not.toContain('شركة البوابة');

    setLanguage('ur');
    const htmlUr = await renderStatementHtml('2026-10', latestSummary);
    expect(htmlUr).toContain('ڈیلیوری کی آمدنی — کیٹا');
    expect(htmlUr).toContain('آپ کے حق میں (لك)');
    expect(htmlUr).toContain('آپ کے ذمے (عليك)');
    setLanguage('ar');
  });

  it('renders debts summary without company or sponsor names', () => {
    const html = renderDebtsHtml({
      totalRemainingDebt: 800,
      totalPaidDebt: 200,
      totalWaivedDebt: 0,
      debts: [
        {
          id: 'd1',
          category: 'WorkPermit',
          categoryAr: 'رخصة عمل',
          categoryEn: 'Work Permit',
          categoryUr: 'ورک پرمٹ',
          totalAmount: 1000,
          paidAmount: 200,
          waivedAmount: 0,
          remainingBalance: 800,
          monthlyInstallment: 200,
          status: 'active',
          effectiveFrom: '2026-10'
        }
      ]
    });
    expect(html).toContain('800.00');
    expect(html).toContain('200.00');
    expect(html).toContain('عليك');
    expect(html).not.toContain('شركة البوابة');
    expect(html).not.toContain('السجل الدائن');
  });

  it('renders general broadcasts and individual-only notices with acknowledgement and deep links', () => {
    const html = renderNotificationsHtml([
      {
        id: 'n1',
        kind: 'individual_notice',
        category: 'summons',
        titleAr: 'استدعاء لمراجعة الإدارة',
        bodyAr: 'يرجى الحضور غداً الساعة 10 صباحاً',
        requiresAck: true,
        acknowledged: false,
        actionUrl: '/rider-app/requests',
        createdAt: '2026-11-19T06:00:00Z'
      },
      {
        id: 'n2',
        kind: 'broadcast',
        category: 'operational',
        titleAr: 'تعميم تشغيلي عام',
        bodyAr: 'تحديث مواعيد العمل',
        requiresAck: false,
        read: false,
        createdAt: '2026-11-19T06:00:00Z'
      }
    ]);
    expect(html).toContain('data-notice-kind="individual_notice"');
    expect(html).toContain('data-ack="n1"');
    expect(html).toContain('data-deep-link="/rider-app/requests"');
    expect(html).toContain('data-read="n2"');
  });

  it('renders all 7 request types (including annual_leave), status timeline, and two-way replies', async () => {
    global.fetch = vi.fn().mockImplementation(async (url: string) => {
      if (String(url).includes('/api/rider-mobile/forms')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ schemas: [], activeCampaigns: [] })
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => [
          {
            id: 'sreq:leave1',
            requestType: 'annual_leave',
            direction: 'rider_to_admin',
            titleAr: 'طلب إجازة سنوية',
            reason: 'سفر عائلي',
            status: 'need_info',
            note: 'أرفق تذكرة السفر',
            createdAt: '2026-11-19T05:00:00Z',
            timeline: [
              { at: '2026-11-19T05:00:00Z', status: 'open', labelAr: 'تم إرسال طلب الإجازة السنوية' },
              { at: '2026-11-19T05:30:00Z', status: 'need_info', labelAr: 'طلبت الإدارة توضيحاً', note: 'أرفق تذكرة السفر' }
            ],
            replies: [{ id: 'r1', senderRole: 'admin', message: 'متى موعد العودة المؤكد؟', createdAt: '2026-11-19T05:30:00Z' }]
          }
        ]
      };
    });

    const htmlLeave = await renderRequestsHtml('annual_leave');
    expect(htmlLeave).toContain('id="mob-service-request-form"');
    expect(htmlLeave).toContain('name="leaveType"');
    expect(htmlLeave).toContain('name="startDate"');
    expect(htmlLeave).toContain('name="endDate"');
    expect(htmlLeave).toContain('تم إرسال طلب الإجازة السنوية');
    expect(htmlLeave).toContain('متى موعد العودة المؤكد؟');
    expect(htmlLeave).toContain('data-reply-req="sreq:leave1"');
  });
});

describe('API Client and Error Handling', () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
    await setSession({
      accessToken: 'rmob_test_token',
      iqama: '2000000999',
      name: 'مندوب تجريبي'
    });
  });

  it('clears session and fires unauthorized event on HTTP 401', async () => {
    let firedEvent = false;
    window.addEventListener(
      'gate-auth-unauthorized',
      () => {
        firedEvent = true;
      },
      { once: true }
    );

    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({ error: 'جلسة المندوب منتهية أو الحساب موقوف.' })
    } as any);

    await expect(api('/api/rider-mobile/statement')).rejects.toThrow('جلسة المندوب منتهية أو الحساب موقوف.');
    expect(getSession()).toBeNull();
    expect(firedEvent).toBe(true);
  });

  it('maps server error message correctly on HTTP failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ error: 'الطلب مكرر أو غير صالح' })
    } as any);

    await expect(api('/api/test')).rejects.toThrow('الطلب مكرر أو غير صالح');
  });

  it('maps network failure to clear Arabic message', async () => {
    global.fetch = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(api('/api/test')).rejects.toThrow('تعذر الاتصال بالخادم؛ يرجى التحقق من اتصال الإنترنت والمحاولة مجدداً.');
  });

  it('correctly escapes HTML characters in esc utility', () => {
    expect(esc('<script>alert("xss")&\'</script>')).toBe('&lt;script&gt;alert(&quot;xss&quot;)&amp;&#39;&lt;/script&gt;');
  });

  it('formats currency correctly with money utility', () => {
    expect(money(1250.5)).toBe('1,250.50');
    expect(money(0)).toBe('0.00');
  });
});
