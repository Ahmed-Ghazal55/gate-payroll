import { describe, it, expect, beforeEach, vi } from 'vitest';
import { routeNotificationLink } from '../push';
import { api, esc, money } from '../api';
import { setSession, getSession } from '../state';

describe('Notification Link Routing', () => {
  it('routes requests and ask links to requests tab', () => {
    expect(routeNotificationLink('/rider-app/requests')).toBe('requests');
    expect(routeNotificationLink('ask:12345')).toBe('requests');
    expect(routeNotificationLink('/advance-requests')).toBe('requests');
  });

  it('routes statement and payslip links to statement tab', () => {
    expect(routeNotificationLink('/rider-app/statement?period=2026-10')).toBe('statement');
    expect(routeNotificationLink('/payslip')).toBe('statement');
  });

  it('routes debt links to debts tab', () => {
    expect(routeNotificationLink('/rider-app/debts')).toBe('debts');
    expect(routeNotificationLink('debt:fine')).toBe('debts');
  });

  it('routes session links to devices tab', () => {
    expect(routeNotificationLink('/rider-app/devices')).toBe('devices');
    expect(routeNotificationLink('/sessions/mine')).toBe('devices');
  });

  it('defaults to notifications tab for general broadcast links', () => {
    expect(routeNotificationLink('')).toBe('notifications');
    expect(routeNotificationLink(undefined)).toBe('notifications');
    expect(routeNotificationLink('/forms/some-campaign')).toBe('notifications');
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
