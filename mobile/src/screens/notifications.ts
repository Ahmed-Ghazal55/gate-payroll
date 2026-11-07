import { esc } from '../api';

export function renderNotificationsHtml(broadcasts: any[]): string {
  const list = broadcasts || [];
  return `
    <div class="mob-card" id="mob-notifications">
      <h3 style="margin-top:0">الإشعارات الإدارية والتشغيلية والنماذج (${list.length})</h3>
      ${list.map(b => `
        <div class="notice ${b.acknowledged || !b.requiresAck ? 'good' : 'warn'}" style="margin-bottom:10px">
          <div style="display:flex;justify-content:space-between;align-items:center">
            <strong>${esc(b.titleAr)}</strong>
            <span class="small number muted">${esc((b.createdAt || '').slice(0, 10))}</span>
          </div>
          <p style="margin:6px 0">${esc(b.bodyAr)}</p>
          <div class="actions">
            ${b.actionUrl && /^https?:\/\//i.test(b.actionUrl) ? `
              <a class="button small secondary" href="${esc(b.actionUrl)}" target="_blank" rel="noopener noreferrer">
                فتح الرابط / الفورم ↗
              </a>` : ''}
            ${b.requiresAck && !b.acknowledged ? `
              <button class="small good" data-ack="${esc(b.id)}">✓ قرأت وأوافق على الإشعار</button>
            ` : b.acknowledged ? `
              <span class="tag good">تم التأكيد ✓</span>
            ` : ''}
          </div>
        </div>`).join('') || '<p class="muted">لا توجد إشعارات جديدة حالياً.</p>'}
    </div>
  `;
}
