import { api, esc } from '../api';
import { getSession, setSession, setActiveFcmToken } from '../state';
import { renderStatementHtml } from './statement';
import { renderNotificationsHtml } from './notifications';
import { renderRequestsHtml } from './requests';
import { renderDebtsHtml } from './debts';
import { renderDevicesHtml } from './devices';

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
  private portalData: any = null;

  constructor(root: HTMLElement, onSignOut: (msg?: string) => void) {
    this.root = root;
    this.onSignOut = onSignOut;
  }

  public setTab(tab: string) {
    this.tab = tab;
    this.render();
  }

  public async render() {
    const session = getSession();
    if (!session?.iqama) {
      return this.onSignOut();
    }

    try {
      this.portalData = await api('/api/rider-mobile/portal/' + encodeURIComponent(session.iqama));
    } catch (ex: any) {
      return this.signOut(ex.message);
    }

    const r = this.portalData.rider || {};
    const unread = (this.portalData.broadcasts || []).filter(
      (b: any) => b.requiresAck && !b.acknowledged
    ).length;

    const tabs: [string, string][] = [
      ['statement', 'كشف الحساب'],
      ['notifications', 'الإشعارات' + (unread ? ` (${unread})` : '')],
      ['requests', 'طلباتي'],
      ['debts', 'المديونيات'],
      ['devices', 'أجهزتي']
    ];

    this.root.innerHTML = `
      <div class="mob-head">
        <div>
          <strong>${esc(r.name || session.iqama)}</strong>
          <div class="muted small number">
            ${esc(r.iqama || session.iqama)} · ${esc(r.sponsor || 'شركة البوابة')}
          </div>
        </div>
        <button class="secondary small" id="mob-logout">خروج</button>
      </div>
      <div class="mob-tabs" role="tablist">
        ${tabs
          .map(
            ([id, label]) =>
              `<button role="tab" data-tab="${id}" aria-selected="${this.tab === id}" class="${
                this.tab === id ? 'active' : ''
              }">${esc(label)}</button>`
          )
          .join('')}
      </div>
      <div id="mob-body"><div class="mob-card muted">جاري التحميل...</div></div>
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

    this.root.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach((b) => {
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
        body.innerHTML = await renderStatementHtml(this.period);
      } else if (this.tab === 'notifications') {
        body.innerHTML = renderNotificationsHtml(this.portalData.broadcasts);
      } else if (this.tab === 'requests') {
        body.innerHTML = await renderRequestsHtml();
      } else if (this.tab === 'debts') {
        body.innerHTML = renderDebtsHtml(this.portalData);
      } else {
        body.innerHTML = await renderDevicesHtml();
      }
    } catch (ex: any) {
      if (/جلسة|401/.test(ex.message)) {
        return this.signOut(ex.message);
      }
      body.innerHTML = `<div class="mob-card"><div class="notice error">${esc(ex.message)}</div></div>`;
    }

    this.bindTabEvents();
  }

  private bindTabEvents() {
    const form = document.getElementById('mob-ask-form') as HTMLFormElement;
    if (form) {
      form.onsubmit = async (e) => {
        e.preventDefault();
        const fd = new FormData(form);
        const submitBtn = form.querySelector('button') as HTMLButtonElement;
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

    this.root.querySelectorAll<HTMLButtonElement>('[data-cancel-ask]').forEach((btn) => {
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

    const p = document.getElementById('mob-period') as HTMLInputElement;
    if (p) {
      p.onchange = () => {
        this.period = p.value;
        this.renderTabContent();
      };
    }

    this.root.querySelectorAll<HTMLButtonElement>('[data-ack]').forEach((btn) => {
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
