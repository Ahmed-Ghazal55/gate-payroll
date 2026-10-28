// كشف حساب المندوب المبسّط — واجهة 0.8.0
// شاشة لقراءة القيود المثبتة بلغة سهلة واضحة بدون مدين ودائن

// let statementSimpleRiderIqama is declared in enterprise.ts
let statementSimpleSponsorFilter: string = '';
let statementSimpleFromMonth: string = '';
let statementSimpleToMonth: string = '';
const statementSimpleExpandedMonths: Set<string> = new Set<string>();

function initSimpleStatementDates(): void {
  const nowMonth = new Date().toISOString().slice(0, 7);
  if (!statementSimpleToMonth) statementSimpleToMonth = nowMonth;
  if (!statementSimpleFromMonth) statementSimpleFromMonth = nowMonth;
}

function simpleBucketLabel(bucket: string): string {
  switch (bucket) {
    case 'earned': return 'مستحقات عمل';
    case 'deducted': return 'خصم';
    case 'received': return 'استلام دفعة';
    case 'transferred': return 'مناقلة شركات';
    case 'debt_added': return 'إضافة مديونية';
    case 'debt_collected': return 'سداد / تسوية مديونية';
    case 'jahez_cash': return 'كاش جاهز';
    case 'other': default: return 'طرف مقابل / أخرى';
  }
}

async function loadSimpleStatementData(iqama: string, sponsorId: string, fromMonth: string, toMonth: string): Promise<any> {
  const query = `?from=${encodeURIComponent(fromMonth)}&to=${encodeURIComponent(toMonth)}${sponsorId ? `&sponsorId=${encodeURIComponent(sponsorId)}` : ''}`;
  return await api(`/finance/statement/rider/${encodeURIComponent(iqama)}/simple${query}`);
}

function renderSimpleMonthRows(m: any, allDetails: any[]): string {
  const monthDetails = allDetails.filter((d: any) => d.period === m.period);
  const isExpanded = statementSimpleExpandedMonths.has(m.period);
  const debtNet = (m.totals.debt_added || 0) + (m.totals.debt_collected || 0);

  const monthRow = `
    <tr class="simple-month-row" data-month-period="${esc(m.period)}" style="cursor: pointer;">
      <td>
        <strong style="display: inline-flex; align-items: center; gap: 6px;">
          <span class="expand-icon no-print">${isExpanded ? '▼' : '◀'}</span>
          <span class="number">${esc(m.period)}</span>
        </strong>
      </td>
      <td class="number">${money(m.openingBalance)}</td>
      <td class="number money-positive">${money(m.totals.earned)}</td>
      <td class="number ${m.totals.deducted ? 'money-negative' : ''}">${money(Math.abs(m.totals.deducted))}</td>
      <td class="number">${money(Math.abs(m.totals.received))}</td>
      <td class="number">${money(m.totals.transferred)}</td>
      <td class="number ${debtNet < 0 ? 'money-negative' : ''}">${money(debtNet)}</td>
      <td class="number ${m.netMovement > 0 ? 'money-positive' : m.netMovement < 0 ? 'money-negative' : ''}"><strong>${money(m.netMovement)}</strong></td>
      <td class="number ${m.closingNet > 0 ? 'money-positive' : m.closingNet < 0 ? 'money-negative' : ''}"><strong>${money(m.closingNet)}</strong></td>
      <td class="no-print">
        <button type="button" class="small secondary toggle-details-btn" data-period="${esc(m.period)}">
          ${isExpanded ? 'إخفاء' : `تفاصيل (${monthDetails.length})`}
        </button>
      </td>
    </tr>
  `;

  const detailsRows = monthDetails.length > 0
    ? monthDetails.map((d: any) => `
        <tr>
          <td class="number" style="white-space: nowrap;">${esc(d.date)}</td>
          <td>
            <strong>${esc(d.labelAr)}</strong>
            ${d.description ? `<div class="muted small">${esc(d.description)}</div>` : ''}
          </td>
          <td><span class="tag">${esc(simpleBucketLabel(d.bucket))}</span></td>
          <td class="small">${esc(d.sponsorNameAr || d.sponsorId || '—')}</td>
          <td class="number">${money(d.amount)} ر.س</td>
          <td class="number">
            ${d.affectsNet
              ? `<strong class="${d.netAmount > 0 ? 'money-positive' : d.netAmount < 0 ? 'money-negative' : ''}">${money(d.netAmount)} ر.س</strong>`
              : `<span class="muted small">طرف مقابل — لا يغير الرصيد</span>`}
          </td>
          <td class="number small muted" style="direction: ltr; text-align: right;">${esc(d.journalId)} #${d.lineNumber}</td>
        </tr>
      `).join('')
    : `<tr><td colspan="7" class="muted" style="text-align: center; padding: 14px;">لا توجد تفاصيل حركات مسجلة لهذا الشهر.</td></tr>`;

  const detailsRow = `
    <tr class="simple-month-details-row ${isExpanded ? '' : 'hide'}" data-details-for="${esc(m.period)}">
      <td colspan="10" style="padding: 10px; background: var(--raised); border-top: 0;">
        <div class="panel" style="margin: 0; box-shadow: none; border-color: var(--line);">
          <div style="font-size: 12px; font-weight: 700; margin-bottom: 8px; color: var(--ink);">
            تفاصيل حركات شهر <span class="number">${esc(m.period)}</span> (${monthDetails.length} حركة مثبتة)
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>التاريخ</th>
                  <th>بيان الحركة وتفسيرها</th>
                  <th>التصنيف المبسط</th>
                  <th>السجل التجاري</th>
                  <th>مبلغ الحركة</th>
                  <th>الأثر على الصافي</th>
                  <th>مرجع القيد والسطر</th>
                </tr>
              </thead>
              <tbody>
                ${detailsRows}
              </tbody>
            </table>
          </div>
        </div>
      </td>
    </tr>
  `;

  return monthRow + detailsRow;
}

