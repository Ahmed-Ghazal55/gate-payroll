import { esc, money } from '../api';
import { t, pickLang } from '../i18n';

export function renderDebtsHtml(portal: any): string {
  const d0 = portal || {};
  const debts: any[] = d0.debts || [];
  const totalRem = Number(d0.totalRemainingDebt || 0);

  return `
    <div class="mob-kpis">
      <div class="mob-kpi">
        <div class="muted small">${esc(t('debtsRemaining'))}</div>
        <strong class="number money-negative">${money(totalRem)}</strong>
        <div class="small muted">${esc(t('currencySar'))} · ${esc(totalRem > 0 ? t('onYou') : t('statusSettled'))}</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">${esc(t('debtsPaid'))}</div>
        <strong class="number money-positive">${money(d0.totalPaidDebt)}</strong>
        <div class="small muted">${esc(t('currencySar'))}</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">${esc(t('debtsWaived'))}</div>
        <strong class="number">${money(d0.totalWaivedDebt)}</strong>
        <div class="small muted">${esc(t('currencySar'))}</div>
      </div>
    </div>
    <div class="mob-card" id="mob-debts">
      <h3 style="margin-top:0">💳 ${esc(t('debtsListTitle'))} (${debts.length})</h3>
      ${
        debts
          .map(d => {
            const rem = Number(d.remainingBalance || 0);
            const settled = d.status === 'settled' || rem <= 0;
            const catTitle = pickLang(d, 'category') || d.categoryAr || d.category || '';
            return `
              <div style="border-bottom:1px solid var(--line);padding:12px 0">
                <div style="display:flex;justify-content:space-between;align-items:center;gap:8px">
                  <strong style="font-size:15px">${esc(catTitle)}</strong>
                  <div style="display:flex;gap:6px;align-items:center">
                    ${rem > 0 ? `<span class="tag bad">− ${esc(t('onYou'))}</span>` : ''}
                    <span class="tag ${settled ? 'good' : 'warn'}">
                      ${esc(settled ? t('statusSettled') : t('statusActive'))}
                    </span>
                  </div>
                </div>
                ${
                  d.effectiveFrom
                    ? `<div class="small muted" style="margin-top:4px">${esc(t('effectiveFrom'))}: <span class="number">${esc(d.effectiveFrom)}</span></div>`
                    : ''
                }
                <div class="small number" style="margin-top:6px;line-height:1.8">
                  ${esc(t('remainingAmount'))}: <b class="money-negative">${money(rem)} ${esc(t('currencySar'))}</b> ·
                  ${esc(t('monthlyInstallment'))}: <b>${money(d.monthlyInstallment)} ${esc(t('currencySar'))}</b><br>
                  ${esc(t('totalAmount'))}: <b>${money(d.totalAmount)}</b> ·
                  ${esc(t('paidAmount'))}: <b class="money-positive">${money(d.paidAmount)}</b> ·
                  ${esc(t('waivedAmount'))}: <b>${money(d.waivedAmount)}</b>
                </div>
              </div>`;
          })
          .join('') || `<p class="muted">${esc(t('debtsEmpty'))}</p>`
      }
    </div>
  `;
}
