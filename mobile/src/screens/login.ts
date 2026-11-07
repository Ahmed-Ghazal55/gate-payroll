import { api, esc } from '../api';
import { setSession, getActiveFcmToken } from '../state';
import { initPushNotifications } from '../push';

export function renderLogin(root: HTMLElement, onSuccess: () => void, err: string = ''): void {
  root.innerHTML = `
    <div class="mob-head">
      <div>
        <strong>بوابة المندوب</strong>
        <div class="muted small">كشوف الحساب · المديونيات · الإشعارات والنماذج</div>
      </div>
      <span class="tag good">متصل</span>
    </div>
    <div class="mob-card">
      <h2 style="margin-top:0">تسجيل دخول المندوب</h2>
      <p class="muted small">
        أدخل الإقامة وآخر أربعة أرقام من جوالك ورمز الدخول الخاص الذي تسلمه لك الإدارة بعد مراجعة الهوية.
      </p>
      ${err ? `<div class="notice error">${esc(err)}</div>` : ''}
      <form id="mob-login-form" class="form-grid">
        <label>
          رقم الهوية / الإقامة (10 أرقام)
          <input name="iqamaNo" required pattern="[12][0-9]{9}" inputmode="numeric" placeholder="2XXXXXXXXX">
        </label>
        <label>
          آخر 4 أرقام من الجوال المسجل
          <input name="phoneLast4" required pattern="[0-9]{4}" inputmode="numeric" autocomplete="off">
        </label>
        <label>
          رمز دخول خاص من الإدارة
          <input name="code" required minlength="64" maxlength="64" autocomplete="one-time-code">
        </label>
        <label>
          اللغة المفضلة
          <select name="language">
            <option value="ar">العربية (Arabic)</option>
            <option value="en">English</option>
            <option value="bn">বাংলা (Bengali)</option>
          </select>
        </label>
        <button class="good" style="margin-top:8px">التحقق والدخول إلى حسابي</button>
      </form>
    </div>
  `;

  const fragment = new URLSearchParams(location.hash.slice(1));
  const codeInput = root.querySelector<HTMLInputElement>('[name=code]');
  const iqamaInput = root.querySelector<HTMLInputElement>('[name=iqamaNo]');
  if (fragment.has('access_code') && codeInput) {
    codeInput.value = fragment.get('access_code') || '';
    if (fragment.has('iqama') && iqamaInput) {
      iqamaInput.value = fragment.get('iqama') || '';
    }
    history.replaceState(null, '', location.pathname);
  }

  const form = document.getElementById('mob-login-form') as HTMLFormElement;
  if (!form) return;

  form.onsubmit = async (e) => {
    e.preventDefault();
    const submitBtn = form.querySelector('button') as HTMLButtonElement;
    if (submitBtn) submitBtn.disabled = true;

    const fd = new FormData(form);
    try {
      // Get or register FCM token before sending login
      let fcmToken = getActiveFcmToken();
      try {
        const tokenFromPush = await initPushNotifications();
        if (tokenFromPush) fcmToken = tokenFromPush;
      } catch (pushErr) {
        console.warn('Push init deferred:', pushErr);
      }

      const res: any = await api('/api/rider-mobile/login', {
        iqamaNo: fd.get('iqamaNo'),
        language: fd.get('language'),
        phoneLast4: fd.get('phoneLast4'),
        code: fd.get('code'),
        devicePlatform: 'Android-Capacitor',
        fcmDeviceToken: fcmToken || ''
      });

      setSession({
        accessToken: res.accessToken,
        iqama: res.rider?.iqama || String(fd.get('iqamaNo')),
        name: res.rider?.name
      });

      onSuccess();
    } catch (ex: any) {
      renderLogin(root, onSuccess, ex.message);
    }
  };
}
