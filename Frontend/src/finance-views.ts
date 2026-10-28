// القوائم والتقارير المالية المعتمدة — إصدار 0.9.0
// شجرة الحسابات المعتمدة، ميزان المراجعة، قائمة الدخل، ملف البنك

// --- الحالة العامة للمالية 0.9.0 ---
let fvCoaData: any = null;
let fvCoaSearch: string = '';
const fvCoaExpanded: Set<string> = new Set<string>();

let fvTrialSponsor: string = '';
let fvTrialFrom: string = '';
let fvTrialTo: string = '';
let fvTrialView: 'current' | 'target' = 'current';
let fvTrialData: any = null;
const fvTrialExpanded: Set<string> = new Set<string>();

let fvIncomeSponsor: string = '';
let fvIncomeFrom: string = '';
let fvIncomeTo: string = '';
let fvIncomeView: 'current' | 'target' = 'target';
let fvIncomeData: any = null;
const fvIncomeExpanded: Set<string> = new Set<string>();

let fvBankSponsor: string = '';
let fvBankPeriod: string = '';
let fvBankPreviewData: any = null;
let fvBankRows: any[] = [];
let fvBankConfirmCode40: boolean = false;
// صفوف كود بنكها غير معروف: البنك يتكتب يدوي (BR §19)
let fvBankManualRows: Set<number> = new Set();

function fvInitDates(): void {
  const currentMonth = new Date().toISOString().slice(0, 7);
  if (!fvTrialFrom) fvTrialFrom = currentMonth;
  if (!fvTrialTo) fvTrialTo = currentMonth;
  if (!fvIncomeFrom) fvIncomeFrom = currentMonth;
  if (!fvIncomeTo) fvIncomeTo = currentMonth;
  if (!fvBankPeriod) fvBankPeriod = currentMonth;
}

// ==========================================
// 1. شجرة الحسابات المعتمدة (Target COA)
// ==========================================

async function renderCoaTargetView(): Promise<string> {
  if (!fvCoaData) {
    try {
      fvCoaData = await api('/finance/coa/target');
      // توسيع المستويات العليا افتراضياً (المستوى 1 و 2)
      if (fvCoaExpanded.size === 0 && fvCoaData?.accounts) {
        fvCoaData.accounts.forEach((a: any) => {
          if (a.level <= 2) fvCoaExpanded.add(a.code);
        });
      }
    } catch (e: any) {
      return `<div class="notice error">تعذر تحميل شجرة الحسابات المعتمدة: ${esc(e.message)}</div>`;
    }
  }

  const accounts: any[] = fvCoaData.accounts || [];
  const mappings: any[] = fvCoaData.mapping || [];

  const totalAccounts = accounts.length;
  const postingAccounts = accounts.filter((a: any) => a.posting).length;
  const headerAccounts = accounts.filter((a: any) => !a.posting).length;

  const mappedTargetCodes = new Set<string>();
  mappings.forEach((m: any) => {
    (m.targetCodes || []).forEach((c: string) => mappedTargetCodes.add(c));
  });
  const newAccountsCount = totalAccounts - mappedTargetCodes.size;

  const parentsSet = new Set<string>();
  accounts.forEach((a: any) => {
    if (a.parent) parentsSet.add(a.parent);
  });

  // معالجة البحث
  const query = fvCoaSearch.trim().toLowerCase();
  const matchingCodes = new Set<string>();
  const accountsByCode = new Map<string, any>();
  accounts.forEach((a: any) => accountsByCode.set(a.code, a));

  if (query) {
    accounts.forEach((a: any) => {
      const codeMatch = (a.code || '').toLowerCase().includes(query);
      const nameMatch = (a.nameAr || '').toLowerCase().includes(query);
      if (codeMatch || nameMatch) {
        matchingCodes.add(a.code);
        // إضافة جميع الآباء في المسار
        let cur = a.parent;
        while (cur) {
          matchingCodes.add(cur);
          cur = accountsByCode.get(cur)?.parent;
        }
      }
    });
  }

  const statsHtml = `
    <div class="stats" style="grid-template-columns: repeat(4, minmax(0, 1fr)); margin-bottom: 20px;">
      <article class="stat">
        <div class="label">إجمالي الحسابات</div>
        <strong>${totalAccounts}</strong>
        <div class="hint">حسابات الشجرة المعتمدة بالكامل</div>
      </article>
      <article class="stat">
        <div class="label">حسابات حركة (ترحيل)</div>
        <strong class="money-positive">${postingAccounts}</strong>
        <div class="hint">حسابات فرعية تقبل الترحيل المباشر</div>
      </article>
      <article class="stat">
        <div class="label">حسابات رئيسية (تجميعية)</div>
        <strong>${headerAccounts}</strong>
        <div class="hint">حسابات تبويب ومستويات رقابية</div>
      </article>
      <article class="stat">
        <div class="label">جديدة vs المربوطة من Excel</div>
        <strong>${newAccountsCount} <span style="font-size: 16px; color: var(--muted); font-weight: 400;">/ ${mappings.length}</span></strong>
        <div class="hint">${newAccountsCount} حساب مستحدث · ${mappings.length} ربط من الشجرة الحالية</div>
      </article>
    </div>
  `;

  const toolbarHtml = `
    <div class="panel" style="margin-bottom: 16px; padding: 14px 18px;">
      <div style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap;">
        <div style="flex: 1; min-width: 240px;">
          <input type="search" id="coa-search-input" placeholder="بحث برقم الحساب أو الاسم بالعربية..." value="${esc(fvCoaSearch)}" style="width: 100%;">
        </div>
        <div class="actions">
          <button type="button" class="secondary small" id="coa-expand-all">توسيع الكل</button>
          <button type="button" class="secondary small" id="coa-collapse-all">طي الكل</button>
        </div>
      </div>
    </div>
  `;

  // تحديد الصفوف المرئية
  const rowsHtml: string[] = [];
  accounts.forEach((a: any) => {
    const hasChildren = parentsSet.has(a.code);
    const isExpanded = fvCoaExpanded.has(a.code);

    let isVisible = true;
    if (query) {
      isVisible = matchingCodes.has(a.code);
    } else {
      let cur = a.parent;
      while (cur) {
        if (!fvCoaExpanded.has(cur)) {
          isVisible = false;
          break;
        }
        cur = accountsByCode.get(cur)?.parent;
      }
    }

    if (!isVisible) return;

    const indentPx = (a.level - 1) * 22;
    const toggleIcon = hasChildren ? (isExpanded ? '▼' : '◀') : '';

    const natureBadge = a.nature === 'debit'
      ? '<span class="tag good">مدين</span>'
      : '<span class="tag warn">دائن</span>';

    const typeBadge = a.type === 'bank'
      ? '<span class="tag">بنك</span>'
      : a.type === 'cash'
        ? '<span class="tag">خزينة</span>'
        : '<span class="tag">أخرى</span>';

    const directionBadge = a.direction === 'pay'
      ? '<span class="tag">صرف</span>'
      : a.direction === 'receive'
        ? '<span class="tag">قبض</span>'
        : a.direction === 'both'
          ? '<span class="tag">الاتنين</span>'
          : '<span class="muted">—</span>';

    const postingBadge = a.posting
      ? '<span class="tag good">حساب حركة</span>'
      : '<span class="tag">رئيسي</span>';

    const statusBadge = a.status === 'active'
      ? '<span class="tag good">فعال</span>'
      : `<span class="tag">${esc(a.status)}</span>`;

    rowsHtml.push(`
      <tr class="coa-row" data-code="${esc(a.code)}" data-parent="${esc(a.parent || '')}" data-level="${a.level}">
        <td style="padding-inline-start: ${indentPx + 8}px; white-space: nowrap;">
          ${hasChildren
            ? `<button type="button" class="ghost small coa-toggle-btn" data-code="${esc(a.code)}" aria-label="طي/توسيع ${esc(a.code)}" style="padding: 2px 6px; margin-inline-end: 4px; font-size: 11px;">${toggleIcon}</button>`
            : '<span style="display: inline-block; width: 22px;"></span>'
          }
          <strong class="number">${esc(a.code)}</strong>
        </td>
        <td>
          <span style="${a.posting ? '' : 'font-weight: 700;'}">${esc(a.nameAr)}</span>
          ${a.company && a.company !== 'الكل' ? `<span class="muted small" style="margin-inline-start: 6px;">(${esc(a.company)})</span>` : ''}
        </td>
        <td>${postingBadge}</td>
        <td>${natureBadge}</td>
        <td>${typeBadge}</td>
        <td>${directionBadge}</td>
        <td class="small muted">${esc(a.company || 'الكل')}</td>
        <td>${statusBadge}</td>
      </tr>
    `);
  });

  const tableHtml = `
    <div class="panel" style="padding: 0; overflow: hidden;">
      <div class="table-wrap" tabindex="0" role="region" aria-label="شجرة الحسابات المعتمدة" style="max-height: calc(100vh - 300px); overflow: auto;">
        <table>
          <thead>
            <tr>
              <th scope="col" style="min-width: 140px;">كود الحساب</th>
              <th scope="col" style="min-width: 220px;">اسم الحساب بالعربية</th>
              <th scope="col">التبويب</th>
              <th scope="col">طبيعة الحساب</th>
              <th scope="col">النوع</th>
              <th scope="col">الاتجاه</th>
              <th scope="col">الجهة / الشركة</th>
              <th scope="col">الحالة</th>
            </tr>
          </thead>
          <tbody>
            ${rowsHtml.length > 0 ? rowsHtml.join('') : '<tr><td colspan="8" class="muted" style="text-align: center; padding: 24px;">لا توجد حسابات تطابق البحث.</td></tr>'}
          </tbody>
        </table>
      </div>
    </div>
  `;

  return statsHtml + toolbarHtml + tableHtml;
}

