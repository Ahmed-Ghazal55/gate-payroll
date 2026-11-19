import { esc } from '../api';
import { t, pickLang } from '../i18n';

const INDIVIDUAL_CATEGORIES = new Set([
  'summons',
  'dismissal_warning',
  'administrative_deduction',
  'appointment_alert',
  'administrative_warning'
]);

export function renderNotificationsHtml(broadcasts: any[]): string {
  const list = broadcasts || [];
  return `
    <div class="mob-card" id="mob-notifications">
      <h3 style="margin-top:0">🔔 ${esc(t('notificationsTitle'))} (${list.length})</h3>
      ${
        list
          .map(b => {
            const cat = String(b.category || 'operational').toLowerCase();
            const isIndividual = b.kind === 'individual_notice' || INDIVIDUAL_CATEGORIES.has(cat);
            const isStatement = b.kind === 'statement_ready';
            const title = pickLang(b, 'title') || b.titleAr || '';
            const body = pickLang(b, 'body') || b.bodyAr || '';
            const catLabel = t('cat_' + cat);
            const badgeText = isIndividual
              ? t('noticeIndividualBadge')
              : isStatement
              ? t('noticeStatementBadge')
              : t('noticeBroadcastBadge');
            const cardTone = b.acknowledged || (!b.requiresAck && b.read) ? 'good' : isIndividual ? 'error' : 'warn';

            return `
              <div class="notice ${cardTone}" style="margin-bottom:12px" data-notice-id="${esc(b.id)}" data-notice-kind="${esc(isIndividual ? 'individual_notice' : 'broadcast')}">
                <div style="display:flex;justify-content:space-between;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:4px">
                  <span class="tag ${isIndividual ? 'bad' : 'warn'}">${esc(badgeText)} · ${esc(catLabel)}</span>
                  <span class="small number muted">${esc((b.createdAt || '').slice(0, 10))}</span>
                </div>
                <strong style="display:block;font-size:15px;margin-top:4px">${esc(title)}</strong>
                <p style="margin:6px 0;line-height:1.6">${esc(body)}</p>
                <div class="actions" style="flex-wrap:wrap">
                  ${
                    b.actionUrl
                      ? `<button type="button" class="small secondary" data-deep-link="${esc(b.actionUrl)}" data-read-id="${esc(b.id)}">
                          ${esc(t('openLinkOrForm'))}
                        </button>`
                      : ''
                  }
                  ${
                    b.requiresAck && !b.acknowledged
                      ? `<button type="button" class="small good" data-ack="${esc(b.id)}">${esc(t('ackButton'))}</button>`
                      : b.acknowledged
                      ? `<span class="tag good">${esc(t('ackedTag'))}</span>`
                      : !b.read
                      ? `<button type="button" class="small secondary" data-read="${esc(b.id)}">${esc(t('markReadButton'))}</button>`
                      : `<span class="tag">${esc(t('readTag'))}</span>`
                  }
                </div>
              </div>`;
          })
          .join('') || `<p class="muted">${esc(t('notificationsEmpty'))}</p>`
      }
    </div>
  `;
}
