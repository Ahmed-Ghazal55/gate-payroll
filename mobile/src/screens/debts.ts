import { esc, money } from '../api';

export function renderDebtsHtml(portal: any): string {
  const d0 = portal || {};
  const debts: any[] = d0.debts || [];

  return `
    <div class="mob-kpis">
      <div class="mob-kpi">
        <div class="muted small">المديونية المتبقية</div>
        <strong class="number money-negative">${money(d0.totalRemainingDebt)}</strong>
        <div class="small muted">ر.س</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">إجمالي المسدد</div>
        <strong class="number money-positive">${money(d0.totalPaidDebt)}</strong>
        <div class="small muted">ر.س</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">إعفاءات ممنوحة</div>
        <strong class="number">${money(d0.totalWaivedDebt)}</strong>
        <div class="small muted">ر.س</div>
      </div>
    </div>
    <div class="mob-card" id="mob-debts">
      <h3 style="margin-top:0">بيان المديونيات والأقساط (${debts.length})</h3>
      ${debts.map(d => `
        <div style="border-bottom:1px solid var(--line);padding:10px 0">
          <div style="display:flex;justify-content:space-between;align-items:center">
            <strong>${esc(d.categoryAr)}</strong>
            <span class="tag ${d.status === 'settled' ? 'good' : 'warn'}">
              ${esc(d.status === 'settled' ? 'مسدد' : 'نشط')}
            </span>
          </div>
          <div class="small muted" style="margin-top:4px">
            السجل الدائن: ${esc(d.creditorSponsorNameAr)} · بداية الاستقطاع: ${esc(d.effectiveFrom)}
          </div>
          <div class="small number" style="margin-top:4px;line-height:1.7">
            الإجمالي: <b>${money(d.totalAmount)}</b> · 
            المسدد: <b class="money-positive">${money(d.paidAmount)}</b> · 
            الإعفاء: <b>${money(d.waivedAmount)}</b> · 
            المتبقي: <b class="money-negative">${money(d.remainingBalance)} ر.س</b> 
            (القسط: ${money(d.monthlyInstallment)})
          </div>
        </div>`).join('') || '<p class="muted">لا توجد مديونيات مسجلة على حسابك.</p>'}
    </div>
  `;
}