function bindCoaTargetView(): void {
  const searchInput = document.querySelector<HTMLInputElement>('#coa-search-input');
  if (searchInput) {
    searchInput.oninput = () => {
      fvCoaSearch = searchInput.value;
      if (fvCoaSearch.trim()) {
        // عند البحث نوسع النتائج المطابقة تلقائياً
        fvCoaExpanded.clear();
        const query = fvCoaSearch.trim().toLowerCase();
        (fvCoaData?.accounts || []).forEach((a: any) => {
          if ((a.code || '').toLowerCase().includes(query) || (a.nameAr || '').toLowerCase().includes(query)) {
            fvCoaExpanded.add(a.code);
            let cur = a.parent;
            while (cur) {
              fvCoaExpanded.add(cur);
              const parentAcc = (fvCoaData?.accounts || []).find((x: any) => x.code === cur);
              cur = parentAcc?.parent;
            }
          }
        });
      }
      show('debts');
    };
  }

  const expandAllBtn = document.querySelector<HTMLButtonElement>('#coa-expand-all');
  if (expandAllBtn) {
    expandAllBtn.onclick = () => {
      (fvCoaData?.accounts || []).forEach((a: any) => fvCoaExpanded.add(a.code));
      show('debts');
    };
  }

  const collapseAllBtn = document.querySelector<HTMLButtonElement>('#coa-collapse-all');
  if (collapseAllBtn) {
    collapseAllBtn.onclick = () => {
      fvCoaExpanded.clear();
      show('debts');
    };
  }

  document.querySelectorAll<HTMLButtonElement>('.coa-toggle-btn').forEach(btn => {
    btn.onclick = (e) => {
      e.stopPropagation();
      const code = btn.dataset.code;
      if (!code) return;
      if (fvCoaExpanded.has(code)) {
        fvCoaExpanded.delete(code);
      } else {
        fvCoaExpanded.add(code);
      }
      show('debts');
    };
  });
}

// ==========================================
// 2. ميزان المراجعة (Trial Balance)
// ==========================================

