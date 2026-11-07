import { API_BASE } from './config';
import { getSession, setSession } from './state';

export async function api<T = any>(path: string, body?: any): Promise<T> {
  const session = getSession();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json'
  };
  if (session?.accessToken) {
    headers['Authorization'] = 'Bearer ' + session.accessToken;
  }
  const url = path.startsWith('http') ? path : `${API_BASE}${path}`;

  let res: Response;
  try {
    res = await fetch(url, {
      method: body ? 'POST' : 'GET',
      headers,
      body: body ? JSON.stringify(body) : undefined
    });
  } catch {
    throw new Error('تعذر الاتصال بالخادم؛ يرجى التحقق من اتصال الإنترنت والمحاولة مجدداً.');
  }

  const data = await res.json().catch(() => ({}));
  if (res.status === 401) {
    await setSession(null);
    window.dispatchEvent(
      new CustomEvent('gate-auth-unauthorized', {
        detail: { message: data.error || 'انتهت صلاحية الجلسة.' }
      })
    );
    throw new Error(data.error || 'جلسة المندوب منتهية، يرجى إعادة تسجيل الدخول');
  }

  if (!res.ok) {
    throw new Error(data.error || 'تعذر إتمام الطلب');
  }
  return data as T;
}

export function esc(s: any): string {
  return String(s ?? '').replace(
    /[&<>"']/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] || c)
  );
}

export function money(n: any): string {
  return Number(n || 0).toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  });
}