async function renderSimpleStatementView(): Promise<string> {
  initSimpleStatementDates();
  const riders = cache.riders || [];
  const sponsors = cache.sponsors || [];

  if (!statementSimpleRiderIqama && riders.length) {
    statementSimpleRiderIqama = (typeof statementRiderIqama !== 'undefined' && statementRiderIqama) ? statementRiderIqama : riders[0].iqama;
  }

  const filtersHtml = `
    <div class="panel no-print">
      <div class="form-grid simple-statement-filters">
        <label>المندوب أو الموظف
          <select id="simple-statement-rider-select">
            ${options(riders.map((r: any) => [r.iqama, `${r.name} — ${r.iqama} (${r.sponsor || 'البوابا'})`]), statementSimpleRiderIqama)}
          </select>
        </label>
        <label>الشركة / السجل التجاري
          <select id="simple-statement-sponsor-select">
            <option value="">كل الشركات المسموح بها</option>
            ${options(sponsors.map((s: any) => [s.id, `${s.shortNameAr} — ${s.nameAr}`]), statementSimpleSponsorFilter)}
          </select>
        </label>
        <label>من شهر
          <input type="month" id="simple-statement-from-month" value="${esc(statementSimpleFromMonth)}" required>
        </label>
        <label>إلى شهر
          <input type="month" id="simple-statement-to-month" value="${esc(statementSimpleToMonth)}" required>
        </label>
        <div>
          <button type="button" id="simple-statement-apply-btn" class="secondary" style="width: 100%;">تحديث الكشف</button>
        </div>
      </div>
    </div>
  `;

  if (!statementSimpleRiderIqama) {
    return filtersHtml + empty('اختر مندوباً لعرض الكشف', 'حدد المندوب والفترة المطلوبة من القائمة أعلاه.');
  }

  let st: any = null;
  let fetchError: any = null;
  try {
    st = await loadSimpleStatementData(statementSimpleRiderIqama, statementSimpleSponsorFilter, statementSimpleFromMonth, statementSimpleToMonth);
    cache.currentSimpleStatement = st;
  } catch (err: any) {
    fetchError = err;
  }

  if (fetchError) {
    return filtersHtml + `
      <div class="notice error">
        <strong>تعذر تحميل كشف الحساب:</strong> ${esc(fetchError.message || 'حدث خطأ أثناء جلب البيانات')}
        <div style="margin-top: 10px;">
          <button type="button" class="small secondary" id="simple-statement-retry-btn">إعادة المحاولة</button>
        </div>
      </div>
    `;
  }

  const sponsorObj = sponsors.find((s: any) => s.id === st.sponsorId);
  const sponsorName = sponsorObj ? `${sponsorObj.shortNameAr} — ${sponsorObj.nameAr}` : 'كل الشركات المسموح بها';

  const styles = `
    <style id="rider-statement-styles">
      .simple-statement-filters {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
        gap: 14px;
        align-items: end;
      }
      .simple-statement-stats {
        display: grid;
        grid-template-columns: repeat(6, minmax(0, 1fr));
        gap: 14px;
        margin-bottom: 18px;
      }
      @media (max-width: 1050px) {
        .simple-statement-stats {
          grid-template-columns: repeat(3, minmax(0, 1fr));
        }
      }
      @media (max-width: 600px) {
        .simple-statement-stats {
          grid-template-columns: repeat(2, minmax(0, 1fr));
          gap: 8px;
        }
        .simple-statement-filters {
          grid-template-columns: 1fr;
        }
      }
      .simple-month-row:hover {
        background: var(--raised) !important;
      }
      .expand-icon {
        font-size: 11px;
        color: var(--accent);
        transition: transform 0.15s ease;
      }
      .scope-limited-banner {
        border-inline-start: 4px solid #d97706 !important;
        font-size: 13px !important;
        padding: 12px 16px !important;
        margin-bottom: 16px !important;
      }
      .net-split-cards {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 12px;
      }
      @media (max-width: 700px) {
        .net-split-cards {
          grid-template-columns: 1fr;
        }
      }
      @media print {
        @page {
          size: A4 portrait;
          margin: 10mm 12mm;
        }
        html, body {
          background: #fff !important;
          color: #0f172a !important;
          font-family: Cairo, Tahoma, Arial, sans-serif !important;
          font-size: 10px !important;
          direction: rtl !important;
          -webkit-print-color-adjust: exact !important;
          print-color-adjust: exact !important;
        }
        .shell {
          display: block !important;
        }
        .sidebar, .topbar, .no-print, button, .actions, .toolbar, #toast, dialog, .grid-toolbar, .column-filters, .response-chips {
          display: none !important;
        }
        .content {
          max-width: 100% !important;
          margin: 0 !important;
          padding: 0 !important;
        }
        .panel {
          border: 1px solid #cbd5e1 !important;
          border-radius: 4px !important;
          box-shadow: none !important;
          padding: 8px 10px !important;
          margin-bottom: 10px !important;
          break-inside: avoid;
        }
        .scope-limited-banner {
          display: block !important;
          background: #fff8e9 !important;
          border: 1px solid #f2e2ba !important;
          border-inline-start: 4px solid #d97706 !important;
          color: #927132 !important;
          font-size: 9.5px !important;
          padding: 8px 10px !important;
          margin-bottom: 10px !important;
          break-inside: avoid;
        }
        .simple-statement-stats {
          display: grid !important;
          grid-template-columns: repeat(6, 1fr) !important;
          gap: 6px !important;
          margin-bottom: 10px !important;
        }
        .simple-statement-stats .stat {
          border: 1px solid #cbd5e1 !important;
          border-radius: 4px !important;
          padding: 6px 8px !important;
          box-shadow: none !important;
          background: #f8fafc !important;
          break-inside: avoid;
        }
        .simple-statement-stats .stat strong {
          font-size: 13px !important;
          margin: 2px 0 !important;
        }
        .simple-statement-stats .stat .label {
          font-size: 9.5px !important;
          color: #475569 !important;
        }
        .simple-statement-stats .stat .hint {
          font-size: 8px !important;
          color: #64748b !important;
        }
        .simple-statement-net-split {
          break-inside: avoid;
          margin-bottom: 10px !important;
        }
        .net-split-cards {
          display: grid !important;
          grid-template-columns: repeat(3, 1fr) !important;
          gap: 6px !important;
        }
        .net-split-cards .stat {
          border: 1px solid #cbd5e1 !important;
          border-radius: 4px !important;
          padding: 6px 8px !important;
          box-shadow: none !important;
          background: #f8fafc !important;
          break-inside: avoid;
        }
        .net-split-cards .stat strong {
          font-size: 13px !important;
          margin: 2px 0 !important;
        }
        .net-split-cards .stat .label {
          font-size: 9px !important;
          color: #475569 !important;
        }
        .net-split-cards .stat .hint {
          font-size: 8px !important;
          color: #64748b !important;
        }
        .table-wrap {
          overflow: visible !important;
          max-height: none !important;
        }
        table {
          width: 100% !important;
          border-collapse: collapse !important;
          font-size: 9.5px !important;
          white-space: normal !important;
        }
        th, td {
          border: 1px solid #cbd5e1 !important;
          padding: 4px 6px !important;
        }
        thead th {
          background: #f1f5f9 !important;
          color: #0f172a !important;
        }
        .simple-month-details-row {
          display: table-row !important;
        }
        .simple-month-details-row > td {
          padding: 6px !important;
          background: #fafafa !important;
        }
        .simple-month-details-row .panel {
          border: none !important;
          padding: 0 !important;
          background: transparent !important;
        }
      }
    </style>
  `;

  const isScopeLimited = Boolean(st.scopeLimited || st.closingNetIsComplete === false);
  const finalNetPhrase = isScopeLimited
    ? 'صافي جزئي ضمن نطاقك — لا يمثل الرصيد الكامل'
    : (st.closingLabelAr || (st.closingNet > 0 ? 'مستحق لك لدى الشركة' : st.closingNet < 0 ? 'مستحق عليك للشركة' : 'لا يوجد رصيد متبقٍ'));

  const headerHtml = `
    <div class="panel simple-statement-header">
      <div class="panel-title" style="margin-bottom: 6px; flex-wrap: wrap; gap: 10px;">
        <div>
          <h2 style="margin: 0; font-size: 18px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap;">
            <span>كشف حساب المندوب: <strong>${esc(st.rider?.name || 'غير مسجل')}</strong></span>
            <span class="number" style="font-size: 14px; color: var(--muted);">${esc(st.rider?.iqama || statementSimpleRiderIqama)}</span>
            ${badge(statusLabels[st.rider?.status] || st.rider?.status || 'نشط', st.rider?.status === 'Active' ? 'good' : 'warn')}
            ${isScopeLimited ? `<span class="tag warn">نطاق جزئي (${Number(st.excludedLineCount || 0)} سطر محجوب)</span>` : ''}
          </h2>
          <div class="hint" style="margin-top: 6px; line-height: 1.6;">
            المدينة: <strong>${esc(st.rider?.city || '—')}</strong> ·
            الفترة: <strong>${esc(st.from)}</strong> إلى <strong>${esc(st.to)}</strong> ·
            السجل: <strong>${esc(sponsorName)}</strong> ·
            أساس الرصيد: <strong>${st.balanceBasis === 'posted_journal_lines' ? 'قيود يومية مثبتة' : esc(st.balanceBasis)}</strong>
            ${isScopeLimited ? ` · <span style="color: #b45309; font-weight: 700;">صافي جزئي — غير مكتمل</span>` : ''}
          </div>
        </div>
        <div class="actions no-print">
          <button type="button" class="secondary" data-action="print-simple-statement">
            🖨️ طباعة كشف الحساب (A4)
          </button>
        </div>
      </div>
    </div>
  `;

  const scopeLimitedHtml = isScopeLimited ? `
    <div class="notice warn scope-limited-banner">
      ⚠️ <strong>تنبيه نطاق الصلاحيات:</strong> تم حجب ${Number(st.excludedLineCount || 0)} سطر مالي مثبت يخص المندوب لعدم شمول صلاحيات المستخدم لنطاق الشركة أو الفئة أو المنصة أو مسيرات الرواتب.
      الأرقام المعروضة تمثل <strong>صافياً جزئياً</strong> ضمن نطاق صلاحياتك فقط، ولا تمثل الرصيد النهائي الكامل للمندوب.
    </div>
  ` : '';

  const netDebtMovement = (st.periodTotals?.debt_added || 0) + (st.periodTotals?.debt_collected || 0) + (st.periodTotals?.jahez_cash || 0) + (st.periodTotals?.other || 0);

  const statsHtml = `
    <div class="stats simple-statement-stats">
      <article class="stat">
        <div class="label">اتستحقله</div>
        <strong class="money-positive">${money(st.periodTotals?.earned || 0)}</strong>
        <div class="hint">مستحقات عمل معتمدة خلال الفترة</div>
      </article>
      <article class="stat">
        <div class="label">اتخصم</div>
        <strong class="${(st.periodTotals?.deducted || 0) < 0 ? 'money-negative' : ''}">${money(st.periodTotals?.deducted || 0)}</strong>
        <div class="hint">استقطاعات وخصومات معتمدة</div>
      </article>
      <article class="stat">
        <div class="label">استلم</div>
        <strong class="${(st.periodTotals?.received || 0) < 0 ? 'money-negative' : ''}">${money(st.periodTotals?.received || 0)}</strong>
        <div class="hint">دفعات نقدية وبنكية مسلّمة</div>
      </article>
      <article class="stat">
        <div class="label">اتحوّل</div>
        <strong class="${(st.periodTotals?.transferred || 0) < 0 ? 'money-negative' : (st.periodTotals?.transferred || 0) > 0 ? 'money-positive' : ''}">${money(st.periodTotals?.transferred || 0)}</strong>
        <div class="hint">مناقلات حقوق بين السجلات</div>
      </article>
      <article class="stat">
        <div class="label">مديونية</div>
        <strong class="${netDebtMovement < 0 ? 'money-negative' : netDebtMovement > 0 ? 'money-positive' : ''}">${money(netDebtMovement)}</strong>
        <div class="hint">أضيف: ${money(Math.abs(st.periodTotals?.debt_added || 0))} · سُدد: ${money(st.periodTotals?.debt_collected || 0)}${(st.periodTotals?.jahez_cash || 0) !== 0 ? ` · جاهز: ${money(st.periodTotals?.jahez_cash)}` : ''}${(st.periodTotals?.other || 0) !== 0 ? ` · أخرى: ${money(st.periodTotals?.other)}` : ''}</div>
      </article>
      <article class="stat">
        <div class="label">الصافي</div>
        <strong class="${st.closingNet > 0 ? 'money-positive' : st.closingNet < 0 ? 'money-negative' : ''}">${money(st.closingNet)}</strong>
        <div class="hint">${esc(finalNetPhrase)} · رصيد الافتتاح: ${money(st.openingBalance)}</div>
      </article>
    </div>
  `;

  const netSplitHtml = `
    <div class="panel simple-statement-net-split">
      <div class="panel-title" style="margin-bottom: 8px; flex-wrap: wrap; gap: 8px;">
        <h3 style="margin: 0; font-size: 14px; font-weight: 700;">
          تفصيل الصافي حسب الحسابات (${money(st.closingNet)} ر.س)
        </h3>
        <span class="muted small">مجموع الحسابات الثلاثة يطابق الصافي بدقة: حساب الراتب + كاش جاهز + الذمم والديون الأخرى</span>
      </div>
      <div class="stats net-split-cards" style="margin-bottom: 0;">
        <article class="stat">
          <div class="label">حساب الراتب والمستحقات (Salary Account)</div>
          <strong class="${st.closingSalaryNet > 0 ? 'money-positive' : st.closingSalaryNet < 0 ? 'money-negative' : ''}">${money(st.closingSalaryNet)}</strong>
          <div class="hint">مستحقات عمل معتمدة وعجز الرواتب (ح/ 2110 و1211)</div>
        </article>
        <article class="stat">
          <div class="label">كاش تطبيق جاهز (Jahez Cash)</div>
          <strong class="${st.closingJahezCashNet > 0 ? 'money-positive' : st.closingJahezCashNet < 0 ? 'money-negative' : ''}">${money(st.closingJahezCashNet)}</strong>
          <div class="hint">عهدة كاش جاهز المثبتة (ح/ 1230) — منفصلة عن الراتب</div>
        </article>
        <article class="stat">
          <div class="label">الديون والذمم الأخرى (Other Debts)</div>
          <strong class="${st.closingOtherDebtNet > 0 ? 'money-positive' : st.closingOtherDebtNet < 0 ? 'money-negative' : ''}">${money(st.closingOtherDebtNet)}</strong>
          <div class="hint">باقي ذمم وسلف وخدمات HR المثبتة على المندوب (ح/ 1210)</div>
        </article>
      </div>
    </div>
  `;

  const legacyHtml = (st.legacyEvidenceIncomplete || (st.legacyUnverifiedDebts && st.legacyUnverifiedDebts.length > 0)) ? `
    <div class="notice warn">
      ⚠️ <strong>تنبيه ديون قديمة:</strong> توجد ديون تاريخية تحتاج مطابقة؛ غير مضافة إلى صافي القيود المثبتة
      ${(st.legacyUnverifiedDebts || []).map((d: any) => `(${esc(d.debtId)}: ${money(d.remainingBalance)} ر.س)`).join(' · ')}
    </div>
  ` : '';

  const tableHtml = `
    <div class="panel">
      <div class="panel-title" style="flex-wrap: wrap; gap: 8px;">
        <h2>حركات الأشهر والتفاصيل اليومية</h2>
        <div class="actions no-print">
          <button type="button" class="small ghost" id="simple-statement-expand-all">توسيع كل الأشهر</button>
          <button type="button" class="small ghost" id="simple-statement-collapse-all">طي كل الأشهر</button>
        </div>
      </div>
      <div class="table-wrap">
        <table class="simple-statement-month-table">
          <thead>
            <tr>
              <th>الشهر</th>
              <th>رصيد أول المدة</th>
              <th>اتستحقله</th>
              <th>اتخصم</th>
              <th>استلم</th>
              <th>اتحوّل</th>
              <th>حركة المديونية</th>
              <th>حركة الشهر</th>
              <th>رصيد آخر المدة</th>
              <th class="no-print">التفاصيل</th>
            </tr>
          </thead>
          <tbody>
            ${(st.months || []).map((m: any) => renderSimpleMonthRows(m, st.details || [])).join('')}
          </tbody>
        </table>
      </div>
      ${!(st.months && st.months.length) ? empty('لا توجد بيانات للأشهر المحددة', 'لم تسجل أي حركة في هذا النطاق الزمني.') : ''}
    </div>
  `;

  return styles + filtersHtml + headerHtml + scopeLimitedHtml + statsHtml + netSplitHtml + legacyHtml + tableHtml;
}

