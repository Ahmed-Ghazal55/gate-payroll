import { api, esc, money } from '../api';

export const LABELS: Record<string, string> = {
  debt_installment: 'قسط مديونية',
  vehicle_charge: 'إيجار المركبة',
  account_deduction: 'خصم الحساب',
  penalty: 'غرامة',
  adjustment: 'تسوية'
};

export async function renderStatementHtml(period: string): Promise<string> {
  const currentMonth = new Date().toISOString().slice(0, 7);
  const picker = `
    <div class="mob-card">
      <label class="small muted">
        الشهر 
        <input type="month" id="mob-period" value="${esc(period)}" max="${esc(currentMonth)}">
      </label>
    </div>
  `;

  let s: any;
  try {
    s = await api('/api/rider-mobile/statement?period=' + encodeURIComponent(period));
  } catch (ex: any) {
    if (/جلسة|401/.test(ex.message)) throw ex;
    return picker + `<div class="mob-card muted" id="mob-statement-empty">لا يوجد كشف معتمد لهذا الشهر بعد. ${esc(ex.message)}</div>`;
  }

  const cards: any[] = s.cards || [];
  const ded: any[] = s.deductions || [];
  if (!cards.length && !ded.length) {
    return picker + `<div class="mob-card muted" id="mob-statement-empty">لا يوجد كشف معتمد لهذا الشهر بعد.</div>`;
  }

  const net = Number(s.closing ?? s.net ?? 0);
  return picker + `
    <div class="mob-kpis" id="mob-statement">
      <div class="mob-kpi">
        <div class="muted small">الصافي</div>
        <strong class="number ${Number(s.net) < 0 ? 'money-negative' : 'money-positive'}">${money(s.net)}</strong>
      </div>
      <div class="mob-kpi">
        <div class="muted small">مرحّل</div>
        <strong class="number">${money(s.carried)}</strong>
      </div>
      <div class="mob-kpi">
        <div class="muted small">الرصيد الختامي</div>
        <strong class="number ${net < 0 ? 'money-negative' : 'money-positive'}">${money(net)}</strong>
      </div>
    </div>
    ${cards.map(c => `
      <div class="mob-card">
        <div style="display:flex;justify-content:space-between">
          <strong>${esc(c.platform || c.app)}</strong>
          <span class="small muted">${esc(c.schemeName || '')}</span>
        </div>
        <div class="small number" style="margin-top:6px;line-height:1.9">
          ${c.orders != null ? `الطلبات: <b>${esc(c.orders)}</b><br>` : ''}
          الإجمالي: <b>${money(c.gross)}</b>
          ${Number(c.incentives) ? ` · الحوافز: <b class="money-positive">${money(c.incentives)}</b>` : ''}
          ${Number(c.companyCommission) ? `<br>عمولة الشركة: <b class="money-negative">${money(c.companyCommission)}</b>` : ''}
          ${Number(c.vehicleCharge) ? ` · المركبة: <b class="money-negative">${money(c.vehicleCharge)}</b>` : ''}
        </div>
      </div>`).join('')}
    ${ded.length ? `
      <div class="mob-card">
        <h3 style="margin-top:0">الاستقطاعات</h3>
        ${ded.map(d => `
          <div style="display:flex;justify-content:space-between;border-bottom:1px solid var(--line);padding:6px 0" class="small">
            <span>${esc(d.description || LABELS[d.category] || d.category)}</span>
            <b class="number money-negative">${money(Math.abs(Number(d.amount)))}</b>
          </div>`).join('')}
      </div>` : ''}
    <p class="small muted" style="text-align:center">
      ${s.status === 'approved' || s.approvedOnly ? 'كشف معتمد' : esc(s.status || '')}
    </p>
  `;
}
