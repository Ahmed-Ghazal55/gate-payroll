import { api, esc } from '../api';
import { t } from '../i18n';

function statusTag(status: string): [string, string] {
  if (status === 'active') return [t('sessActive'), 'good'];
  if (status === 'revoked') return [t('sessRevoked'), 'bad'];
  return [t('sessExpired'), ''];
}

export async function renderDevicesHtml(): Promise<string> {
  const rows: any = await api('/api/rider-mobile/sessions/mine');
  const list: any[] = Array.isArray(rows) ? rows : rows.items || [];

  return `
    <div class="mob-card" id="mob-devices">
      <h3 style="margin-top:0">📱 ${esc(t('devicesTitle'))}</h3>
      <p class="small muted">${esc(t('devicesHint'))}</p>
      ${
        list
          .map(s => {
            const [label, cls] = statusTag(s.status);
            return `
              <div style="display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--line);padding:10px 0" class="small">
                <div>
                  <b class="number">${esc(s.deviceFingerprint || t('deviceDefault'))}</b>
                  <div class="muted number" style="margin-top:4px">
                    ${esc((s.createdAt || '').slice(0, 16).replace('T', ' '))} · ${esc(t('lastUsed'))} ${esc((s.lastSeenAt || '').slice(0, 16).replace('T', ' '))}
                  </div>
                </div>
                <span class="tag ${cls}">${esc(label)}</span>
              </div>`;
          })
          .join('') || `<p class="muted">${esc(t('devicesEmpty'))}</p>`
      }
    </div>
  `;
}
