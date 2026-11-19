import { api, esc } from '../api';
import { getSession, setSession, setActiveFcmToken } from '../state';
import { renderStatementHtml } from './statement';
import { renderNotificationsHtml } from './notifications';
import { renderRequestsHtml, type RequestTypeKey } from './requests';
import { renderDebtsHtml } from './debts';
import { renderDevicesHtml } from './devices';
import { getLanguage, setLanguage, applyDirection, t, validateSaudiIban, type Language } from '../i18n';
import { routeNotificationLink, extractNotificationPeriod } from '../push';

export function prevMonth(): string {
  const d = new Date();
  d.setDate(1);
  d.setMonth(d.getMonth() - 1);
  return d.toISOString().slice(0, 7);
}

export class PortalController {
  private root: HTMLElement;
  private onSignOut: (msg?: string) => void;
  private tab: string = 'statement';
  private period: string = prevMonth();
  private selectedRequestType: RequestTypeKey = 'advance';
  private portalData: any = null;

  constructor(root: HTMLElement, onSignOut: (msg?: string) => void) {
    this.root = root;
    this.onSignOut = onSignOut;
  }

  public setTab(tab: string, periodOverride?: string) {
    this.tab = tab;
    if (periodOverride && /^\d{4}-\d{2}$/.test(periodOverride)) {
      this.period = periodOverride;
    }
    this.render();
  }