function bindSimpleStatementView(): void {
  const rSel = $('#simple-statement-rider-select');
  if (rSel) {
    rSel.onchange = async () => {
      statementSimpleRiderIqama = rSel.value;
      await show('debts');
    };
  }

  const sSel = $('#simple-statement-sponsor-select');
  if (sSel) {
    sSel.onchange = async () => {
      statementSimpleSponsorFilter = sSel.value;
      await show('debts');
    };
  }

  const applyBtn = $('#simple-statement-apply-btn');
  if (applyBtn) {
    applyBtn.onclick = async () => {
      const fromInput = $('#simple-statement-from-month') as HTMLInputElement;
      const toInput = $('#simple-statement-to-month') as HTMLInputElement;
      const fromVal = fromInput ? fromInput.value : '';
      const toVal = toInput ? toInput.value : '';
      if (!fromVal || !toVal) {
        toast('يرجى تحديد شهري البداية والنهاية', true);
        return;
      }
      if (fromVal > toVal) {
        toast('شهر البداية يجب ألا يتجاوز شهر النهاية', true);
        return;
      }
      statementSimpleFromMonth = fromVal;
      statementSimpleToMonth = toVal;
      await show('debts');
    };
  }

  const retryBtn = $('#simple-statement-retry-btn');
  if (retryBtn) {
    retryBtn.onclick = async () => {
      await show('debts');
    };
  }

  const expandAllBtn = $('#simple-statement-expand-all');
  if (expandAllBtn) {
    expandAllBtn.onclick = () => {
      document.querySelectorAll<HTMLElement>('.simple-month-row').forEach(row => {
        const period = row.dataset.monthPeriod;
        if (period) statementSimpleExpandedMonths.add(period);
      });
      document.querySelectorAll<HTMLElement>('.simple-month-details-row').forEach(row => row.classList.remove('hide'));
      document.querySelectorAll<HTMLElement>('.expand-icon').forEach(icon => icon.textContent = '▼');
      document.querySelectorAll<HTMLElement>('.toggle-details-btn').forEach(btn => btn.textContent = 'إخفاء');
    };
  }

  const collapseAllBtn = $('#simple-statement-collapse-all');
  if (collapseAllBtn) {
    collapseAllBtn.onclick = () => {
      statementSimpleExpandedMonths.clear();
      document.querySelectorAll<HTMLElement>('.simple-month-details-row').forEach(row => row.classList.add('hide'));
      document.querySelectorAll<HTMLElement>('.expand-icon').forEach(icon => icon.textContent = '◀');
      document.querySelectorAll<HTMLElement>('.toggle-details-btn').forEach(btn => {
        const period = btn.dataset.period;
        const detailsCount = cache.currentSimpleStatement?.details?.filter((d: any) => d.period === period)?.length || 0;
        btn.textContent = `تفاصيل (${detailsCount})`;
      });
    };
  }

  const printBtn = document.querySelector('[data-action="print-simple-statement"]');
  if (printBtn) {
    (printBtn as HTMLElement).onclick = () => {
      window.print();
    };
  }

  document.querySelectorAll<HTMLElement>('.simple-month-row').forEach(row => {
    row.onclick = () => {
      const period = row.dataset.monthPeriod;
      if (!period) return;
      const detailsRow = document.querySelector<HTMLElement>(`.simple-month-details-row[data-details-for="${period}"]`);
      if (!detailsRow) return;

      const isNowExpanded = detailsRow.classList.contains('hide');
      if (isNowExpanded) {
        detailsRow.classList.remove('hide');
        statementSimpleExpandedMonths.add(period);
      } else {
        detailsRow.classList.add('hide');
        statementSimpleExpandedMonths.delete(period);
      }

      const icon = row.querySelector('.expand-icon');
      if (icon) icon.textContent = isNowExpanded ? '▼' : '◀';

      const btn = row.querySelector('.toggle-details-btn');
      if (btn) {
        if (isNowExpanded) {
          btn.textContent = 'إخفاء';
        } else {
          const detailsCount = cache.currentSimpleStatement?.details?.filter((d: any) => d.period === period)?.length || 0;
          btn.textContent = `تفاصيل (${detailsCount})`;
        }
      }
    };
  });
}

(window as any).renderSimpleStatementView = renderSimpleStatementView;
(window as any).bindSimpleStatementView = bindSimpleStatementView;