async function renderTrialBalanceView(): Promise<string> {
  fvInitDates();
  const query = `?from=${encodeURIComponent(fvTrialFrom)}&to=${encodeURIComponent(fvTrialTo)}&view=${fvTrialView}${fvTrialSponsor ? `&sponsorId=${encodeURIComponent(fvTrialSponsor)}` : ''}`;

  try {
    fvTrialData = await api(`/finance/trial-balance${query}`);
  } catch (e: any) {
    return `
      <div class="notice error">تعذر تحميل ميزان المراجعة: ${esc(e.message)}</div>
      <button class="secondary" onclick="show('debts')">إعادة المحاولة</button>
    `;
  }

  const companies: any[] = fvTrialData.companies || [];
  const sponsorsCatalog = cache.financeCatalog?.sponsors || [];

  const filtersHtml = `
    <div class="panel" style="margin-bottom: 16px;">
      <div style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap;">
        <label style="margin: 0; min-width: 180px;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">السجل التجاري / الشركة</span>
          <select id="tb-sponsor-select" style="width: 100%;">
            <option value="">كل الشركات المتاحة</option>
            ${options(sponsorsCatalog.map((s: any) => [s.id || s.code, `${s.shortNameAr || s.code} (${s.code})`]), fvTrialSponsor)}
          </select>
        </label>
        <label style="margin: 0;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">من شهر</span>
          <input type="month" id="tb-from-month" value="${esc(fvTrialFrom)}">
        </label>
        <label style="margin: 0;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">إلى شهر</span>
          <input type="month" id="tb-to-month" value="${esc(fvTrialTo)}">
        </label>
        <div style="display: flex; flex-direction: column; gap: 4px;">
          <span class="small muted">عرض الشجرة</span>
          <div class="actions" style="gap: 4px;">
            <button type="button" class="${fvTrialView === 'current' ? 'good' : 'secondary'}" id="tb-view-current">الشجرة الحالية</button>
            <button type="button" class="${fvTrialView === 'target' ? 'good' : 'secondary'}" id="tb-view-target">الشجرة المعتمدة</button>
          </div>
        </div>
        <div style="margin-inline-start: auto; display: flex; gap: 8px; align-items: flex-end;">
          <button type="button" id="tb-refresh-btn">تحديث التقرير</button>
          <button type="button" class="secondary" id="tb-export-excel">📥 تصدير Excel</button>
          <button type="button" class="ghost small" id="tb-expand-all">توسيع الكل</button>
          <button type="button" class="ghost small" id="tb-collapse-all">طي الكل</button>
        </div>
      </div>
    </div>
  `;

  if (companies.length === 0) {
    return filtersHtml + empty('لا توجد بيانات', 'لا توجد حركات مالية مرحلة للشركات والفترة المحددة.');
  }

  const companyBlocks = companies.map((comp: any) => {
    const isBalanced = Boolean(comp.isBalanced);
    const scopeLimited = Boolean(comp.scopeLimited);
    const totals = comp.totals || {};
    const unmapped: any[] = comp.unmapped || [];

    const closingDiff = Math.abs((totals.closingDebit || 0) - (totals.closingCredit || 0));
    const balanceBadge = isBalanced
      ? '<span class="tag good" style="font-size: 13px; font-weight: 700; padding: 5px 12px;">✓ متوازن</span>'
      : `<span class="tag bad" style="font-size: 13px; font-weight: 700; padding: 5px 12px;">✕ غير متوازن (فرق: ${money(closingDiff)} ر.س)</span>`;

    const scopeBanner = scopeLimited ? `
      <div class="notice warn" style="margin-bottom: 14px; border-inline-start: 4px solid #d97706;">
        ⚠️ <strong>تنبيه نطاق الصلاحيات:</strong> تم حجب ${Number(comp.excludedLineCount || 0)} سطر مالي داخل قيود هذه الشركة لعدم شمول صلاحيات المستخدم لحسابات الخزائن الخاصة بها. الأرصدة المعروضة تمثل نطاقك فقط.
      </div>
    ` : '';

    const unmappedPanel = unmapped.length > 0 ? `
      <div class="panel" style="border: 1px solid #f2e2ba; background: #fffdf7; margin-bottom: 16px;">
        <div class="panel-title">
          <h3 style="color: #927132; margin: 0; font-size: 14px;">⚠️ حسابات غير مربوطة بالشجرة المعتمدة (${unmapped.length})</h3>
          <span class="tag warn">مشمولة في الإجمالي لضمان التوازن</span>
        </div>
        <p class="small muted" style="margin: 0 0 10px;">الحسابات أدناه حركات مرحلة لم يتوفر لها سياق كافٍ للربط الآلي، تم إدراج مبالغها في الإجمالي لضمان سلامة التوازن المحاسبي.</p>
        <div class="table-wrap" style="max-height: 220px; overflow: auto;">
          <table>
            <thead>
              <tr>
                <th>كود الحساب الأصلي</th>
                <th>اسم الحساب</th>
                <th>افتتاح مدين</th>
                <th>افتتاح دائن</th>
                <th>حركة مدين</th>
                <th>حركة دائن</th>
                <th>إقفال مدين</th>
                <th>إقفال دائن</th>
              </tr>
            </thead>
            <tbody>
              ${unmapped.map((u: any) => `
                <tr>
                  <td><strong class="number">${esc(u.code)}</strong></td>
                  <td>${esc(u.nameAr || u.name)}</td>
                  <td class="number">${money(u.openingDebit)}</td>
                  <td class="number">${money(u.openingCredit)}</td>
                  <td class="number">${money(u.periodDebit)}</td>
                  <td class="number">${money(u.periodCredit)}</td>
                  <td class="number">${money(u.closingDebit)}</td>
                  <td class="number">${money(u.closingCredit)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    ` : '';

    const rows: any[] = comp.rows || [];
    const rowsByCode = new Map<string, any>();
    const parents = new Set<string>();
    rows.forEach((r: any) => {
      rowsByCode.set(r.code, r);
      if (r.parent) parents.add(r.parent);
    });

    // تجهيز الصفوف القابلة للطي
    const renderedRows: string[] = [];
    rows.forEach((r: any) => {
      const hasChildren = parents.has(r.code);
      const isExpanded = fvTrialExpanded.has(r.code);

      // فحص ظهور الحساب بناءً على الآباء
      let isVisible = true;
      let cur = r.parent;
      while (cur) {
        if (!fvTrialExpanded.has(cur)) {
          isVisible = false;
          break;
        }
        cur = rowsByCode.get(cur)?.parent;
      }

      if (!isVisible) return;

      const level = Number(r.level || 1);
      const indentPx = (level - 1) * 18;
      const toggleIcon = hasChildren ? (isExpanded ? '▼' : '◀') : '';
      const isHeader = !r.posting || hasChildren;

      renderedRows.push(`
        <tr class="tb-row ${isHeader ? 'tb-header-row' : ''}" data-code="${esc(r.code)}" style="${isHeader ? 'background: var(--raised); font-weight: 700;' : ''}">
          <td style="padding-inline-start: ${indentPx + 8}px; white-space: nowrap;">
            ${hasChildren
              ? `<button type="button" class="ghost small tb-toggle-btn" data-code="${esc(r.code)}" aria-label="طي/توسيع ${esc(r.code)}" style="padding: 2px 6px; margin-inline-end: 4px; font-size: 11px;">${toggleIcon}</button>`
              : '<span style="display: inline-block; width: 20px;"></span>'
            }
            <span class="number">${esc(r.code)}</span>
          </td>
          <td>${esc(r.nameAr || r.name)}</td>
          <td class="number">${money(r.openingDebit)}</td>
          <td class="number">${money(r.openingCredit)}</td>
          <td class="number">${money(r.periodDebit)}</td>
          <td class="number">${money(r.periodCredit)}</td>
          <td class="number">${money(r.closingDebit)}</td>
          <td class="number">${money(r.closingCredit)}</td>
        </tr>
      `);
    });

    const tableHtml = `
      <div class="panel" style="padding: 0; overflow: hidden; margin-bottom: 24px;">
        <div style="padding: 14px 18px; border-bottom: 1px solid var(--line); display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px;">
          <div>
            <h3 style="margin: 0 0 4px; font-size: 16px;">ميزان مراجعة: <strong>${esc(comp.companyCode || comp.sponsorId)}</strong></h3>
            <div class="small muted">الفترة: من <span class="number">${esc(fvTrialData.from)}</span> إلى <span class="number">${esc(fvTrialData.to)}</span> · العرض: <strong>${fvTrialView === 'target' ? 'الشجرة المعتمدة' : 'الشجرة الحالية'}</strong></div>
          </div>
          <div>${balanceBadge}</div>
        </div>
        <div class="table-wrap" tabindex="0" role="region" aria-label="جدول ميزان المراجعة" style="max-height: calc(100vh - 280px); overflow: auto;">
          <table class="tb-table" data-company="${esc(comp.companyCode || comp.sponsorId)}">
            <thead>
              <tr>
                <th scope="col" style="min-width: 140px;">كود الحساب</th>
                <th scope="col" style="min-width: 220px;">اسم الحساب</th>
                <th scope="col">افتتاح مدين</th>
                <th scope="col">افتتاح دائن</th>
                <th scope="col">حركة مدين</th>
                <th scope="col">حركة دائن</th>
                <th scope="col">إقفال مدين</th>
                <th scope="col">إقفال دائن</th>
              </tr>
            </thead>
            <tbody>
              ${renderedRows.join('')}
            </tbody>
            <tfoot>
              <tr style="font-weight: 800; background: #e2e8f0; border-top: 2px solid var(--line);">
                <td colspan="2" style="text-align: right; padding: 12px 14px;">الإجمالي العام (Totals)</td>
                <td class="number">${money(totals.openingDebit)}</td>
                <td class="number">${money(totals.openingCredit)}</td>
                <td class="number">${money(totals.periodDebit)}</td>
                <td class="number">${money(totals.periodCredit)}</td>
                <td class="number">${money(totals.closingDebit)}</td>
                <td class="number">${money(totals.closingCredit)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </div>
    `;

    return scopeBanner + unmappedPanel + tableHtml;
  });

  return filtersHtml + companyBlocks.join('');
}

function bindTrialBalanceView(): void {
  const sponsorSelect = document.querySelector<HTMLSelectElement>('#tb-sponsor-select');
  if (sponsorSelect) {
    sponsorSelect.onchange = () => {
      fvTrialSponsor = sponsorSelect.value;
      show('debts');
    };
  }

  const fromMonth = document.querySelector<HTMLInputElement>('#tb-from-month');
  if (fromMonth) {
    fromMonth.onchange = () => {
      fvTrialFrom = fromMonth.value;
    };
  }

  const toMonth = document.querySelector<HTMLInputElement>('#tb-to-month');
  if (toMonth) {
    toMonth.onchange = () => {
      fvTrialTo = toMonth.value;
    };
  }

  const viewCurrentBtn = document.querySelector<HTMLButtonElement>('#tb-view-current');
  if (viewCurrentBtn) {
    viewCurrentBtn.onclick = () => {
      fvTrialView = 'current';
      show('debts');
    };
  }

  const viewTargetBtn = document.querySelector<HTMLButtonElement>('#tb-view-target');
  if (viewTargetBtn) {
    viewTargetBtn.onclick = () => {
      fvTrialView = 'target';
      show('debts');
    };
  }

  const refreshBtn = document.querySelector<HTMLButtonElement>('#tb-refresh-btn');
  if (refreshBtn) {
    refreshBtn.onclick = () => {
      show('debts');
    };
  }

  const expandAllBtn = document.querySelector<HTMLButtonElement>('#tb-expand-all');
  if (expandAllBtn) {
    expandAllBtn.onclick = () => {
      (fvTrialData?.companies || []).forEach((c: any) => {
        (c.rows || []).forEach((r: any) => fvTrialExpanded.add(r.code));
      });
      show('debts');
    };
  }

  const collapseAllBtn = document.querySelector<HTMLButtonElement>('#tb-collapse-all');
  if (collapseAllBtn) {
    collapseAllBtn.onclick = () => {
      fvTrialExpanded.clear();
      show('debts');
    };
  }

  document.querySelectorAll<HTMLButtonElement>('.tb-toggle-btn').forEach(btn => {
    btn.onclick = (e) => {
      e.stopPropagation();
      const code = btn.dataset.code;
      if (!code) return;
      if (fvTrialExpanded.has(code)) {
        fvTrialExpanded.delete(code);
      } else {
        fvTrialExpanded.add(code);
      }
      show('debts');
    };
  });

  const exportExcelBtn = document.querySelector<HTMLButtonElement>('#tb-export-excel');
  if (exportExcelBtn) {
    exportExcelBtn.onclick = async () => {
      exportExcelBtn.disabled = true;
      try {
        const headers = ['كود الحساب', 'اسم الحساب', 'افتتاح مدين', 'افتتاح دائن', 'حركة مدين', 'حركة دائن', 'إقفال مدين', 'إقفال دائن'];
        const rows: (string | number)[][] = [];

        (fvTrialData?.companies || []).forEach((comp: any) => {
          (comp.rows || []).forEach((r: any) => {
            rows.push([
              r.code,
              r.nameAr || r.name,
              Number(r.openingDebit || 0),
              Number(r.openingCredit || 0),
              Number(r.periodDebit || 0),
              Number(r.periodCredit || 0),
              Number(r.closingDebit || 0),
              Number(r.closingCredit || 0)
            ]);
          });
          const totals = comp.totals || {};
          rows.push([
            `الإجمالي (${comp.companyCode || comp.sponsorId})`,
            comp.isBalanced ? 'متوازن' : 'غير متوازن',
            Number(totals.openingDebit || 0),
            Number(totals.openingCredit || 0),
            Number(totals.periodDebit || 0),
            Number(totals.periodCredit || 0),
            Number(totals.closingDebit || 0),
            Number(totals.closingCredit || 0)
          ]);
        });

        await downloadExcel(headers, rows, [2, 3, 4, 5, 6, 7], `trial-balance-${fvTrialFrom}-${fvTrialTo}.xlsx`);
        toast('تم تصدير ميزان المراجعة بنجاح');
      } catch (e: any) {
        toast(e.message || 'تعذر تصدير Excel', true);
      } finally {
        exportExcelBtn.disabled = false;
      }
    };
  }
}

// ==========================================
// 3. قائمة الدخل (Income Statement)
// ==========================================

async function renderIncomeStatementView(): Promise<string> {
  fvInitDates();
  const query = `?from=${encodeURIComponent(fvIncomeFrom)}&to=${encodeURIComponent(fvIncomeTo)}&view=${fvIncomeView}${fvIncomeSponsor ? `&sponsorId=${encodeURIComponent(fvIncomeSponsor)}` : ''}`;

  try {
    fvIncomeData = await api(`/finance/income-statement${query}`);
  } catch (e: any) {
    return `
      <div class="notice error">تعذر تحميل قائمة الدخل: ${esc(e.message)}</div>
      <button class="secondary" onclick="show('debts')">إعادة المحاولة</button>
    `;
  }

  const companies: any[] = fvIncomeData.companies || [];
  const sponsorsCatalog = cache.financeCatalog?.sponsors || [];

  const filtersHtml = `
    <div class="panel" style="margin-bottom: 16px;">
      <div style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap;">
        <label style="margin: 0; min-width: 180px;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">السجل التجاري / الشركة</span>
          <select id="inc-sponsor-select" style="width: 100%;">
            <option value="">كل الشركات المتاحة</option>
            ${options(sponsorsCatalog.map((s: any) => [s.id || s.code, `${s.shortNameAr || s.code} (${s.code})`]), fvIncomeSponsor)}
          </select>
        </label>
        <label style="margin: 0;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">من شهر</span>
          <input type="month" id="inc-from-month" value="${esc(fvIncomeFrom)}">
        </label>
        <label style="margin: 0;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">إلى شهر</span>
          <input type="month" id="inc-to-month" value="${esc(fvIncomeTo)}">
        </label>
        <div style="display: flex; flex-direction: column; gap: 4px;">
          <span class="small muted">عرض الشجرة</span>
          <div class="actions" style="gap: 4px;">
            <button type="button" class="${fvIncomeView === 'target' ? 'good' : 'secondary'}" id="inc-view-target">الشجرة المعتمدة</button>
            <button type="button" class="${fvIncomeView === 'current' ? 'good' : 'secondary'}" id="inc-view-current">الشجرة الحالية</button>
          </div>
        </div>
        <div style="margin-inline-start: auto; display: flex; gap: 8px; align-items: flex-end;">
          <button type="button" id="inc-refresh-btn">تحديث</button>
          <button type="button" class="secondary" id="inc-export-excel">📥 تصدير Excel</button>
          <button type="button" class="ghost small" id="inc-expand-all">توسيع الكل</button>
          <button type="button" class="ghost small" id="inc-collapse-all">طي الكل</button>
        </div>
      </div>
    </div>
  `;

  if (companies.length === 0) {
    return filtersHtml + empty('لا توجد بيانات', 'لا توجد حركات إيرادات أو مصروفات مرحلة للفترة والشركة المحددة.');
  }

  // افتراضياً نوسع البنود إذا لم تكن موسعة
  if (fvIncomeExpanded.size === 0) {
    companies.forEach((c: any) => {
      (c.revenueGroups || []).forEach((g: any) => fvIncomeExpanded.add(g.code));
      (c.expenseGroups || []).forEach((g: any) => fvIncomeExpanded.add(g.code));
    });
  }

  const companyBlocks = companies.map((comp: any) => {
    const revenue = Number(comp.revenue || 0);
    const expense = Number(comp.expense || 0);
    const net = Number(comp.net || 0);
    const netIsComplete = Boolean(comp.netIsComplete);

    const statsHtml = `
      <div class="stats" style="grid-template-columns: repeat(3, minmax(0, 1fr)); margin-bottom: 20px;">
        <article class="stat">
          <div class="label">إجمالي الإيرادات (Revenue)</div>
          <strong class="money-positive">${money(revenue)} <small style="font-size: 13px;">ر.س</small></strong>
          <div class="hint">إجمالي إيرادات النشاط والتشغيل للفترة</div>
        </article>
        <article class="stat">
          <div class="label">إجمالي المصروفات (Expenses)</div>
          <strong style="color: #d97706;">${money(expense)} <small style="font-size: 13px;">ر.س</small></strong>
          <div class="hint">إجمالي تكاليف التشغيل والمصروفات الإدارية</div>
        </article>
        <article class="stat">
          <div class="label">صافي الدخل / (الخسارة) (Net)</div>
          <strong class="${net >= 0 ? 'money-positive' : 'money-negative'}">${money(net)} <small style="font-size: 13px;">ر.س</small></strong>
          <div class="hint">${net >= 0 ? 'صافي أرباح تشغيلية' : 'صافي خسائر تشغيلية'} (${badge(net >= 0 ? 'ربح' : 'خسارة', net >= 0 ? 'good' : 'bad')})</div>
        </article>
      </div>
    `;

    const noticeBanner = !netIsComplete ? `
      <div class="notice warn" style="margin-bottom: 16px; border-inline-start: 4px solid #d97706;">
        ⚠️ <strong>تنبيه اكتمال صافي الدخل:</strong> يوجد نقص في الدليل المالي (حجب نطاق صلاحيات أو بنود لم يتم ربطها بعد). صافي الدخل المعروض جزئي ولا يمثل النتيجة النهائية المطلقة.
      </div>
    ` : '';

    const renderGroupTree = (groups: any[], title: string, isRevenue: boolean) => {
      const groupsByCode = new Map<string, any>();
      const parents = new Set<string>();
      groups.forEach((g: any) => {
        groupsByCode.set(g.code, g);
        if (g.parent) parents.add(g.parent);
      });

      const rows: string[] = [];
      groups.forEach((g: any) => {
        const hasChildren = parents.has(g.code);
        const isExpanded = fvIncomeExpanded.has(g.code);

        let isVisible = true;
        let cur = g.parent;
        while (cur) {
          if (!fvIncomeExpanded.has(cur)) {
            isVisible = false;
            break;
          }
          cur = groupsByCode.get(cur)?.parent;
        }

        if (!isVisible) return;

        const level = g.code.length <= 2 ? 1 : g.code.length <= 4 ? 2 : 3;
        const indentPx = (level - 1) * 20;
        const toggleIcon = hasChildren ? (isExpanded ? '▼' : '◀') : '';
        const isHeader = !g.posting || hasChildren;

        rows.push(`
          <tr class="inc-row" data-code="${esc(g.code)}" style="${isHeader ? 'background: var(--raised); font-weight: 700;' : ''}">
            <td style="padding-inline-start: ${indentPx + 8}px; white-space: nowrap;">
              ${hasChildren
                ? `<button type="button" class="ghost small inc-toggle-btn" data-code="${esc(g.code)}" aria-label="طي/توسيع ${esc(g.code)}" style="padding: 2px 6px; margin-inline-end: 4px; font-size: 11px;">${toggleIcon}</button>`
                : '<span style="display: inline-block; width: 20px;"></span>'
              }
              <span class="number">${esc(g.code)}</span>
            </td>
            <td>${esc(g.nameAr || g.code)}</td>
            <td>${g.posting ? '<span class="tag good">حساب حركة</span>' : '<span class="tag">تجميعي</span>'}</td>
            <td class="number ${isRevenue ? 'money-positive' : ''}" style="font-weight: 700;">${money(g.amount)} ر.س</td>
          </tr>
        `);
      });

      return `
        <div class="panel" style="padding: 0; overflow: hidden; margin-bottom: 18px;">
          <div style="padding: 12px 16px; background: var(--surface); border-bottom: 1px solid var(--line); display: flex; justify-content: space-between; align-items: center;">
            <h4 style="margin: 0; font-size: 15px; font-weight: 700;">${title}</h4>
            <span class="number" style="font-weight: 800; font-size: 15px;">${money(isRevenue ? revenue : expense)} ر.س</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col" style="width: 140px;">الكود</th>
                  <th scope="col">البيان</th>
                  <th scope="col" style="width: 100px;">النوع</th>
                  <th scope="col" style="width: 160px;">المبلغ للفترة</th>
                </tr>
              </thead>
              <tbody>
                ${rows.length > 0 ? rows.join('') : '<tr><td colspan="4" class="muted" style="text-align: center; padding: 16px;">لا توجد بنود مسجلة</td></tr>'}
              </tbody>
            </table>
          </div>
        </div>
      `;
    };

    const revenueSection = renderGroupTree(comp.revenueGroups || [], 'مجموعات الإيرادات (Revenues)', true);
    const expenseSection = renderGroupTree(comp.expenseGroups || [], 'مجموعات المصروفات (Expenses)', false);

    return `
      <div style="margin-bottom: 30px;">
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
          <h3 style="margin: 0; font-size: 17px;">قائمة الدخل: <strong>${esc(comp.companyCode || comp.sponsorId)}</strong></h3>
          <span class="small muted">الفترة: <span class="number">${esc(fvIncomeData.from)}</span> إلى <span class="number">${esc(fvIncomeData.to)}</span></span>
        </div>
        ${statsHtml}
        ${noticeBanner}
        ${revenueSection}
        ${expenseSection}
      </div>
    `;
  });

  return filtersHtml + companyBlocks.join('');
}

function bindIncomeStatementView(): void {
  const sponsorSelect = document.querySelector<HTMLSelectElement>('#inc-sponsor-select');
  if (sponsorSelect) {
    sponsorSelect.onchange = () => {
      fvIncomeSponsor = sponsorSelect.value;
      show('debts');
    };
  }

  const fromMonth = document.querySelector<HTMLInputElement>('#inc-from-month');
  if (fromMonth) {
    fromMonth.onchange = () => {
      fvIncomeFrom = fromMonth.value;
    };
  }

  const toMonth = document.querySelector<HTMLInputElement>('#inc-to-month');
  if (toMonth) {
    toMonth.onchange = () => {
      fvIncomeTo = toMonth.value;
    };
  }

  const viewTargetBtn = document.querySelector<HTMLButtonElement>('#inc-view-target');
  if (viewTargetBtn) {
    viewTargetBtn.onclick = () => {
      fvIncomeView = 'target';
      show('debts');
    };
  }

  const viewCurrentBtn = document.querySelector<HTMLButtonElement>('#inc-view-current');
  if (viewCurrentBtn) {
    viewCurrentBtn.onclick = () => {
      fvIncomeView = 'current';
      show('debts');
    };
  }

  const refreshBtn = document.querySelector<HTMLButtonElement>('#inc-refresh-btn');
  if (refreshBtn) {
    refreshBtn.onclick = () => {
      show('debts');
    };
  }

  const expandAllBtn = document.querySelector<HTMLButtonElement>('#inc-expand-all');
  if (expandAllBtn) {
    expandAllBtn.onclick = () => {
      (fvIncomeData?.companies || []).forEach((c: any) => {
        (c.revenueGroups || []).forEach((g: any) => fvIncomeExpanded.add(g.code));
        (c.expenseGroups || []).forEach((g: any) => fvIncomeExpanded.add(g.code));
      });
      show('debts');
    };
  }

  const collapseAllBtn = document.querySelector<HTMLButtonElement>('#inc-collapse-all');
  if (collapseAllBtn) {
    collapseAllBtn.onclick = () => {
      fvIncomeExpanded.clear();
      show('debts');
    };
  }

  document.querySelectorAll<HTMLButtonElement>('.inc-toggle-btn').forEach(btn => {
    btn.onclick = (e) => {
      e.stopPropagation();
      const code = btn.dataset.code;
      if (!code) return;
      if (fvIncomeExpanded.has(code)) {
        fvIncomeExpanded.delete(code);
      } else {
        fvIncomeExpanded.add(code);
      }
      show('debts');
    };
  });

  const exportExcelBtn = document.querySelector<HTMLButtonElement>('#inc-export-excel');
  if (exportExcelBtn) {
    exportExcelBtn.onclick = async () => {
      exportExcelBtn.disabled = true;
      try {
        const headers = ['النوع', 'الكود', 'البيان', 'المبلغ'];
        const rows: (string | number)[][] = [];

        (fvIncomeData?.companies || []).forEach((comp: any) => {
          (comp.revenueGroups || []).forEach((g: any) => {
            rows.push(['إيراد', g.code, g.nameAr || g.code, Number(g.amount || 0)]);
          });
          rows.push(['إجمالي الإيرادات', comp.companyCode || comp.sponsorId, '', Number(comp.revenue || 0)]);

          (comp.expenseGroups || []).forEach((g: any) => {
            rows.push(['مصروف', g.code, g.nameAr || g.code, Number(g.amount || 0)]);
          });
          rows.push(['إجمالي المصروفات', comp.companyCode || comp.sponsorId, '', Number(comp.expense || 0)]);
          rows.push(['صافي الدخل', comp.companyCode || comp.sponsorId, comp.net >= 0 ? 'صافي ربح' : 'صافي خسارة', Number(comp.net || 0)]);
        });

        await downloadExcel(headers, rows, [3], `income-statement-${fvIncomeFrom}-${fvIncomeTo}.xlsx`);
        toast('تم تصدير قائمة الدخل بنجاح');
      } catch (e: any) {
        toast(e.message || 'تعذر تصدير Excel', true);
      } finally {
        exportExcelBtn.disabled = false;
      }
    };
  }
}

// ==========================================
// 4. ملف البنك (Bank File)
// ==========================================

function bankErrorLabel(err: string): string {
  switch (err) {
    case 'missing_iban': return 'لا يوجد آيبان';
    case 'invalid_iban_mod97': return 'آيبان غير صالح (فحص mod-97)';
    case 'unknown_bank_code': return 'كود بنك غير معروف: اكتب أول 4 حروف من SWIFT';
    case 'bank_mismatch': return 'اختصار البنك لا يطابق الآيبان';
    case 'bank_code_40_manual_confirmation_required': return 'كود 40 يتطلب إقرار يدوي (NCBK)';
    case 'invalid_amount_positive_2dp_required': return 'مبلغ غير موجب أو بدقة أكثر من منزلتين';
    case 'invalid_iqama': return 'رقم هوية أو إقامة غير صالح';
    case 'missing_name': return 'اسم الموظف مفقود';
    default: return err;
  }
}

function bankRowHasEffectiveErrors(row: any, confirmCode40: boolean): boolean {
  if (!row.errors || row.errors.length === 0) return false;
  return row.errors.some((err: string) => {
    if (err === 'bank_code_40_manual_confirmation_required' && confirmCode40) return false;
    return true;
  });
}

async function renderBankFileView(): Promise<string> {
  fvInitDates();
  const sponsorsCatalog = cache.financeCatalog?.sponsors || [];
  if (!fvBankSponsor && sponsorsCatalog.length > 0) {
    fvBankSponsor = sponsorsCatalog[0].id || sponsorsCatalog[0].code;
  }

  const filtersHtml = `
    <div class="panel" style="margin-bottom: 16px;">
      <div style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap;">
        <label style="margin: 0; min-width: 220px;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">السجل التجاري / الشركة *</span>
          <select id="bank-file-sponsor" style="width: 100%;" required>
            ${options(sponsorsCatalog.map((s: any) => [s.id || s.code, `${s.shortNameAr || s.code} (${s.code})`]), fvBankSponsor)}
          </select>
        </label>
        <label style="margin: 0;">
          <span class="small muted" style="display: block; margin-bottom: 4px;">فترة المسير (الشهر) *</span>
          <input type="month" id="bank-file-period" value="${esc(fvBankPeriod)}" required>
        </label>
        <div style="display: flex; gap: 8px; align-items: flex-end; margin-inline-start: auto;">
          <button type="button" id="bank-file-preview-btn">🔍 معاينة ملف البنك</button>
        </div>
      </div>
      <div class="notice" style="margin-top: 12px; margin-bottom: 0;">
        يتم استخراج المناديب المستحقين بناءً على رصيد الالتزام (ح/ 2110) المرحل حتى نهاية الشهر، مع فحص الآيبان المعتمد وخلو السجلات من الأخطاء.
      </div>
    </div>
  `;

  if (!fvBankPreviewData) {
    return filtersHtml + empty('معاينة ملف البنك', 'اختر الشركة والشهر واضغط على «معاينة ملف البنك» لعرض المستحقات وتدقيق الحسابات.');
  }

  const rows = fvBankRows || [];
  const totalAmount = rows.reduce((s: number, r: any) => s + Number(r.amount || 0), 0);
  const rowsWithErrors = rows.filter((r: any) => bankRowHasEffectiveErrors(r, fvBankConfirmCode40));
  const totalErrors = rowsWithErrors.length;
  const canExport = rows.length > 0 && totalErrors === 0;

  const hasCode40 = rows.some((r: any) => (r.errors || []).includes('bank_code_40_manual_confirmation_required'));

  const statsHtml = `
    <div class="stats" style="grid-template-columns: repeat(3, minmax(0, 1fr)); margin-bottom: 18px;">
      <article class="stat">
        <div class="label">عدد السجلات (Rows)</div>
        <strong>${rows.length}</strong>
        <div class="hint">إجمالي المستحقين المختارين للفترة</div>
      </article>
      <article class="stat">
        <div class="label">إجمالي المبلغ (Total Amount)</div>
        <strong class="money-positive">${money(totalAmount)} <small style="font-size: 13px;">ر.س</small></strong>
        <div class="hint">إجمالي المبالغ المراد تحويلها بالبنك</div>
      </article>
      <article class="stat">
        <div class="label">سجلات بها أخطاء تدقيق (Errors)</div>
        <strong class="${totalErrors > 0 ? 'money-negative' : 'money-positive'}">${totalErrors}</strong>
        <div class="hint">${totalErrors > 0 ? 'يجب تصحيحها لتفعيل التصدير' : 'جميع السجلات سليمة ومحققة'}</div>
      </article>
    </div>
  `;

  const code40Notice = hasCode40 ? `
    <div class="notice warn" style="margin-bottom: 14px; border-inline-start: 4px solid #d97706;">
      <label class="check" style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-weight: 700;">
        <input type="checkbox" id="bank-confirm-code-40" ${fvBankConfirmCode40 ? 'checked' : ''}>
        <span>إقرار يدوي لاعتماد حسابات كود 40 (البنك الأهلي السعودي NCBK وفق Business Rules §19)</span>
      </label>
    </div>
  ` : '';

  const tableRowsHtml = rows.map((r: any, idx: number) => {
    const rowErrors = (r.errors || []).filter((err: string) => {
      if (err === 'bank_code_40_manual_confirmation_required' && fvBankConfirmCode40) return false;
      return true;
    });

    const isRowOk = rowErrors.length === 0;
    const errorBadges = isRowOk
      ? '<span class="tag good">سليم</span>'
      : rowErrors.map((err: string) => `<span class="tag bad" style="margin: 2px;">${esc(bankErrorLabel(err))}</span>`).join('');

    return `
      <tr class="bank-row" data-index="${idx}" style="${isRowOk ? '' : 'background: #fff5f5;'}">
        <td class="number">${idx + 1}</td>
        <td><strong>${esc(r.name || '—')}</strong></td>
        <td class="number">${esc(r.iqama)}</td>
        <td>${fvBankManualRows.has(idx)
          ? `<input type="text" maxlength="4" class="bank-code-input" data-index="${idx}" value="${esc(r.bank || '')}" style="width: 70px; text-transform: uppercase;" placeholder="SWIFT">`
          : `<span class="tag">${esc(r.bank || '—')}</span>`}</td>
        <td class="number" style="font-family: monospace; letter-spacing: 0.5px;">${esc(r.iban || '—')}</td>
        <td>
          <input type="number" step="0.01" min="0.01" class="bank-amount-input number" data-index="${idx}" value="${Number(r.amount).toFixed(2)}" style="width: 130px; font-weight: 700; text-align: left;" required>
        </td>
        <td>
          <input type="text" maxlength="2000" class="bank-notes-input" data-index="${idx}" value="${esc(r.notes || '')}" style="width: 220px;" placeholder="ملاحظات الصرف...">
        </td>
        <td class="bank-errors-cell">${errorBadges}</td>
      </tr>
    `;
  }).join('');

  const tableHtml = `
    <div class="panel" style="padding: 0; overflow: hidden; margin-bottom: 24px;">
      <div style="padding: 14px 18px; border-bottom: 1px solid var(--line); display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px;">
        <div>
          <h3 style="margin: 0; font-size: 16px;">معاينة ملف صرف الرواتب والأجور</h3>
          <span class="small muted">الشركة: <strong>${esc(fvBankSponsor)}</strong> · الفترة: <span class="number">${esc(fvBankPeriod)}</span></span>
        </div>
        <div class="actions">
          <button type="button" class="good" id="bank-file-export-btn" ${canExport ? '' : 'disabled'}>
            📥 تصدير ملف البنك (CSV)
          </button>
        </div>
      </div>
      <div class="table-wrap" tabindex="0" role="region" aria-label="جدول معاينة ملف البنك" style="max-height: calc(100vh - 300px); overflow: auto;">
        <table>
          <thead>
            <tr>
              <th scope="col" style="width: 40px;">#</th>
              <th scope="col">اسم الموظف / المندوب</th>
              <th scope="col">رقم الهوية / الإقامة</th>
              <th scope="col">البنك</th>
              <th scope="col">رقم الحساب (الآيبان)</th>
              <th scope="col" style="width: 140px;">إجمالي الراتب (المبلغ)</th>
              <th scope="col" style="width: 230px;">ملاحظات</th>
              <th scope="col">حالة التدقيق</th>
            </tr>
          </thead>
          <tbody>
            ${tableRowsHtml.length > 0 ? tableRowsHtml : '<tr><td colspan="8" class="muted" style="text-align: center; padding: 20px;">لا توجد صفوف مستحقة لهذا الشهر.</td></tr>'}
          </tbody>
        </table>
      </div>
    </div>
  `;

  return filtersHtml + statsHtml + code40Notice + tableHtml;
}

function bindBankFileView(): void {
  const sponsorSelect = document.querySelector<HTMLSelectElement>('#bank-file-sponsor');
  if (sponsorSelect) {
    sponsorSelect.onchange = () => {
      fvBankSponsor = sponsorSelect.value;
    };
  }

  const periodInput = document.querySelector<HTMLInputElement>('#bank-file-period');
  if (periodInput) {
    periodInput.onchange = () => {
      fvBankPeriod = periodInput.value;
    };
  }

  const previewBtn = document.querySelector<HTMLButtonElement>('#bank-file-preview-btn');
  if (previewBtn) {
    previewBtn.onclick = async () => {
      previewBtn.disabled = true;
      try {
        if (!fvBankSponsor) throw new Error('اختر الشركة أولاً');
        if (!fvBankPeriod) throw new Error('اختر فترة المسير');

        const data = await api('/finance/bank-file/preview', {
          sponsorId: fvBankSponsor,
          period: fvBankPeriod
        });

        fvBankPreviewData = data;
        fvBankRows = data.rows || [];
        fvBankManualRows = new Set(fvBankRows.map((r: any, i: number) => (r.errors || []).includes('unknown_bank_code') ? i : -1).filter((i: number) => i >= 0));
        show('debts');
        toast('تم تحميل معاينة ملف البنك');
      } catch (e: any) {
        toast(e.message || 'تعذر إجراء معاينة ملف البنك', true);
      } finally {
        previewBtn.disabled = false;
      }
    };
  }

  const confirmCode40Checkbox = document.querySelector<HTMLInputElement>('#bank-confirm-code-40');
  if (confirmCode40Checkbox) {
    confirmCode40Checkbox.onchange = () => {
      fvBankConfirmCode40 = confirmCode40Checkbox.checked;
      show('debts');
    };
  }

  // ربط حقول التعديل: المبلغ والملاحظات
  document.querySelectorAll<HTMLInputElement>('.bank-amount-input').forEach(input => {
    input.oninput = () => {
      const idx = Number(input.dataset.index);
      const val = parseFloat(input.value);
      if (isNaN(val) || val <= 0) {
        if (!fvBankRows[idx].errors) fvBankRows[idx].errors = [];
        if (!fvBankRows[idx].errors.includes('invalid_amount_positive_2dp_required')) {
          fvBankRows[idx].errors.push('invalid_amount_positive_2dp_required');
        }
      } else {
        fvBankRows[idx].amount = Math.round(val * 100) / 100;
        if (fvBankRows[idx].errors) {
          fvBankRows[idx].errors = fvBankRows[idx].errors.filter((e: string) => e !== 'invalid_amount_positive_2dp_required');
        }
      }
      // تحديث حالة زر التصدير والإحصائيات
      updateBankFileExportState();
    };
  });

  document.querySelectorAll<HTMLInputElement>('.bank-code-input').forEach(input => {
    input.oninput = () => {
      const idx = Number(input.dataset.index);
      const code = input.value.trim().toUpperCase();
      fvBankRows[idx].bank = code;
      const errors = (fvBankRows[idx].errors || []).filter((e: string) => e !== 'unknown_bank_code');
      if (!/^[A-Z]{4}$/.test(code)) errors.push('unknown_bank_code');
      fvBankRows[idx].errors = errors;
      updateBankFileExportState();
    };
  });

  document.querySelectorAll<HTMLInputElement>('.bank-notes-input').forEach(input => {
    input.oninput = () => {
      const idx = Number(input.dataset.index);
      fvBankRows[idx].notes = input.value;
    };
  });

  const exportBtn = document.querySelector<HTMLButtonElement>('#bank-file-export-btn');
  if (exportBtn) {
    exportBtn.onclick = async () => {
      exportBtn.disabled = true;
      try {
        if (!fvBankPreviewData?.previewToken) {
          throw new Error('انتهت صلاحية المعاينة؛ يرجى إعادة المعاينة أولاً');
        }

        const payload = {
          sponsorId: fvBankSponsor,
          period: fvBankPeriod,
          previewToken: fvBankPreviewData.previewToken,
          confirmBankCode40: fvBankConfirmCode40,
          rows: fvBankRows
        };

        // تنزيل ملف CSV المرتجع من الخادم مباشرة كما هو دون إعادة بنائه
        const res = await fetch('/api/finance/bank-file/export', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(payload)
        });

        if (!res.ok) {
          const errData = await res.json().catch(() => ({}));
          throw new Error(errData.error || 'تعذر تصدير ملف البنك من الخادم');
        }

        const blob = await res.blob();
        let filename = `${Date.now()}_bulkPayroll.csv`;
        const disposition = res.headers.get('content-disposition');
        if (disposition && disposition.includes('filename=')) {
          const match = disposition.match(/filename="?([^";]+)"?/);
          if (match && match[1]) filename = match[1];
        }

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);

        toast('تم تصدير ملف البنك بنجاح بصيغة CSV');
      } catch (e: any) {
        toast(e.message || 'تعذر تصدير ملف البنك', true);
      } finally {
        updateBankFileExportState();
      }
    };
  }
}

function updateBankFileExportState(): void {
  const exportBtn = document.querySelector<HTMLButtonElement>('#bank-file-export-btn');
  if (!exportBtn) return;
  const totalErrors = fvBankRows.filter((r: any) => bankRowHasEffectiveErrors(r, fvBankConfirmCode40)).length;
  exportBtn.disabled = fvBankRows.length === 0 || totalErrors > 0;
}

// ==========================================
// الواجهة الرئيسية الموحدة للتبويبات
// ==========================================

async function renderFinanceViewsTab(subTab: string): Promise<string> {
  const styleBlock = `
    <style id="finance-views-styles">
      .tb-header-row td {
        border-top: 1px solid var(--line);
        border-bottom: 1px solid var(--line);
      }
      .money-positive {
        color: #0f766e;
      }
      .money-negative {
        color: #b91c1c;
      }
      [data-theme="dark"] .money-positive {
        color: #2dd4bf;
      }
      [data-theme="dark"] .money-negative {
        color: #f87171;
      }
      @media (max-width: 768px) {
        .stats {
          grid-template-columns: 1fr !important;
        }
      }
    </style>
  `;

  let content = '';
  switch (subTab) {
    case 'coa-target':
      content = await renderCoaTargetView();
      break;
    case 'trial-balance':
      content = await renderTrialBalanceView();
      break;
    case 'income-statement':
      content = await renderIncomeStatementView();
      break;
    case 'bank-file':
      content = await renderBankFileView();
      break;
    default:
      content = empty('تبويب غير معروف', 'التبويب المطلوب غير متاح في النظام المالي.');
      break;
  }

  return styleBlock + content;
}

function bindFinanceViewsTab(subTab: string): void {
  switch (subTab) {
    case 'coa-target':
      bindCoaTargetView();
      break;
    case 'trial-balance':
      bindTrialBalanceView();
      break;
    case 'income-statement':
      bindIncomeStatementView();
      break;
    case 'bank-file':
      bindBankFileView();
      break;
  }
}

// إتاحة الدوال على كائن window للتكامل السلس
(window as any).renderFinanceViewsTab = renderFinanceViewsTab;
(window as any).bindFinanceViewsTab = bindFinanceViewsTab;