  public async render() {
    const session = getSession();
    if (!session?.iqama) {
      return this.onSignOut();
    }

    applyDirection(getLanguage());

    try {
      this.portalData = await api('/api/rider-mobile/portal/' + encodeURIComponent(session.iqama));
      if (this.portalData?.language && !localStorage.getItem('gate_rider_lang')) {
        setLanguage(this.portalData.language as Language);
      }
    } catch (ex: any) {
      if (/جلسة|401/.test(ex.message)) {
        return this.signOut(ex.message);
      }
      this.root.innerHTML = `
        <div class="mob-head">
          <div><strong>${esc(t('portalTitle'))}</strong></div>
          <button type="button" class="secondary small" id="mob-logout-err">${esc(t('logout'))}</button>
        </div>
        <div class="mob-card" style="text-align: center; padding: 24px;">
          <div class="notice warn" style="margin-bottom: 16px;">
            <strong>${esc(t('loadFailed'))}</strong>
            <p style="margin: 6px 0;">${esc(ex.message)}</p>
          </div>
          <button type="button" class="good" id="mob-retry-portal">${esc(t('retry'))}</button>
        </div>
      `;
      const retryBtn = document.getElementById('mob-retry-portal');
      if (retryBtn) retryBtn.onclick = () => this.render();
      const logoutErr = document.getElementById('mob-logout-err');
      if (logoutErr) logoutErr.onclick = () => this.signOut();
      return;
    }

    const lang = getLanguage();
    applyDirection(lang);
    const r = this.portalData.rider || {};
    const unread = (this.portalData.broadcasts || []).filter(
      (b: any) => (b.requiresAck && !b.acknowledged) || (!b.requiresAck && !b.read)
    ).length;

    const tabs: [string, string][] = [
      ['statement', '📊 ' + t('tabStatement')],
      ['notifications', '🔔 ' + t('tabNotifications') + (unread ? ` (${unread})` : '')],
      ['requests', '🗂️ ' + t('tabRequests')],
      ['debts', '💳 ' + t('tabDebts')],
      ['devices', '📱 ' + t('tabDevices')]
    ];

    this.root.innerHTML = `
      <div class="mob-head" style="flex-wrap:wrap;gap:8px">
        <div>
          <strong>${esc(r.name || session.iqama)}</strong>
          <div class="muted small number">
            ${esc(r.iqama || session.iqama)}
          </div>
        </div>
        <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap">
          <div class="lang-switch" role="group" aria-label="Language">
            ${(['ar', 'en', 'ur'] as Language[])
              .map(
                l =>
                  `<button type="button" class="small ${l === lang ? 'good' : 'secondary'}" data-lang="${l}" aria-pressed="${
                    l === lang
                  }">${{ ar: 'ع', en: 'EN', ur: 'اردو' }[l]}</button>`
              )
              .join('')}
          </div>
          <button type="button" class="secondary small" id="mob-logout">${esc(t('logout'))}</button>
        </div>
      </div>
      <div class="mob-tabs" role="tablist">
        ${tabs
          .map(
            ([id, label]) =>
              `<button type="button" role="tab" data-tab="${id}" aria-selected="${this.tab === id}" class="${
                this.tab === id ? 'active' : ''
              }">${esc(label)}</button>`
          )
          .join('')}
      </div>
      <div id="mob-body"><div class="mob-card muted">${esc(t('loading'))}</div></div>
    `;

    const logoutBtn = document.getElementById('mob-logout');
    if (logoutBtn) {
      logoutBtn.onclick = async () => {
        try {
          await api('/api/rider-mobile/logout', {});
        } catch {}
        this.signOut();
      };
    }

    this.root.querySelectorAll<HTMLButtonElement>('[data-lang]').forEach(btn => {
      btn.onclick = async () => {
        const nextLang = btn.dataset.lang as Language;
        if (nextLang) {
          setLanguage(nextLang);
          api('/api/rider-mobile/language', { language: nextLang }).catch(() => {});
          await this.render();
        }
      };
    });

    this.root.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach(b => {
      b.onclick = () => {
        if (b.dataset.tab) {
          this.tab = b.dataset.tab;
          this.render();
        }
      };
    });

    await this.renderTabContent();
  }

  private async renderTabContent() {
    const body = document.getElementById('mob-body');
    if (!body) return;

    try {
      if (this.tab === 'statement') {
        body.innerHTML = await renderStatementHtml(this.period, this.portalData?.latestApprovedStatement);
      } else if (this.tab === 'notifications') {
        body.innerHTML = renderNotificationsHtml(this.portalData.broadcasts);
      } else if (this.tab === 'requests') {
        body.innerHTML = await renderRequestsHtml(this.selectedRequestType);
      } else if (this.tab === 'debts') {
        body.innerHTML = renderDebtsHtml(this.portalData);
      } else {
        body.innerHTML = await renderDevicesHtml();
      }
    } catch (ex: any) {
      if (/جلسة|401/.test(ex.message)) {
        return this.signOut(ex.message);
      }
      body.innerHTML = `
        <div class="mob-card" style="text-align: center; padding: 20px;">
          <div class="notice error">${esc(ex.message)}</div>
          <button type="button" class="secondary small" id="mob-retry-tab" style="margin-top: 10px;">${esc(t('retry'))}</button>
        </div>`;
      const retryTab = document.getElementById('mob-retry-tab');
      if (retryTab) retryTab.onclick = () => this.renderTabContent();
    }

    this.bindTabEvents();
  }

  private bindTabEvents() {
    // Request type pills
    this.root.querySelectorAll<HTMLButtonElement>('[data-req-type]').forEach(btn => {
      btn.onclick = () => {
        const rt = btn.dataset.reqType as RequestTypeKey;
        if (rt) {
          this.selectedRequestType = rt;
          this.renderTabContent();
        }
      };
    });

    // Live IBAN auto-detection inside IBAN update request form
    const ibanInput = document.getElementById('mob-req-iban') as HTMLInputElement | null;
    const bankSelect = document.getElementById('mob-req-bank') as HTMLSelectElement | null;
    const ibanHint = document.getElementById('mob-req-iban-hint');
    if (ibanInput && bankSelect) {
      ibanInput.oninput = () => {
        const res = validateSaudiIban(ibanInput.value);
        if (res.valid && res.bank) {
          bankSelect.value = res.bankCode;
          ibanInput.setCustomValidity('');
          if (ibanHint) ibanHint.textContent = `✓ ${res.bank.ar} (${res.bank.swift})`;
        } else if (ibanInput.value.trim().length > 4) {
          if (ibanHint) ibanHint.textContent = t('ibanHint');
        }
      };
    }

    // Advance ask form
    const askForm = document.getElementById('mob-ask-form') as HTMLFormElement | null;
    if (askForm) {
      askForm.onsubmit = async e => {
        e.preventDefault();
        const fd = new FormData(askForm);
        const submitBtn = askForm.querySelector('button[type=submit]') as HTMLButtonElement;
        if (submitBtn) submitBtn.disabled = true;

        try {
          await api('/api/rider-mobile/requests', {
            type: fd.get('type'),
            amount: Number(fd.get('amount')),
            reason: fd.get('reason')
          });
        } catch (ex: any) {
          if (submitBtn) submitBtn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.renderTabContent();
      };
    }

    // Service request form (annual_leave, iban_update, data_update, statement_request, complaint, appointment)
    const serviceForm = document.getElementById('mob-service-request-form') as HTMLFormElement | null;
    if (serviceForm) {
      serviceForm.onsubmit = async e => {
        e.preventDefault();
        const payload = Object.fromEntries(new FormData(serviceForm)) as Record<string, any>;
        const submitBtn = serviceForm.querySelector('button[type=submit]') as HTMLButtonElement;
        if (submitBtn) submitBtn.disabled = true;

        try {
          await api('/api/rider-mobile/requests', payload);
        } catch (ex: any) {
          if (submitBtn) submitBtn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.renderTabContent();
      };
    }

    // In-app open campaign forms (submitted with active rider session — no Iqama re-typing)
    this.root.querySelectorAll<HTMLFormElement>('form[data-inapp-campaign]').forEach(form => {
      form.onsubmit = async e => {
        e.preventDefault();
        const token = form.dataset.inappCampaign || '';
        const payload = Object.fromEntries(new FormData(form)) as Record<string, any>;
        const btn = form.querySelector('button[type=submit]') as HTMLButtonElement;
        if (btn) btn.disabled = true;
        try {
          await api('/api/rider-mobile/forms/' + encodeURIComponent(token) + '/submit', payload);
        } catch (ex: any) {
          if (btn) btn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.renderTabContent();
      };
    });

    // Two-way reply forms on requests
    this.root.querySelectorAll<HTMLFormElement>('form[data-reply-req]').forEach(form => {
      form.onsubmit = async e => {
        e.preventDefault();
        const id = form.dataset.replyReq || '';
        const fd = new FormData(form);
        const btn = form.querySelector('button[type=submit]') as HTMLButtonElement;
        if (btn) btn.disabled = true;
        try {
          await api('/api/rider-mobile/requests/' + encodeURIComponent(id) + '/reply', {
            message: fd.get('message')
          });
        } catch (ex: any) {
          if (btn) btn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.renderTabContent();
      };
    });

    // Cancel request
    this.root.querySelectorAll<HTMLButtonElement>('[data-cancel-ask]').forEach(btn => {
      btn.onclick = async () => {
        btn.disabled = true;
        try {
          await api('/api/rider-mobile/requests/' + encodeURIComponent(btn.dataset.cancelAsk || '') + '/cancel', {});
        } catch (ex: any) {
          btn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.renderTabContent();
      };
    });

    // Period picker & Jump to latest approved period
    const p = document.getElementById('mob-period') as HTMLInputElement | null;
    if (p) {
      p.onchange = () => {
        this.period = p.value;
        this.renderTabContent();
      };
    }

    this.root.querySelectorAll<HTMLButtonElement>('[data-jump-period]').forEach(btn => {
      btn.onclick = () => {
        if (btn.dataset.jumpPeriod) {
          this.period = btn.dataset.jumpPeriod;
          this.renderTabContent();
        }
      };
    });

    // Acknowledge notification (stores read receipt)
    this.root.querySelectorAll<HTMLButtonElement>('[data-ack]').forEach(btn => {
      btn.onclick = async () => {
        btn.disabled = true;
        const session = getSession();
        try {
          await api('/api/rider-mobile/broadcasts/' + encodeURIComponent(btn.dataset.ack || '') + '/ack', {
            iqama: session?.iqama
          });
        } catch (ex: any) {
          btn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.render();
      };
    });

    // Mark notification as read (stores read receipt)
    this.root.querySelectorAll<HTMLButtonElement>('[data-read]').forEach(btn => {
      btn.onclick = async () => {
        btn.disabled = true;
        try {
          await api('/api/rider-mobile/broadcasts/' + encodeURIComponent(btn.dataset.read || '') + '/read', {});
        } catch (ex: any) {
          btn.disabled = false;
          this.showAlert(ex.message);
          return;
        }
        await this.render();
      };
    });

    // Deep-link buttons on notifications
    this.root.querySelectorAll<HTMLButtonElement>('[data-deep-link]').forEach(btn => {
      btn.onclick = async () => {
        const link = btn.dataset.deepLink || '';
        const readId = btn.dataset.readId || '';
        if (readId) {
          api('/api/rider-mobile/broadcasts/' + encodeURIComponent(readId) + '/read', {}).catch(() => {});
        }
        const targetTab = routeNotificationLink(link);
        const targetPeriod = extractNotificationPeriod(link);
        this.setTab(targetTab, targetPeriod);
      };
    });
  }

  private showAlert(msg: string) {
    const body = document.getElementById('mob-body');
    if (body) {
      body.insertAdjacentHTML('afterbegin', `<div class="notice error">${esc(msg)}</div>`);
    }
  }

  private async signOut(msg?: string) {
    await setSession(null);
    await setActiveFcmToken('');
    this.portalData = null;
    this.onSignOut(msg);
  }
}
