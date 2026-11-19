import { api, esc, money } from '../api';
import { t, pickLang } from '../i18n';

function markerBadge(amount: number): string {
  if (amount > 0) {
    return `<span class="tag good statement-marker" data-marker="for_you">+ ${esc(t('forYou'))}</span>`;
  }
  if (amount < 0) {
    return `<span class="tag bad statement-marker" data-marker="on_you">− ${esc(t('onYou'))}</span>`;
  }
  return `<span class="tag statement-marker" data-marker="balanced">${esc(t('balanced'))}</span>`;
}

function renderHomeSummaryBanner(latestSummary: any, currentPeriod: string): string {
  if (!latestSummary || !latestSummary.available) {
    return `
      <div class="mob-card home-summary-card" id="mob-home-summary">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <strong>📊 ${esc(t('homeLatestTitle'))}</strong>
        </div>
        <div class="small muted" style="margin-top:6px">${esc(t('homeNoApprovedYet'))}</div>
      </div>
    `;
  }

  const closing = Number(latestSummary.closing ?? latestSummary.net ?? 0);
  const latestPeriod = String(latestSummary.period || '');
  return `
    <div class="mob-card home-summary-card" id="mob-home-summary">
      <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap">
        <div>
          <strong>📊 ${esc(t('homeLatestTitle'))}</strong>
          <span class="tag good" style="margin-inline-start:6px">${esc(latestPeriod)}</span>
        </div>
        ${markerBadge(closing)}
      </div>
      <div style="display:flex;justify-content:space-between;align-items:baseline;margin-top:10px;flex-wrap:wrap;gap:8px">
        <div>
          <span class="small muted">${esc(t('closingBalance'))}: </span>
          <strong class="number ${closing < 0 ? 'money-negative' : 'money-positive'}" style="font-size:1.35rem">
            ${money(Math.abs(closing))} ${esc(t('currencySar'))}
          </strong>
        </div>
        ${
          latestPeriod && latestPeriod !== currentPeriod
            ? `<button type="button" class="small secondary" data-jump-period="${esc(latestPeriod)}">${esc(t('homeViewMonth'))} (${esc(latestPeriod)})</button>`
            : ''
        }
      </div>
    </div>
  `;
}

export async function renderStatementHtml(period: string, latestSummary?: any): Promise<string> {
  const currentMonth = new Date().toISOString().slice(0, 7);
  const homeBanner = renderHomeSummaryBanner(latestSummary, period);
  const picker = `
    ${homeBanner}
    <div class="mob-card">
      <label for="mob-period" class="small muted">
        📅 ${esc(t('monthLabel'))}
        <input type="month" id="mob-period" value="${esc(period)}" max="${esc(currentMonth)}">
      </label>
    </div>
  `;

  let s: any;
  try {
    s = await api('/api/rider-mobile/statement?period=' + encodeURIComponent(period));
  } catch (ex: any) {
    if (/جلسة|401/.test(ex.message)) throw ex;
    return picker + `<div class="mob-card muted" id="mob-statement-empty">${esc(t('statementEmpty'))} ${esc(ex.message)}</div>`;
  }

  const cards: any[] = s.cards || [];
  const ded: any[] = s.deductions || [];
  const shortLinesBalanced = s.shortLinesBalanced !== false;
  const shortLines: any[] =
    shortLinesBalanced && Array.isArray(s.shortLines) ? s.shortLines : [];
  if (s.source === 'none' || (!cards.length && !ded.length && shortLinesBalanced && !shortLines.length)) {
    return picker + `<div class="mob-card muted" id="mob-statement-empty">${esc(t('statementEmpty'))}</div>`;
  }

  const netMonthVal = Number(s.net ?? 0);
  const carriedVal = Number(s.carried ?? 0);
  const closingVal = Number(s.closing ?? s.net ?? 0);
  const disclaimer = pickLang(s, 'disclaimer') || t('nonLegalDisclaimer');

  const linesCard =
    shortLines.length > 0
      ? `
    <div class="mob-card" id="mob-statement-lines">
      <h3 style="margin-top:0">📋 ${esc(t('statementItemsTitle'))}</h3>
      ${shortLines
        .map(line => {
          const amt = Number(line.amount ?? 0);
          const absAmt = Number(line.absAmount ?? Math.abs(amt));
          const runBal = Number(line.runningBalance ?? 0);
          const label = pickLang(line, 'label') || line.labelAr || '';
          return `
            <div class="statement-line-row" style="border-bottom:1px solid var(--line);padding:12px 0">
              <div style="display:flex;justify-content:space-between;align-items:center;gap:8px">
                <strong style="font-size:15px">${esc(label)}</strong>
                ${markerBadge(amt)}
              </div>
              <div style="display:flex;justify-content:space-between;align-items:center;margin-top:6px" class="small">
                <span class="number ${amt < 0 ? 'money-negative' : 'money-positive'}" style="font-size:15px;font-weight:700">
                  ${amt < 0 ? '−' : '+'} ${money(absAmt)} ${esc(t('currencySar'))}
                </span>
                <span class="muted">
                  ${esc(t('runningBalanceAfter'))}:
                  <b class="number ${runBal < 0 ? 'money-negative' : 'money-positive'}">${money(Math.abs(runBal))}</b>
                  (${esc(runBal > 0 ? t('forYou') : runBal < 0 ? t('onYou') : t('balanced'))})
                </span>
              </div>
            </div>
          `;
        })
        .join('')}
    </div>
  `
      : `
    <div class="mob-card muted" id="mob-statement-not-available">
      ${esc(t('statementItemsNotAvailable'))}
    </div>
  `;

  return (
    picker +
    `
    <div class="mob-kpis" id="mob-statement">
      <div class="mob-kpi">
        <div class="muted small">${esc(t('netMonth'))}</div>
        <strong class="number ${netMonthVal < 0 ? 'money-negative' : 'money-positive'}">${money(Math.abs(netMonthVal))}</strong>
        <div style="margin-top:4px">${markerBadge(netMonthVal)}</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">${esc(t('carriedBalance'))}</div>
        <strong class="number">${money(Math.abs(carriedVal))}</strong>
        <div style="margin-top:4px">${markerBadge(carriedVal)}</div>
      </div>
      <div class="mob-kpi">
        <div class="muted small">${esc(t('closingBalance'))}</div>
        <strong class="number ${closingVal < 0 ? 'money-negative' : 'money-positive'}">${money(Math.abs(closingVal))}</strong>
        <div style="margin-top:4px">${markerBadge(closingVal)}</div>
      </div>
    </div>

    ${linesCard}

    <div class="mob-card small muted" style="text-align:center" id="mob-statement-disclaimer">
      <div>✓ ${esc(s.status === 'approved' || s.approvedOnly ? t('approvedBadge') : s.status || '')}</div>
      <div style="margin-top:4px">${esc(disclaimer)}</div>
    </div>
  `
  );
}
