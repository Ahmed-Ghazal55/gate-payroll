import { api, esc } from '../api';
import { setSession, getActiveFcmToken } from '../state';
import { initPushNotifications } from '../push';
import { getLanguage, setLanguage, applyDirection, t, type Language } from '../i18n';

export function renderLogin(root: HTMLElement, onSuccess: () => void, err: string = ''): void {
  const lang = getLanguage();
  applyDirection(lang);

  root.innerHTML = `
    <div class="mob-head">
      <div>
        <strong>${esc(t('portalTitle'))}</strong>
        <div class="muted small">${esc(t('portalSubtitle'))}</div>
      </div>
      <div class="lang-switch" role="group" aria-label="Language">
        ${(['ar', 'en', 'ur'] as Language[])
          .map(
            l =>
              `<button type="button" class="small ${l === lang ? 'good' : 'secondary'}" data-login-lang="${l}" aria-pressed="${l === lang}">${
                { ar: 'العربية', en: 'English', ur: 'اردو' }[l]
              }</button>`
          )
          .join('')}
      </div>
    </div>
    <div class="mob-card">
      <h2 style="margin-top:0">${esc(t('loginTitle'))}</h2>
      <p class="muted small">${esc(t('loginHint'))}</p>
      ${err ? `<div class="notice error" role="alert">${esc(err)}</div>` : ''}
      <form id="mob-login-form" class="form-grid">
        <label for="login-iqama">
          ${esc(t('iqamaLabel'))}
          <input id="login-iqama" name="iqamaNo" required pattern="[12][0-9]{9}" inputmode="numeric" dir="ltr" autocorrect="off" autocapitalize="none" spellcheck="false" placeholder="2XXXXXXXXX">
        </label>
        <label for="login-phone4">
          ${esc(t('phoneLast4Label'))}
          <input id="login-phone4" name="phoneLast4" required pattern="[0-9]{4}" maxlength="4" inputmode="numeric" dir="ltr" autocomplete="off">
        </label>
        <label for="login-code">
          ${esc(t('accessCodeLabel'))}
          <input id="login-code" name="code" required minlength="64" maxlength="64" dir="ltr" autocomplete="one-time-code" autocorrect="off" autocapitalize="none" spellcheck="false">
        </label>
        <label for="login-language">
          ${esc(t('prefLangLabel'))}
          <select id="login-language" name="language">
            <option value="ar" ${lang === 'ar' ? 'selected' : ''}>العربية (Arabic)</option>
            <option value="en" ${lang === 'en' ? 'selected' : ''}>English</option>
            <option value="ur" ${lang === 'ur' ? 'selected' : ''}>اردو (Urdu)</option>
          </select>
        </label>
        <button type="submit" class="good" style="margin-top:8px">${esc(t('loginSubmit'))}</button>
      </form>
    </div>
  `;

  root.querySelectorAll<HTMLButtonElement>('[data-login-lang]').forEach(btn => {
    btn.onclick = () => {
      const next = btn.dataset.loginLang as Language;
      if (next) {
        setLanguage(next);
        renderLogin(root, onSuccess, err);
      }
    };
  });

  const langSelect = root.querySelector<HTMLSelectElement>('#login-language');
  if (langSelect) {
    langSelect.onchange = () => {
      const next = langSelect.value as Language;
      setLanguage(next);
      renderLogin(root, onSuccess, err);
    };
  }

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

  form.onsubmit = async e => {
    e.preventDefault();
    const submitBtn = form.querySelector('button[type=submit]') as HTMLButtonElement;
    if (submitBtn) submitBtn.disabled = true;

    const fd = new FormData(form);
    const chosenLang = (String(fd.get('language') || lang) as Language) || 'ar';
    setLanguage(chosenLang);

    try {
      let fcmToken = getActiveFcmToken();
      try {
        const tokenFromPush = await initPushNotifications();
        if (tokenFromPush) fcmToken = tokenFromPush;
      } catch (pushErr) {
        console.warn('Push init deferred:', pushErr);
      }

      const res: any = await api('/api/rider-mobile/login', {
        iqamaNo: fd.get('iqamaNo'),
        language: chosenLang,
        phoneLast4: fd.get('phoneLast4'),
        code: fd.get('code'),
        devicePlatform: 'Android-Capacitor',
        fcmDeviceToken: fcmToken || ''
      });

      await setSession({
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
