import { api, esc } from '../api';

const STATUS_MAP: Record<string, [string, string]> = {
  active: ['نشطة', 'good'],
  expired: ['منتهية', ''],
  revoked: ['ملغاة', 'bad']
};

export async function renderDevicesHtml(): Promise<string> {
  const rows: any = await api('/api/rider-mobile/sessions/mine');
  const list: any[] = Array.isArray(rows) ? rows : (rows.items || []);

  return `
    <div class="mob-card" id="mob-devices">
      <h3 style="margin-top:0">الأجهزة التي سجلت الدخول</h3>
      <p class="small muted">لو شفت جهاز مش بتاعك، كلم الإدارة لإلغاء جلسته فورًا.</p>
      ${list.map(s => {
        const [label, cls] = STATUS_MAP[s.status] || [s.status, ''];
        return `
          <div style="display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--line);padding:10px 0" class="small">
            <div>
              <b class="number">${esc(s.deviceFingerprint || 'جهاز مندوب')}</b>
              <div class="muted number" style="margin-top:4px">
                ${esc((s.createdAt || '').slice(0, 16).replace('T', ' '))} · آخر استخدام ${esc((s.lastSeenAt || '').slice(0, 16).replace('T', ' '))}
              </div>
            </div>
            <span class="tag ${cls}">${esc(label)}</span>
          </div>`;
      }).join('') || '<p class="muted">لا توجد أجهزة مسجلة.</p>'}
    </div>
  `;
}
