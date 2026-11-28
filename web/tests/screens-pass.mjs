// G3 Full Screens Pass: visits every sidebar page, tab, sub-tab, and dialog in Arabic RTL.
// Starts Payroll.Api against PAYROLL_TEST_SQL_CONNECTION (throwaway SQL Server in Docker) or isolated SQLite,
// seeds synthetic fixtures via API only (zero real data), and records all UI/console/network issues.

import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:net';
import { mkdtempSync, rmSync, cpSync, mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { randomBytes } from 'node:crypto';
import { crc32 } from 'node:zlib';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(webDir, '..');

const pwModulePath =
  process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwModulePath));

const SHOTS_DIR = process.env.G3_SHOTS_DIR || '/home/ghazal/.cache/agents-runs/agy-G3-shots';
const JSON_OUT = process.env.G3_JSON_OUT || '/home/ghazal/.cache/agents-runs/agy-G3-results.json';
mkdirSync(SHOTS_DIR, { recursive: true });

function findChromiumExecutable() {
  if (process.env.PLAYWRIGHT_EXECUTABLE_PATH) return process.env.PLAYWRIGHT_EXECUTABLE_PATH;
  const home = process.env.HOME || '/home/ghazal';
  const candidates = [
    path.join(home, '.cache/ms-playwright/chromium-1254/chrome-linux64/chrome'),
    path.join(home, '.cache/ms-playwright/chromium_headless_shell-1254/chrome-headless-shell-linux64/chrome-headless-shell'),
  ];
  for (const c of candidates) {
    if (existsSync(c)) return c;
  }
  return undefined;
}

async function freePort() {
  const srv = createServer();
  await new Promise((resolve, reject) => {
    srv.once('error', reject);
    srv.listen(0, '127.0.0.1', resolve);
  });
  const { port } = srv.address();
  await new Promise((resolve) => srv.close(resolve));
  return port;
}

function buildZipXlsx(sheets) {
  const col = (i) => {
    let r = '';
    for (let n = i + 1; n > 0; n = Math.floor((n - 1) / 26)) {
      r = String.fromCharCode(65 + ((n - 1) % 26)) + r;
    }
    return r;
  };
  const esc = (s) =>
    String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
  const rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
  const files = {
    'xl/workbook.xml': `<workbook xmlns="${ns}" xmlns:r="${rel}"><sheets>${sheets
      .map((s, idx) => `<sheet name="${esc(s.name)}" r:id="r${idx + 1}"/>`)
      .join('')}</sheets></workbook>`,
    'xl/_rels/workbook.xml.rels': `<Relationships>${sheets
      .map((_, idx) => `<Relationship Id="r${idx + 1}" Target="worksheets/sheet${idx + 1}.xml"/>`)
      .join('')}</Relationships>`,
  };
  sheets.forEach((s, idx) => {
    const body = s.rows
      .map(
        (cells, rIdx) =>
          `<row r="${rIdx + 1}">${cells
            .map((v, cIdx) => `<c r="${col(cIdx)}${rIdx + 1}" t="inlineStr"><is><t>${esc(v)}</t></is></c>`)
            .join('')}</row>`
      )
      .join('');
    files[`xl/worksheets/sheet${idx + 1}.xml`] = `<worksheet xmlns="${ns}"><sheetData>${body}</sheetData></worksheet>`;
  });

  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text] of Object.entries(files)) {
    const data = Buffer.from(text);
    const nameBytes = Buffer.from(name);
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += 30 + nameBytes.length + data.length;
  }
  const dir = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  const count = Object.keys(files).length;
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(count, 8);
  end.writeUInt16LE(count, 10);
  end.writeUInt32LE(dir.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, dir, end]);
}

function syntheticNinjaWorkbook() {
  const header = [
    'supplier_id', 'supplier_name', 'CAPTAIN ACTIVE STATUS', 'capabilty', 'captain_id',
    'Zone Name', 'First Attended Date', 'Total Delivered Orders', 'Contract Amount', 'Contract Target',
    'Count of First Attended Date to End of Month', 'Adjusted Target', 'Adjusted Amount', 'Below Target Percentage',
    'Target Level Type', 'Target Deductions Amount', 'Target Bonus Amount', 'Target Total Amount',
    'Count of Attended Days', 'Attendance Deduction', 'Count of Days Less than 8 Hr', '8 Hour Deductions',
    'Deduction refund', 'Gasoline Support', 'Weekend 40 Bonus', 'Weekend 42 Bonus', 'Weekend 43 Bonus',
    '100 SAR BONUS', 'Supplier Performance Bonus', 'NAQIL BLOCKED', 'Penalty', 'on demand',
    'STOPS WITHOUT NOTICE DEDUCTION', 'Final Amount', 'VAT', 'TOTAL COST',
  ];
  const row1 = {
    supplier_id: '9001',
    capabilty: 'capability_car',
    captain_id: 'SYN-NJ-001',
    'Total Delivered Orders': '420',
    'Contract Amount': '6500',
    'Contract Target': '460',
    'Count of First Attended Date to End of Month': '31',
    'Adjusted Target': '460',
    'Adjusted Amount': '6500',
    'Target Total Amount': '6500',
    'Count of Attended Days': '30',
    Penalty: '-150',
    'Final Amount': '6350',
    VAT: '952.5',
    'TOTAL COST': '7302.5',
  };
  return buildZipXlsx([
    {
      name: 'NinjaSynthetic',
      rows: [header, header.map((h) => row1[h] ?? '0')],
    },
  ]);
}

function syntheticKeetaWorkbook() {
  const header = ['المعرف', 'الطلبات المُسلمة', 'الخصم', 'الإجمالي', 'إجمالي المبلغ المستحق'];
  const row1 = ['SYN-KT-001', '210', '-120', '4200', '4830'];
  return buildZipXlsx([{ name: 'KeetaSynthetic', rows: [header, row1] }]);
}

async function seedSyntheticData(baseUrl, cookie) {
  const headers = (extra = {}) => ({ Cookie: cookie, ...extra });
  const apiCall = async (route, body, method) => {
    const isForm = body instanceof FormData;
    const res = await fetch(`${baseUrl}${route}`, {
      method: method || (body === undefined ? 'GET' : 'POST'),
      headers: headers(body === undefined || isForm ? {} : { 'Content-Type': 'application/json' }),
      body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    });
    const text = await res.text();
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {}
    return { status: res.status, json, text };
  };

  const companiesRes = await apiCall('/api/companies');
  const companies = Array.isArray(companiesRes.json) ? companiesRes.json : [];
  const companyId = companies[0]?.id || 'BW';

  // Secondary synthetic user (checker)
  const secondPw = randomBytes(20).toString('hex');
  await apiCall('/api/users', {
    username: 'qa-checker',
    password: secondPw,
    displayNameAr: 'مراجع تجريبي',
    role: 'FinancialAccountant',
    customPermissions: ['approvals.view', 'approvals.decide', 'finance.journal.approve'],
    department: 'FIN',
    rankLevel: 5,
  });

  // Synthetic opening balances
  const openRows = ['BW', 'EX', 'TR'].flatMap((code) =>
    [`MAIN.${code}.DEBT`, `BANK.${code}`].map((accountCode) => ({
      accountCode,
      sponsorCode: code,
      iqama: '',
      verifiedBalance: 50000,
      sourceRef: `synthetic-g3:${accountCode}`,
      notes: 'Synthetic G3 cash fixture',
    }))
  );
  const openPrev = await apiCall('/api/finance/opening/preview', { rows: openRows });
  if (openPrev.status === 200 && openPrev.json?.fingerprint) {
    await apiCall('/api/finance/opening/commit', {
      rows: openPrev.json.rows,
      fingerprint: openPrev.json.fingerprint,
      confirm: true,
    });
  }

  // Synthetic riders
  const riders = [
    { iqama: '2000000101', name: 'أحمد التجريبي الأول', phone: '+966500000101', city: 'جدة', financeSponsorId: companyId, status: 'Active', engagementType: 'SponsoredInternal', platform: 'ninja' },
    { iqama: '2000000102', name: 'سالم التجريبي الثاني', phone: '+966500000102', city: 'الرياض', financeSponsorId: companyId, status: 'Active', engagementType: 'SponsoredInternal', platform: 'keeta' },
    { iqama: '2000000103', name: 'فهد الإداري التجريبي', phone: '+966500000103', city: 'الدمام', financeSponsorId: companyId, status: 'OnLeave', engagementType: 'External', platform: 'jahez' },
  ];
  for (const r of riders) {
    await apiCall('/api/riders', r);
  }

  // Synthetic work rows (2026-10)
  await apiCall('/api/test/seed-work', {
    rows: [
      { period: '2026-10', platform: 'Keeta', iqama: '2000000102', name: 'سالم التجريبي الثاني', accountId: 'SYN-KT-001', from: '2026-10-01', to: '2026-10-31', orders: 210, platformRevenue: 4200, company: companyId, city: 'الرياض' },
      { period: '2026-10', platform: 'Ninja', iqama: '2000000101', name: 'أحمد التجريبي الأول', accountId: 'SYN-NJ-001', from: '2026-10-01', to: '2026-10-31', orders: 420, platformRevenue: 6350, company: companyId, city: 'جدة' },
    ],
  });

  // Synthetic Ninja invoice
  const njForm = (prev) => {
    const f = new FormData();
    f.set('file', new Blob([syntheticNinjaWorkbook()]), 'synthetic-ninja.xlsx');
    f.set('companyId', companyId);
    f.set('period', '2026-10');
    f.set('reference', 'SYN-G3-NINJA');
    if (prev) {
      f.set('rows', JSON.stringify(prev.rows));
      f.set('fingerprint', prev.fingerprint);
      f.set('confirm', 'true');
    }
    return f;
  };
  const njPrev = await apiCall('/api/platforms/ninja/invoices', njForm());
  if (njPrev.status === 200 && njPrev.json?.canCommit) {
    await apiCall('/api/platforms/ninja/invoices?commit=1', njForm(njPrev.json));
  }

  // Synthetic Keeta invoice
  const ktForm = (prev) => {
    const f = new FormData();
    f.set('file', new Blob([syntheticKeetaWorkbook()]), 'synthetic-keeta.xlsx');
    f.set('companyId', companyId);
    f.set('period', '2026-10');
    f.set('reference', 'SYN-G3-KEETA');
    if (prev) {
      f.set('rows', JSON.stringify(prev.rows));
      f.set('fingerprint', prev.fingerprint);
      f.set('confirm', 'true');
    }
    return f;
  };
  const ktPrev = await apiCall('/api/platforms/keeta/invoices', ktForm());
  if (ktPrev.status === 200 && ktPrev.json?.canCommit) {
    await apiCall('/api/platforms/keeta/invoices?commit=1', ktForm(ktPrev.json));
  }

  // Synthetic campaign + public submission
  const camp = await apiCall('/api/campaigns', { title: 'حملة تجريبية G3', days: 5 });
  if (camp.status === 200 && camp.json?.path) {
    const token = camp.json.path.split('/').at(-1);
    await fetch(`${baseUrl}/api/public/forms/${token}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ iqama: '2000000101', name: 'أحمد التجريبي الأول', workSystem: 'tiers' }),
    });
  }

  // Synthetic custom form
  await apiCall('/api/forms/builder', {
    titleAr: 'استبيان تجريبي G3',
    expiresAt: new Date(Date.now() + 7 * 864e5).toISOString(),
    maxSubmissions: 50,
    fields: [{ id: 'f1', labelAr: 'ملاحظات التشغيل', type: 'text', required: true }],
  });

  // Synthetic manual journal
  await apiCall('/api/finance/journals/manual', {
    companyId,
    date: '2026-10-15',
    reference: 'SYN-G3-JV',
    description: 'قيد تجريبي G3',
    lines: [
      { accountCode: '512101', debit: 150, credit: 0 },
      { accountCode: '212103', debit: 0, credit: 150 },
    ],
  });

  // Synthetic debt
  await apiCall('/api/debts', {
    iqama: '2000000101',
    category: 'CashAdvance',
    totalAmount: 1200,
    monthlyInstallment: 300,
    creditorSponsorId: companyId,
    originSponsorId: companyId,
    notes: 'مديونية تجريبية G3',
  });

  // Synthetic payment supplier + quick payment
  await apiCall('/api/payments/suppliers', {
    nameAr: 'مورد تجريبي G3',
    code: 'SYN-SUP-1',
    taxNumber: '300000000000003',
  });
}

// Exhaustive route/tab/sub-tab inventory matching Sidebar.tsx + page-tabs.ts + sub-tabs
const SCREENS = [
  // Home
  { id: 'home-dashboard', label: 'الرئيسية / لوحة المؤشرات والمهام', url: '/app/?period=2026-10', sidebarId: 'dash-kpi' },
  { id: 'workforce-directory', label: 'القوى العاملة / الدليل', url: '/app/workforce/?tab=directory&period=2026-10', sidebarId: 'workforce' },
  { id: 'workforce-360', label: 'القوى العاملة / الملف 360', url: '/app/workforce/?tab=360&iqama=2000000101&period=2026-10' },
  { id: 'workforce-quality', label: 'القوى العاملة / جودة البيانات', url: '/app/workforce/?tab=quality&period=2026-10' },
  { id: 'workforce-saudi', label: 'القوى العاملة / رواتب سعودية', url: '/app/workforce/?tab=saudi&period=2026-10' },
  { id: 'workforce-cases', label: 'القوى العاملة / الإنهاء والغياب', url: '/app/workforce/?tab=cases&period=2026-10' },
  { id: 'workforce-legal', label: 'القوى العاملة / القضايا', url: '/app/workforce/?tab=legal&period=2026-10' },
  { id: 'companies-list', label: 'الشركات والسجلات / الشركات والكفلاء', url: '/app/companies/?tab=companies&period=2026-10', sidebarId: 'companies' },
  { id: 'companies-cr', label: 'الشركات والسجلات / السجلات التجارية', url: '/app/companies/?tab=cr&period=2026-10' },
  { id: 'companies-expiring', label: 'الشركات والسجلات / وثائق تنتهي قريبًا', url: '/app/companies/?tab=expiring&period=2026-10' },
  { id: 'companies-stats', label: 'الشركات والسجلات / الإحصائيات والمؤشرات', url: '/app/companies/?tab=stats&period=2026-10' },
  { id: 'hr-job-titles', label: 'كتالوجات الموظفين / المسميات الوظيفية', url: '/app/hr/catalogs/?tab=job-titles&period=2026-10', sidebarId: 'hr-catalogs' },
  { id: 'hr-work-types', label: 'كتالوجات الموظفين / أنواع العمل', url: '/app/hr/catalogs/?tab=work-types&period=2026-10' },
  { id: 'hr-iqama-prof', label: 'كتالوجات الموظفين / مهن الإقامة', url: '/app/hr/catalogs/?tab=iqama-professions&period=2026-10' },
  { id: 'hr-licence', label: 'كتالوجات الموظفين / فئات الرخص', url: '/app/hr/catalogs/?tab=licence-categories&period=2026-10' },
  { id: 'hr-sponsors', label: 'كتالوجات الموظفين / الكفلاء', url: '/app/hr/catalogs/?tab=sponsors&period=2026-10' },
  { id: 'hr-city-settings', label: 'كتالوجات الموظفين / إعدادات المدن', url: '/app/hr/catalogs/?tab=city-settings&period=2026-10' },
  { id: 'rider-statement', label: 'كشف حساب المندوب', url: '/app/riders/statement/?iqama=2000000101&period=2026-10', sidebarId: 'rider-statement' },
  { id: 'notifications-all', label: 'الإشعارات / الكل', url: '/app/notifications/?period=2026-10' },
  { id: 'notifications-general', label: 'الإشعارات / عام', url: '/app/notifications/?period=2026-10', clickTab: 'عام' },
  { id: 'notifications-personal', label: 'الإشعارات / مخصص', url: '/app/notifications/?period=2026-10', clickTab: 'مخصص' },
  { id: 'notifications-tasks', label: 'الإشعارات / طلبات ومهام', url: '/app/notifications/?period=2026-10', clickTab: 'طلبات ومهام' },

  // Payments
  { id: 'pay-rents-contracts', label: 'المدفوعات / الإيجارات - العقود', url: '/app/payments/rents/?period=2026-10', sidebarId: 'pay-rents', clickTab: 'العقود' },
  { id: 'pay-rents-schedule', label: 'المدفوعات / الإيجارات - الجدول', url: '/app/payments/rents/?period=2026-10', clickTab: 'جدول الدفعات والاستحقاق' },
  { id: 'pay-rents-props', label: 'المدفوعات / الإيجارات - العقارات', url: '/app/payments/rents/?period=2026-10', clickTab: 'العقارات والمكاتب' },
  { id: 'pay-inst-contracts', label: 'المدفوعات / الأقساط - عقود التمويل', url: '/app/payments/installments/?period=2026-10', sidebarId: 'pay-installments', clickTab: 'عقود التمويل' },
  { id: 'pay-inst-sched', label: 'المدفوعات / الأقساط - جدول الأقساط', url: '/app/payments/installments/?period=2026-10', clickTab: 'جدول الأقساط' },
  { id: 'pay-inst-accidents', label: 'المدفوعات / الأقساط - الحوادث', url: '/app/payments/installments/?period=2026-10', clickTab: 'أقساط فترات الحوادث' },
  { id: 'pay-inst-financiers', label: 'المدفوعات / الأقساط - الممولون', url: '/app/payments/installments/?period=2026-10', clickTab: 'لوحة الممولين' },
  { id: 'pay-purch-erp', label: 'المدفوعات / المشتريات - استلامات الصيانة', url: '/app/payments/purchases/?period=2026-10', sidebarId: 'pay-purchases', clickTab: 'استلامات الصيانة' },
  { id: 'pay-purch-exp', label: 'المدفوعات / المشتريات - فواتير ومصروفات', url: '/app/payments/purchases/?period=2026-10', clickTab: 'فواتير ومصروفات أخرى' },
  { id: 'pay-purch-petty', label: 'المدفوعات / المشتريات - العهد النثرية', url: '/app/payments/purchases/?period=2026-10', clickTab: 'العهد النثرية' },
  { id: 'pay-fuel-wallets', label: 'المدفوعات / الوقود - المحافظ', url: '/app/payments/fuel/?period=2026-10', sidebarId: 'pay-fuel', clickTab: 'محافظ الوقود والتمويل' },
  { id: 'pay-fuel-mov', label: 'المدفوعات / الوقود - حركات التمويل', url: '/app/payments/fuel/?period=2026-10', clickTab: 'حركات التمويل' },
  { id: 'pay-fuel-rec', label: 'المدفوعات / الوقود - المطابقة الشهرية', url: '/app/payments/fuel/?period=2026-10', clickTab: 'المطابقة الشهرية' },
  { id: 'pay-fuel-cons', label: 'المدفوعات / الوقود - الاستهلاك مقابل السقف', url: '/app/payments/fuel/?period=2026-10', clickTab: 'الاستهلاك مقابل السقف' },
  { id: 'pay-fuel-prov', label: 'المدفوعات / الوقود - إعدادات المزودين', url: '/app/payments/fuel/?period=2026-10', clickTab: 'إعدادات المزودين' },
  { id: 'pay-hr-req', label: 'المدفوعات / طلبات الموارد - المفتوحة', url: '/app/payments/hr-requests/?period=2026-10', sidebarId: 'pay-hr-requests', clickTab: 'الطلبات المفتوحة' },
  { id: 'pay-hr-paid', label: 'المدفوعات / طلبات الموارد - المدفوعة', url: '/app/payments/hr-requests/?period=2026-10', clickTab: 'المعتمدة والمدفوعة' },
  { id: 'pay-hr-box', label: 'المدفوعات / طلبات الموارد - صندوق الشريك', url: '/app/payments/hr-requests/?period=2026-10', clickTab: 'صندوق الشريك' },
  { id: 'pay-adv-req', label: 'المدفوعات / السلف والشحن - الطلبات', url: '/app/payments/advances/?period=2026-10', sidebarId: 'pay-advances', clickTab: 'طلبات السلف' },
  { id: 'pay-adv-payouts', label: 'المدفوعات / السلف والشحن - الصرف', url: '/app/payments/advances/?period=2026-10', clickTab: 'صرف السلف المعتمدة' },
  { id: 'pay-adv-hs', label: 'المدفوعات / السلف والشحن - شحن هنقر', url: '/app/payments/advances/?period=2026-10', clickTab: 'طلبات شحن محفظة هنقرستيشن' },
  { id: 'pay-adv-asks', label: 'المدفوعات / السلف والشحن - طلبات المناديب', url: '/app/payments/advances/?period=2026-10', clickTab: 'طلبات المناديب من التطبيق' },
  { id: 'pay-adv-cat', label: 'المدفوعات / السلف والشحن - الشبكات', url: '/app/payments/advances/?period=2026-10', clickTab: 'شبكات وباقات الشحن' },
  { id: 'pay-mada-bal', label: 'المدفوعات / مدا - الرصيد', url: '/app/payments/mada/?period=2026-10', sidebarId: 'pay-mada', clickTab: 'رصيد محفظة مدا' },
  { id: 'pay-mada-topups', label: 'المدفوعات / مدا - الشحن', url: '/app/payments/mada/?period=2026-10', clickTab: 'عمليات شحن مدا' },
  { id: 'pay-mada-cards', label: 'المدفوعات / مدا - سلف الإنترنت', url: '/app/payments/mada/?period=2026-10', clickTab: 'سلف الإنترنت وبطاقات الشحن' },
  { id: 'pay-mada-settings', label: 'المدفوعات / مدا - الإعدادات', url: '/app/payments/mada/?period=2026-10', clickTab: 'إعدادات الشبكات والباقات' },
  { id: 'pay-hs-events', label: 'المدفوعات / عهد هنقر - الأحداث', url: '/app/payments/hs-custody/?period=2026-10', sidebarId: 'pay-hs-custody', clickTab: 'أحداث العهدة والشحن' },
  { id: 'pay-hs-sup', label: 'المدفوعات / عهد هنقر - المشرفون', url: '/app/payments/hs-custody/?period=2026-10', clickTab: 'ملخص حسب المشرف' },
  { id: 'pay-hs-rec', label: 'المدفوعات / عهد هنقر - التوازن', url: '/app/payments/hs-custody/?period=2026-10', clickTab: 'فحص التوازن وإغلاق الشهر' },
  { id: 'pay-traffic-open', label: 'المدفوعات / المخالفات المرورية - للسداد', url: '/app/payments/traffic-violations/?period=2026-10', sidebarId: 'pay-traffic', clickTab: 'مخالفات للسداد' },
  { id: 'pay-traffic-paid', label: 'المدفوعات / المخالفات المرورية - المدفوعة', url: '/app/payments/traffic-violations/?period=2026-10', clickTab: 'السجل والمدفوعة' },
  { id: 'pay-sup-list', label: 'المدفوعات / الموردون - القائمة', url: '/app/payments/suppliers/?period=2026-10', sidebarId: 'pay-suppliers', clickTab: 'قائمة الموردين' },
  { id: 'pay-sup-queue', label: 'المدفوعات / الموردون - الطابور', url: '/app/payments/suppliers/?period=2026-10', clickTab: 'طابور طلبات الدفع' },
  { id: 'pay-sup-vouchers', label: 'المدفوعات / الموردون - سندات الصرف', url: '/app/payments/suppliers/?period=2026-10', clickTab: 'سندات الصرف' },
  { id: 'pay-due-week', label: 'المدفوعات / المستحقات - الأسبوع', url: '/app/payments/due-calendar/?period=2026-10', sidebarId: 'pay-due-calendar', clickTab: 'مستحق هذا الأسبوع' },
  { id: 'pay-due-month', label: 'المدفوعات / المستحقات - الشهر', url: '/app/payments/due-calendar/?period=2026-10', clickTab: 'مستحق هذا الشهر' },
  { id: 'pay-due-cal', label: 'المدفوعات / المستحقات - التقويم', url: '/app/payments/due-calendar/?period=2026-10', clickTab: 'التقويم الشهري' },
  { id: 'pay-gov-vat', label: 'المدفوعات / الحكومية - ضريبة القيمة المضافة', url: '/app/payments/government/?period=2026-10', sidebarId: 'pay-government', clickTab: 'إقرارات ضريبة القيمة المضافة' },
  { id: 'pay-gov-zakat', label: 'المدفوعات / الحكومية - الزكاة', url: '/app/payments/government/?period=2026-10', clickTab: 'الزكاة وهيئة الزكاة' },
  { id: 'pay-gov-sadad', label: 'المدفوعات / الحكومية - سداد', url: '/app/payments/government/?period=2026-10', clickTab: 'رسوم حكومية وسداد' },
  { id: 'pay-prepaid-wallets', label: 'المدفوعات / المحافظ المسبقة - المحافظ', url: '/app/payments/prepaid-wallets/?period=2026-10', sidebarId: 'pay-prepaid', clickTab: 'المحافظ والأرصدة' },
  { id: 'pay-prepaid-mov', label: 'المدفوعات / المحافظ المسبقة - الحركات', url: '/app/payments/prepaid-wallets/?period=2026-10', clickTab: 'حركات الشحن والاستهلاك' },
  { id: 'pay-prepaid-set', label: 'المدفوعات / المحافظ المسبقة - الإعدادات', url: '/app/payments/prepaid-wallets/?period=2026-10', clickTab: 'إعدادات المحافظ' },
  { id: 'pay-quick-form', label: 'المدفوعات / السريعة - النموذج', url: '/app/payments/quick/?period=2026-10', sidebarId: 'pay-quick', clickTab: 'تسجيل دفعة سريعة' },
  { id: 'pay-quick-hist', label: 'المدفوعات / السريعة - السجل', url: '/app/payments/quick/?period=2026-10', clickTab: 'السجل السريع' },
  { id: 'pay-rec-list', label: 'المدفوعات / الدورية - المستفيدون', url: '/app/payments/recurring/?period=2026-10', sidebarId: 'pay-recurring', clickTab: 'المستفيدون' },
  { id: 'pay-rec-form', label: 'المدفوعات / الدورية - ملف مستفيد', url: '/app/payments/recurring/?period=2026-10', clickTab: 'ملف مستفيد' },
  { id: 'pay-rec-runs-tab', label: 'المدفوعات / الدورية - التشغيل الشهري', url: '/app/payments/recurring/?period=2026-10', clickTab: 'التشغيل الشهري' },
  { id: 'pay-rec-oneoff', label: 'المدفوعات / الدورية - دفعة لمرة', url: '/app/payments/recurring/?period=2026-10', clickTab: 'دفعة لمرة' },
  { id: 'pay-rec-runs-page', label: 'المدفوعات / تشغيل المدفوعات الدورية', url: '/app/payments/recurring/runs/?period=2026-10', sidebarId: 'pay-recurring-runs' },

  // Operations
  { id: 'ops-cities', label: 'التشغيل / المدن والمناطق', url: '/app/operations/?tab=cities&period=2026-10', sidebarId: 'ops-cities' },
  { id: 'ops-housing', label: 'التشغيل / السكن والإسكان', url: '/app/operations/?tab=housing&period=2026-10', sidebarId: 'ops-housing' },
  { id: 'ops-vehicles-period', label: 'التشغيل / المركبات - فترة العهد', url: '/app/operations/?tab=vehicles&view=period&period=2026-10', sidebarId: 'ops-vehicles' },
  { id: 'ops-vehicles-oil', label: 'التشغيل / المركبات - تغيير الزيت', url: '/app/operations/?tab=vehicles&view=oil&period=2026-10' },
  { id: 'ops-vehicles-inv', label: 'التشغيل / المركبات - فواتير المشتريات', url: '/app/operations/?tab=vehicles&view=invoices&period=2026-10' },
  { id: 'ops-fuel-usage', label: 'التشغيل / الوقود - الاستهلاك', url: '/app/operations/?tab=fuel&view=usage&period=2026-10', sidebarId: 'ops-fuel' },
  { id: 'ops-fuel-cards', label: 'التشغيل / الوقود - البطاقات', url: '/app/operations/?tab=fuel&view=cards&period=2026-10' },
  { id: 'ops-fuel-log', label: 'التشغيل / الوقود - سجل البطاقات', url: '/app/operations/?tab=fuel&view=log&period=2026-10' },

  // Payroll (apps + subtabs + main tabs)
  { id: 'pr-keeta-settle', label: 'الرواتب / كيتا - الفواتير والتسويات', url: '/app/payroll/?tab=platforms&app=keeta&sub=settlements&period=2026-10', sidebarId: 'pr-keeta', appPill: 'كيتا' },
  { id: 'pr-keeta-terms', label: 'الرواتب / كيتا - الشروط الأصلية', url: '/app/payroll/?tab=platforms&app=keeta&sub=parent_terms&period=2026-10' },
  { id: 'pr-keeta-schemes', label: 'الرواتب / كيتا - الأنظمة الداخلية', url: '/app/payroll/?tab=platforms&app=keeta&sub=internal_schemes&period=2026-10' },
  { id: 'pr-keeta-elig', label: 'الرواتب / كيتا - الأهلية والضوابط', url: '/app/payroll/?tab=platforms&app=keeta&sub=eligibility&period=2026-10' },
  { id: 'pr-keeta-accounts', label: 'الرواتب / كيتا - الحسابات والمعرفات', url: '/app/payroll/?tab=platforms&app=keeta&sub=accounts&period=2026-10' },
  { id: 'pr-hs-invoices', label: 'الرواتب / هنقرستيشن - فواتير المنصة', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=settlements&period=2026-10', sidebarId: 'pr-hs', appPill: 'هنقرستيشن' },
  { id: 'pr-hs-disputes', label: 'الرواتب / هنقرستيشن - نزاعات CAR', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=settlements&period=2026-10', clickId: '#tab-disputes' },
  { id: 'pr-hs-alloc', label: 'الرواتب / هنقرستيشن - توزيعات TGA', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=settlements&period=2026-10', clickId: '#tab-allocations' },
  { id: 'pr-hs-facts', label: 'الرواتب / هنقرستيشن - وقائع ERP', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=settlements&period=2026-10', clickId: '#tab-facts' },
  { id: 'pr-hs-journals', label: 'الرواتب / هنقرستيشن - القيود المقترحة', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=settlements&period=2026-10', clickId: '#tab-proposed_journals' },
  { id: 'pr-hs-terms', label: 'الرواتب / هنقرستيشن - الشروط الأصلية', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=parent_terms&period=2026-10' },
  { id: 'pr-hs-schemes', label: 'الرواتب / هنقرستيشن - الأنظمة الداخلية', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=internal_schemes&period=2026-10' },
  { id: 'pr-hs-accounts', label: 'الرواتب / هنقرستيشن - الحسابات والمعرفات', url: '/app/payroll/?tab=platforms&app=hungerstation&sub=accounts&period=2026-10' },
  { id: 'pr-ninja-settle', label: 'الرواتب / نينجا - الفواتير والتسويات', url: '/app/payroll/?tab=platforms&app=ninja&sub=settlements&period=2026-10', sidebarId: 'pr-ninja', appPill: 'نينجا' },
  { id: 'pr-ninja-terms', label: 'الرواتب / نينجا - الشروط الأصلية', url: '/app/payroll/?tab=platforms&app=ninja&sub=parent_terms&period=2026-10' },
  { id: 'pr-ninja-schemes', label: 'الرواتب / نينجا - الأنظمة الداخلية', url: '/app/payroll/?tab=platforms&app=ninja&sub=internal_schemes&period=2026-10' },
  { id: 'pr-ninja-elig', label: 'الرواتب / نينجا - الأهلية والضوابط', url: '/app/payroll/?tab=platforms&app=ninja&sub=eligibility&period=2026-10' },
  { id: 'pr-ninja-accounts', label: 'الرواتب / نينجا - الحسابات والمعرفات', url: '/app/payroll/?tab=platforms&app=ninja&sub=accounts&period=2026-10' },
  { id: 'pr-chefz-settle', label: 'الرواتب / شيفز - الفواتير والتسويات', url: '/app/payroll/?tab=platforms&app=chefz&sub=settlements&period=2026-10', sidebarId: 'pr-chefz', appPill: 'شيفز' },
  { id: 'pr-chefz-terms', label: 'الرواتب / شيفز - الشروط الأصلية', url: '/app/payroll/?tab=platforms&app=chefz&sub=parent_terms&period=2026-10' },
  { id: 'pr-chefz-schemes', label: 'الرواتب / شيفز - الأنظمة الداخلية', url: '/app/payroll/?tab=platforms&app=chefz&sub=internal_schemes&period=2026-10' },
  { id: 'pr-chefz-elig', label: 'الرواتب / شيفز - الأهلية والضوابط', url: '/app/payroll/?tab=platforms&app=chefz&sub=eligibility&period=2026-10' },
  { id: 'pr-chefz-accounts', label: 'الرواتب / شيفز - الحسابات والمعرفات', url: '/app/payroll/?tab=platforms&app=chefz&sub=accounts&period=2026-10' },
  { id: 'pr-jahez-settle', label: 'الرواتب / جاهز - الفواتير والتسويات', url: '/app/payroll/?tab=platforms&app=jahez&sub=settlements&period=2026-10', sidebarId: 'pr-jahez', appPill: 'جاهز' },
  { id: 'pr-jahez-terms', label: 'الرواتب / جاهز - الشروط الأصلية', url: '/app/payroll/?tab=platforms&app=jahez&sub=parent_terms&period=2026-10' },
  { id: 'pr-jahez-schemes', label: 'الرواتب / جاهز - الأنظمة الداخلية', url: '/app/payroll/?tab=platforms&app=jahez&sub=internal_schemes&period=2026-10' },
  { id: 'pr-jahez-elig', label: 'الرواتب / جاهز - الأهلية والضوابط', url: '/app/payroll/?tab=platforms&app=jahez&sub=eligibility&period=2026-10' },
  { id: 'pr-jahez-accounts', label: 'الرواتب / جاهز - الحسابات والمعرفات', url: '/app/payroll/?tab=platforms&app=jahez&sub=accounts&period=2026-10' },
  { id: 'pr-mudad', label: 'الرواتب / رواتب مدد (platforms بدون app)', url: '/app/payroll/?tab=platforms&period=2026-10', sidebarId: 'pr-mudad', expectMudad: true },
  { id: 'pr-amazon', label: 'الرواتب / أمازون (مسار مباشر)', url: '/app/payroll/amazon/?period=2026-10' },
  { id: 'pr-cycle', label: 'الرواتب / دورة الرواتب والمسير', url: '/app/payroll/?tab=cycle&period=2026-10' },
  { id: 'pr-current-cycle', label: 'الرواتب / الدورة الحالية', url: '/app/payroll/?tab=current-cycle&period=2026-10' },
  { id: 'pr-ninja-upload', label: 'الرواتب / رفع فاتورة نينجا', url: '/app/payroll/?tab=ninja_upload&period=2026-10' },
  { id: 'pr-bank', label: 'الرواتب / مسيرات الرواتب والبنك', url: '/app/payroll/?tab=bank&period=2026-10', sidebarId: 'pr-bank' },
  { id: 'pr-deductions', label: 'الرواتب / طلبات إيقاف والخصم', url: '/app/payroll/?tab=deductions&period=2026-10', sidebarId: 'pr-holds' },
  { id: 'pr-rules', label: 'الرواتب / إعدادات الرواتب (الأنظمة والقواعد)', url: '/app/payroll/?tab=rules&period=2026-10', sidebarId: 'pr-settings' },
  { id: 'pr-policies', label: 'الرواتب / السياسات السابقة', url: '/app/payroll/?tab=policies&period=2026-10' },

  // Debts
  { id: 'debts-list', label: 'المديونيات / سجل المديونيات', url: '/app/debts/?tab=debts&period=2026-10', sidebarId: 'debts-list' },
  { id: 'debts-advances', label: 'المديونيات / طلبات السلف', url: '/app/debts/?tab=advance_requests&period=2026-10' },
  { id: 'debts-actions', label: 'المديونيات / تحصيل وإنشاء', url: '/app/debts/?tab=actions&period=2026-10' },
  { id: 'debts-statement', label: 'المديونيات / كشف حساب المندوب', url: '/app/debts/?tab=statement&period=2026-10' },
  { id: 'debts-approvals', label: 'المديونيات / منظومة الاعتمادات', url: '/app/debts/?tab=approvals&period=2026-10' },
  { id: 'debts-promissory', label: 'المديونيات / أرشيف سندات الأمر', url: '/app/debts/?tab=promissory&period=2026-10', sidebarId: 'debts-promissory' },

  // Approvals
  { id: 'approvals-all', label: 'الاعتمادات / الكل', url: '/app/approvals/?tab=all&period=2026-10', sidebarId: 'approvals-main' },
  { id: 'approvals-payments', label: 'الاعتمادات / السلف والمدفوعات', url: '/app/approvals/?tab=payments&period=2026-10' },
  { id: 'approvals-salaries', label: 'الاعتمادات / إيقاف وخصم الرواتب', url: '/app/approvals/?tab=salaries&period=2026-10' },
  { id: 'approvals-debts', label: 'الاعتمادات / المديونيات والإعفاءات', url: '/app/approvals/?tab=debts&period=2026-10' },
  { id: 'approvals-erp', label: 'الاعتمادات / طلبات نظام الموارد', url: '/app/approvals/?tab=erp_requests&period=2026-10' },
  { id: 'approvals-other', label: 'الاعتمادات / أخرى', url: '/app/approvals/?tab=other&period=2026-10' },

  // Treasury
  { id: 'tr-balances', label: 'الخزينة / الأرصدة', url: '/app/treasury/?tab=balances&period=2026-10', sidebarId: 'tr-balances' },
  { id: 'tr-transfers', label: 'الخزينة / التحويلات', url: '/app/treasury/?tab=transfers&period=2026-10', sidebarId: 'tr-transfers' },
  { id: 'tr-recon', label: 'الخزينة / مطابقة البنوك', url: '/app/treasury/?tab=bank_recon&period=2026-10', sidebarId: 'tr-recon' },
  { id: 'tr-vault-counts', label: 'الخزينة / جرد الخزن', url: '/app/treasury/?tab=vault_counts&period=2026-10', sidebarId: 'tr-vault' },
  { id: 'tr-vaults', label: 'الخزينة / أرصدة الخزينة (1110-1140)', url: '/app/treasury/?tab=vaults&period=2026-10', sidebarId: 'treasury-vaults' },
  { id: 'tr-jahez', label: 'الخزينة / جاهز', url: '/app/treasury/?tab=jahez&period=2026-10', sidebarId: 'tr-jahez' },
  { id: 'tr-movements', label: 'الخزينة / سجل الحركات', url: '/app/treasury/?tab=movements&period=2026-10', sidebarId: 'tr-movements' },
  { id: 'tr-payouts', label: 'الخزينة / صرف السلف المعتمدة', url: '/app/treasury/?tab=advance_payouts&period=2026-10', sidebarId: 'pay-advance-payouts' },
  { id: 'tr-bank-file', label: 'الخزينة / ملف صرف الرواتب البنكي', url: '/app/treasury/?tab=bank_file&period=2026-10' },

  // Accounting
  { id: 'acc-coa-tree', label: 'المحاسبة / دليل الحسابات - الشجرة', url: '/app/accounting/?tab=coa&period=2026-10', sidebarId: 'acc-coa', clickTab: 'الشجرة النشطة' },
  { id: 'acc-coa-inactive', label: 'المحاسبة / دليل الحسابات - الموقوفة', url: '/app/accounting/?tab=coa&period=2026-10', clickTab: 'الحسابات الموقوفة' },
  { id: 'acc-coa-audit', label: 'المحاسبة / دليل الحسابات - التدقيق', url: '/app/accounting/?tab=coa&period=2026-10', clickTab: 'سجل تدقيق الدليل' },
  { id: 'acc-manual', label: 'المحاسبة / القيود اليدوية', url: '/app/accounting/?tab=manual_journals&period=2026-10', sidebarId: 'acc-manual' },
  { id: 'acc-journals', label: 'المحاسبة / سجل القيود اليومية', url: '/app/accounting/?tab=journals&period=2026-10', sidebarId: 'acc-journals' },
  { id: 'acc-gl', label: 'المحاسبة / دفتر الأستاذ', url: '/app/accounting/?tab=general_ledger&period=2026-10', sidebarId: 'acc-gl' },
  { id: 'acc-tb', label: 'المحاسبة / ميزان المراجعة', url: '/app/accounting/?tab=trial_balance&period=2026-10', sidebarId: 'acc-tb' },
  { id: 'acc-pl', label: 'المحاسبة / قائمة الدخل', url: '/app/accounting/?tab=income_statement&period=2026-10', sidebarId: 'acc-pl' },
  { id: 'acc-bs', label: 'المحاسبة / الميزانية العمومية', url: '/app/accounting/?tab=balance_sheet&period=2026-10', sidebarId: 'acc-bs' },
  { id: 'acc-cc', label: 'المحاسبة / مراكز التكلفة والمدن', url: '/app/accounting/?tab=cost_centres&period=2026-10', sidebarId: 'acc-cc' },
  { id: 'acc-app', label: 'المحاسبة / ربحية التطبيقات', url: '/app/accounting/?tab=app_profitability&period=2026-10', sidebarId: 'acc-app' },
  { id: 'acc-reports', label: 'المحاسبة / التقارير', url: '/app/accounting/?tab=reports&period=2026-10', sidebarId: 'acc-reports' },

  // Rider App & Forms
  { id: 'ra-camp-list', label: 'تطبيق المناديب / الحملات - القائمة', url: '/app/rider-app/?tab=campaigns&subtab=list&period=2026-10', sidebarId: 'app-campaigns' },
  { id: 'ra-camp-analytics', label: 'تطبيق المناديب / الحملات - التحليلات', url: '/app/rider-app/?tab=campaigns&subtab=analytics&period=2026-10' },
  { id: 'ra-camp-evals', label: 'تطبيق المناديب / الحملات - التقييمات', url: '/app/rider-app/?tab=campaigns&subtab=evaluations&period=2026-10' },
  { id: 'ra-resp-forms', label: 'تطبيق المناديب / الردود - الحملات والنماذج', url: '/app/rider-app/?tab=responses&subtab=forms&period=2026-10', sidebarId: 'app-responses' },
  { id: 'ra-resp-evals', label: 'تطبيق المناديب / الردود - التقييمات', url: '/app/rider-app/?tab=responses&subtab=evaluations&period=2026-10' },
  { id: 'ra-builder', label: 'تطبيق المناديب / مصمم النماذج', url: '/app/rider-app/?tab=builder&period=2026-10', sidebarId: 'app-builder' },
  { id: 'ra-accounts', label: 'تطبيق المناديب / الحسابات والأجهزة', url: '/app/rider-app/?tab=accounts&period=2026-10', sidebarId: 'app-accounts' },
  { id: 'ra-bc-broadcast', label: 'تطبيق المناديب / الإشعارات - تعميم عام', url: '/app/rider-app/?tab=broadcasts&subtab=broadcast&period=2026-10', sidebarId: 'app-broadcasts' },
  { id: 'ra-bc-individual', label: 'تطبيق المناديب / الإشعارات - فردية', url: '/app/rider-app/?tab=broadcasts&subtab=individual&period=2026-10' },
  { id: 'ra-bc-history', label: 'تطبيق المناديب / الإشعارات - السجل', url: '/app/rider-app/?tab=broadcasts&subtab=history&period=2026-10' },
  { id: 'ra-statements', label: 'تطبيق المناديب / إرسال كشوف الحساب', url: '/app/rider-app/?tab=statements&period=2026-10', sidebarId: 'app-statements' },
  { id: 'ra-req-all', label: 'تطبيق المناديب / الطلبات - الكل', url: '/app/rider-app/?tab=requests&subtab=all&period=2026-10', sidebarId: 'app-requests' },
  { id: 'ra-req-leave', label: 'تطبيق المناديب / الطلبات - الإجازات السنوية', url: '/app/rider-app/?tab=requests&subtab=annual_leave&period=2026-10' },
  { id: 'ra-req-appt', label: 'تطبيق المناديب / الطلبات - المواعيد', url: '/app/rider-app/?tab=requests&subtab=appointments&period=2026-10' },
  { id: 'ra-req-other', label: 'تطبيق المناديب / الطلبات - طلبات أخرى', url: '/app/rider-app/?tab=requests&subtab=other&period=2026-10' },
  { id: 'ra-req-adv', label: 'تطبيق المناديب / الطلبات - السلف', url: '/app/rider-app/?tab=requests&subtab=advances&period=2026-10' },
  { id: 'ra-settings', label: 'تطبيق المناديب / إعدادات التطبيق', url: '/app/rider-app/?tab=settings&period=2026-10', sidebarId: 'app-settings' },

  // Admin
  { id: 'ad-users', label: 'الإدارة / المستخدمون والصلاحيات', url: '/app/admin/?tab=users&period=2026-10', sidebarId: 'ad-users' },
  { id: 'ad-org-mem', label: 'الإدارة / الهيكل - العضويات', url: '/app/admin/?tab=org&period=2026-10', sidebarId: 'ad-org', clickButton: 'العضويات والرتب' },
  { id: 'ad-org-del', label: 'الإدارة / الهيكل - التفويضات', url: '/app/admin/?tab=org&period=2026-10', clickButton: 'التفويضات الإدارية' },
  { id: 'ad-org-sup', label: 'الإدارة / الهيكل - إشراف المناديب', url: '/app/admin/?tab=org&period=2026-10', clickButton: 'إشراف المناديب' },
  { id: 'ad-org-rout', label: 'الإدارة / الهيكل - التوجيه', url: '/app/admin/?tab=org&period=2026-10', clickButton: 'التوجيه وتكليف المسؤولين' },
  { id: 'ad-bypass', label: 'الإدارة / تجاوز الاعتماد', url: '/app/admin/?tab=bypass&period=2026-10', sidebarId: 'ad-bypass' },
  { id: 'ad-lists', label: 'الإدارة / القوائم العامة', url: '/app/admin/?tab=lists&period=2026-10', sidebarId: 'ad-lists' },
  { id: 'ad-audit', label: 'الإدارة / سجل التدقيق', url: '/app/admin/?tab=audit&period=2026-10', sidebarId: 'ad-audit' },
  { id: 'ad-read-today', label: 'الإدارة / ملخص القراءة - اليوم', url: '/app/admin/?tab=read_summary&period=2026-10', sidebarId: 'ad-read', clickTab: 'اليوم' },
  { id: 'ad-read-range', label: 'الإدارة / ملخص القراءة - فترة', url: '/app/admin/?tab=read_summary&period=2026-10', clickTab: 'فترة' },
  { id: 'ad-read-sens', label: 'الإدارة / ملخص القراءة - المناطق الحساسة', url: '/app/admin/?tab=read_summary&period=2026-10', clickTab: 'المناطق الحساسة' },
  { id: 'ad-set-company', label: 'الإدارة / الإعدادات - بيانات الشركة', url: '/app/admin/?tab=settings&period=2026-10', sidebarId: 'ad-settings', clickTab: 'بيانات الشركة' },
  { id: 'ad-set-notif', label: 'الإدارة / الإعدادات - الإشعارات', url: '/app/admin/?tab=settings&period=2026-10', clickTab: 'إعدادات الإشعارات' },
  { id: 'ad-set-periods', label: 'الإدارة / الإعدادات - الفترات المالية', url: '/app/admin/?tab=settings&period=2026-10', clickTab: 'الفترات المالية' },
  { id: 'ad-set-snap', label: 'الإدارة / الإعدادات - اللقطات', url: '/app/admin/?tab=settings&period=2026-10', clickTab: 'جدولة اللقطات التلقائية' },
  { id: 'ad-set-thresh', label: 'الإدارة / الإعدادات - العتبات والأمان', url: '/app/admin/?tab=settings&period=2026-10', clickTab: 'عتبات التقارير' },
  { id: 'ad-notify-set', label: 'الإدارة / إعدادات الإشعارات', url: '/app/admin/?tab=notify_settings&period=2026-10', sidebarId: 'ad-notify' },
  { id: 'ad-arch-pol', label: 'الإدارة / الأرشفة - سياسات الاحتفاظ', url: '/app/admin/?tab=archive&period=2026-10', sidebarId: 'ad-archive', clickTab: 'سياسات الاحتفاظ' },
  { id: 'ad-arch-req', label: 'الإدارة / الأرشفة - طلبات الاسترجاع', url: '/app/admin/?tab=archive&period=2026-10', clickTab: 'طلبات استرجاع العرض' },
  { id: 'ad-staff-chat', label: 'الإدارة / محادثة الموظفين', url: '/app/admin/?tab=staff_chat&period=2026-10', sidebarId: 'ad-chat' },
];

async function waitForSettle(page) {
  await page.waitForLoadState('networkidle', { timeout: 6000 }).catch(() => {});
  const deadline = Date.now() + 8000;
  while (Date.now() < deadline) {
    const busy = await page.evaluate(() => {
      const spinners = document.querySelectorAll('.animate-pulse, .animate-spin').length;
      const loadingText = /جاري التحميل|جاري التحديث/.test(document.body?.innerText || '');
      return spinners > 0 || loadingText;
    });
    if (!busy) break;
    await page.waitForTimeout(250);
  }
  await page.waitForTimeout(150);
}

async function inspectScreen(page, screen, baseUrl) {
  const issues = [];
  const apiFailures = [];
  const consoleErrors = [];
  const pageErrors = [];

  const onConsole = (msg) => {
    if (msg.type() === 'error') consoleErrors.push(msg.text());
  };
  const onPageError = (err) => {
    pageErrors.push(String(err));
  };
  const onResponse = (res) => {
    const u = res.url();
    if (u.includes('/api/') && res.status() >= 400) {
      const parsed = new URL(u);
      apiFailures.push(`${res.status()} ${res.request().method()} ${parsed.pathname}${parsed.search}`);
    }
  };

  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('response', onResponse);

  try {
    await page.goto(`${baseUrl}${screen.url}`, { waitUntil: 'domcontentloaded', timeout: 20000 });
    await waitForSettle(page);

    if (screen.clickTab) {
      const tabBtn = page.locator('[role="tab"]').filter({ hasText: screen.clickTab }).first();
      if ((await tabBtn.count()) > 0) {
        await tabBtn.click();
        await waitForSettle(page);
      } else {
        issues.push({ kind: 'missing_tab', severity: 'medium', detail: `لم يُعثر على تبويب «${screen.clickTab}»` });
      }
    }

    if (screen.clickButton) {
      const btn = page.locator('main button').filter({ hasText: screen.clickButton }).first();
      if ((await btn.count()) > 0) {
        await btn.click();
        await waitForSettle(page);
      } else {
        issues.push({ kind: 'missing_subtab', severity: 'medium', detail: `لم يُعثر على زر التبويب الفرعي «${screen.clickButton}»` });
      }
    }

    if (screen.clickId) {
      const el = page.locator(screen.clickId).first();
      if ((await el.count()) > 0) {
        await el.click();
        await waitForSettle(page);
      } else {
        issues.push({ kind: 'missing_subtab', severity: 'medium', detail: `لم يُعثر على العنصر ${screen.clickId}` });
      }
    }

    const domCheck = await page.evaluate(({ expectedAppPill, expectMudad }) => {
      const htmlDir = document.documentElement.getAttribute('dir');
      const bodyDir = getComputedStyle(document.body).direction;
      const mainEl = document.querySelector('main') || document.body;
      const mainText = (mainEl?.innerText || '').trim();
      const stillSpinning = document.querySelectorAll('.animate-pulse, .animate-spin').length;

      // Toasts
      const toastAlerts = Array.from(document.querySelectorAll('[role="alert"]')).map((el) => ({
        text: (el.innerText || '').trim(),
        cls: el.className || '',
      }));

      // Inline error banners / ErrorState
      const hasErrorState = Boolean(
        document.querySelector('[data-testid="error-state"]') ||
          /Application error|حدث خطأ غير متوقع|تعذر تحميل|لا تملك صلاحية/.test(mainText)
      );
      const isStubScreen = /قيد التجهيز|قيد التطوير والتفعيل|معلّم «لاحقًا»/.test(mainText);

      // Sidebar active highlights
      const activeSidebarLinks = Array.from(document.querySelectorAll('aside nav a')).filter((a) =>
        (a.className || '').includes('bg-[var(--accent)]')
      ).map((a) => ({ href: a.getAttribute('href') || '', text: (a.innerText || '').trim() }));

      // App selector pills highlight check on /payroll/?tab=platforms
      const pillBtns = Array.from(document.querySelectorAll('main button')).filter((b) =>
        ['كيتا', 'هنقرستيشن', 'نينجا', 'أمازون', 'شيفز', 'جاهز'].some((name) =>
          (b.innerText || '').includes(name)
        )
      );
      const activeAppPills = pillBtns
        .filter((b) => (b.className || '').includes('text-[var(--accent)]'))
        .map((b) => (b.innerText || '').trim().split('\n')[0]);

      let expectedAppHighlighted = true;
      if (expectedAppPill) {
        expectedAppHighlighted =
          activeAppPills.length === 1 && activeAppPills[0].includes(expectedAppPill);
      }

      const mudadMismatch = Boolean(expectMudad && activeAppPills.length > 0);

      // Tables: header column filters + number alignment
      const ACTION_HEADERS = new Set(['الإجراء', 'الإجراءات', 'إجراءات', 'المرفق / إجراء', 'إجراء']);
      const tableReports = [];
      const tables = Array.from(document.querySelectorAll('main table'));
      for (let tIdx = 0; tIdx < tables.length; tIdx++) {
        const table = tables[tIdx];
        const ths = Array.from(table.querySelectorAll('thead tr:first-child th'));
        const dataThs = ths.filter((th) => {
          const hasCheckbox = Boolean(th.querySelector('input[type="checkbox"]'));
          const txt = (th.innerText || '').trim();
          return !hasCheckbox && !ACTION_HEADERS.has(txt) && txt !== '';
        });
        const filteredThs = dataThs.filter((th) =>
          Boolean(th.querySelector('button[aria-label^="تصفية عمود"], button[title*="تصفية"]'))
        );
        const missingFilterHeaders = dataThs
          .filter((th) => !th.querySelector('button[aria-label^="تصفية عمود"], button[title*="تصفية"]'))
          .map((th) => (th.innerText || '').trim());

        // Number alignment check across tbody rows
        const bodyRows = Array.from(table.querySelectorAll('tbody tr'));
        const alignmentIssues = [];
        if (bodyRows.length > 0) {
          for (let c = 0; c < ths.length; c++) {
            const th = ths[c];
            const firstTd = bodyRows[0].children[c];
            if (!th || !firstTd) continue;
            const tdText = (firstTd.innerText || '').trim();
            const looksNumeric =
              Boolean(firstTd.querySelector('.num')) ||
              firstTd.classList.contains('num') ||
              /^-?[\d,]+(\.\d+)?(\s*ر\.س)?$/.test(tdText);
            if (!looksNumeric) continue;
            const thAlign = getComputedStyle(th).textAlign;
            const tdAlign = getComputedStyle(firstTd).textAlign;
            const thDir = getComputedStyle(th).direction;
            const tdDir = getComputedStyle(firstTd).direction;
            if (thAlign !== tdAlign) {
              alignmentIssues.push({
                col: (th.innerText || '').trim(),
                thAlign,
                tdAlign,
              });
            }
            if (!firstTd.classList.contains('num') && !firstTd.querySelector('.num') && /^-/.test(tdText)) {
              alignmentIssues.push({
                col: (th.innerText || '').trim(),
                note: 'negative_without_num_class',
                tdDir,
                thDir,
              });
            }
          }
        }

        tableReports.push({
          tableIndex: tIdx,
          totalDataCols: dataThs.length,
          filteredCols: filteredThs.length,
          missingFilterHeaders,
          rowCount: bodyRows.length,
          alignmentIssues,
        });
      }

      return {
        htmlDir,
        bodyDir,
        mainLen: mainText.length,
        stillSpinning,
        toastAlerts,
        hasErrorState,
        isStubScreen,
        activeSidebarLinks,
        activeAppPills,
        expectedAppHighlighted,
        mudadMismatch,
        tableReports,
      };
    }, { expectedAppPill: screen.appPill || null, expectMudad: Boolean(screen.expectMudad) });

    // Evaluate findings
    if (domCheck.htmlDir !== 'rtl' && domCheck.bodyDir !== 'rtl') {
      issues.push({ kind: 'rtl', severity: 'high', detail: `الاتجاه ليس RTL (html=${domCheck.htmlDir}, body=${domCheck.bodyDir})` });
    }
    if (domCheck.mainLen < 20) {
      issues.push({ kind: 'empty_screen', severity: 'high', detail: 'محتوى الشاشة الرئيسي فارغ تقريبًا' });
    }
    if (domCheck.stillSpinning > 0) {
      issues.push({ kind: 'endless_spinner', severity: 'high', detail: `بقيت ${domCheck.stillSpinning} مؤشرات تحميل/هيكلية نشطة بعد الانتظار` });
    }
    if (domCheck.hasErrorState) {
      issues.push({ kind: 'error_state', severity: 'high', detail: 'ظهرت حالة خطأ أو رسالة تعذر تحميل داخل الصفحة' });
    }
    if (domCheck.isStubScreen) {
      issues.push({ kind: 'stub_screen', severity: 'medium', detail: 'شاشة قيد التجهيز / مؤجلة' });
    }
    for (const t of domCheck.toastAlerts) {
      if (t.cls.includes('danger') || t.cls.includes('warning') || /خطأ|تعذر|فشل/.test(t.text)) {
        issues.push({ kind: 'error_toast', severity: 'high', detail: `إشعار خطأ: ${t.text}` });
      }
    }
    if (consoleErrors.length > 0) {
      issues.push({ kind: 'console_error', severity: 'high', detail: `${consoleErrors.length} أخطاء Console` });
    }
    if (pageErrors.length > 0) {
      issues.push({ kind: 'page_error', severity: 'high', detail: `${pageErrors.length} أخطاء JS غير معالجة` });
    }
    if (apiFailures.length > 0) {
      issues.push({ kind: 'api_error', severity: 'high', detail: `فشل طلبات API (${apiFailures.length})` });
    }
    if (domCheck.activeSidebarLinks.length > 1) {
      issues.push({
        kind: 'multi_sidebar_highlight',
        severity: 'medium',
        detail: `أكثر من عنصر نشط في القائمة الجانبية: ${domCheck.activeSidebarLinks.map((l) => l.text).join(' + ')}`,
      });
    }
    if (screen.appPill && !domCheck.expectedAppHighlighted) {
      issues.push({
        kind: 'app_highlight_mismatch',
        severity: 'medium',
        detail: `تمييز التطبيق المتوقع «${screen.appPill}» لكن المميز فعليًا: [${domCheck.activeAppPills.join(', ')}]`,
      });
    }
    if (domCheck.mudadMismatch) {
      issues.push({
        kind: 'mudad_routes_to_hungerstation',
        severity: 'high',
        detail: `رابط «رواتب مدد» في القائمة (/payroll/?tab=platforms) يفتح منصة «${domCheck.activeAppPills.join(', ')}» ولا توجد منصة مدد`,
      });
    }
    for (const tr of domCheck.tableReports) {
      if (tr.totalDataCols >= 2 && tr.filteredCols === 0) {
        issues.push({
          kind: 'missing_column_filters',
          severity: 'medium',
          detail: `جدول #${tr.tableIndex + 1} (${tr.totalDataCols} أعمدة) بدون فلاتر أعمدة في الرأس`,
        });
      } else if (tr.missingFilterHeaders.length > 0 && tr.filteredCols > 0) {
        issues.push({
          kind: 'partial_column_filters',
          severity: 'low',
          detail: `أعمدة بدون فلتر رأس: ${tr.missingFilterHeaders.join('، ')}`,
        });
      }
      if (tr.alignmentIssues.length > 0) {
        issues.push({
          kind: 'number_alignment',
          severity: 'low',
          detail: `تباين محاذاة أرقام: ${tr.alignmentIssues.map((a) => `${a.col}(${a.thAlign || a.note}≠${a.tdAlign || ''})`).join('، ')}`,
        });
      }
    }

    // Dialog open & cancel check: look for safe primary/create button on page
    const dialogTested = [];
    const safeButtons = page.locator('main button:visible').filter({
      hasText: /^(إضافة|إنشاء|جديد|تسجيل|فتح|طلب |تعيين|رفع|استيراد)/,
    });
    const btnCount = await safeButtons.count();
    if (btnCount > 0) {
      const btn = safeButtons.first();
      const btnLabel = ((await btn.innerText()) || '').trim().split('\n')[0];
      const disabled = await btn.isDisabled();
      if (!disabled && !/استخراج|تصفير|حذف|إلغاء|رفض|اعتماد|ترحيل|^إنشاء جرد$/.test(btnLabel)) {
        try {
          await btn.click({ timeout: 2000 });
          await page.waitForTimeout(350);
          const dlg = page.locator('[role="dialog"]').last();
          if ((await dlg.count()) > 0 && (await dlg.isVisible())) {
            const cancelBtn = dlg
              .locator('button')
              .filter({ hasText: /^(إلغاء|إغلاق|رجوع)$/ })
              .first();
            const closeBtn = dlg.locator('button[aria-label="إغلاق النافذة"]').first();
            if ((await cancelBtn.count()) > 0) {
              await cancelBtn.click();
            } else if ((await closeBtn.count()) > 0) {
              await closeBtn.click();
            } else {
              await page.keyboard.press('Escape');
            }
            await page.waitForTimeout(250);
            const stillOpen = (await page.locator('[role="dialog"]:visible').count()) > 0;
            if (stillOpen) {
              issues.push({
                kind: 'dialog_cancel_failed',
                severity: 'medium',
                detail: `النافذة «${btnLabel}» فُتحت ولم تُغلق عند الإلغاء`,
              });
            } else {
              dialogTested.push(`${btnLabel}: OK`);
            }
          }
        } catch (err) {
          issues.push({
            kind: 'dialog_error',
            severity: 'medium',
            detail: `فشل اختبار النافذة «${btnLabel}»: ${String(err).slice(0, 120)}`,
          });
        }
      }
    }

    let screenshotFile = '';
    if (issues.length > 0) {
      screenshotFile = `${screen.id}.png`;
      await page.screenshot({ path: path.join(SHOTS_DIR, screenshotFile), fullPage: true });
    }

    return {
      id: screen.id,
      label: screen.label,
      url: screen.url,
      issues,
      screenshot: screenshotFile ? path.join(SHOTS_DIR, screenshotFile) : '',
      consoleErrors,
      pageErrors,
      apiFailures,
      tableCount: domCheck.tableReports.length,
      dialogTested,
    };
  } finally {
    page.off('console', onConsole);
    page.off('pageerror', onPageError);
    page.off('response', onResponse);
  }
}

async function main() {
  const outDir = path.join(webDir, 'out');
  assert.ok(existsSync(outDir), 'web/out directory missing. Run npm --prefix web run build first.');
  const appTarget = path.join(repoRoot, 'Payroll.Api/wwwroot/app');
  mkdirSync(appTarget, { recursive: true });
  cpSync(outDir, appTarget, { recursive: true });

  const dataDir = mkdtempSync(path.join(tmpdir(), 'gate-g3-screens-'));
  const port = await freePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const sqlConn =
    process.env.PAYROLL_TEST_SQL_CONNECTION ||
    process.env.ConnectionStrings__DefaultConnection ||
    '';

  console.log(`Starting Payroll.Api on ${baseUrl} (store=${sqlConn ? 'mssql' : 'sqlite'})...`);
  const server = spawn(
    process.env.DOTNET_PATH || 'dotnet',
    [path.join(repoRoot, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],
    {
      cwd: path.join(repoRoot, 'Payroll.Api'),
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: baseUrl,
        PAYROLL_DATA_DIR: dataDir,
        ConnectionStrings__DefaultConnection: sqlConn,
        Operations__Source: 'store',
        Jobs__Enabled: 'false',
        Vouchers__Secret: process.env.Vouchers__Secret || 'GateSynthetic_CI_Only_Voucher_Key_000001',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    }
  );

  let logs = '';
  server.stdout.on('data', (d) => (logs += d));
  server.stderr.on('data', (d) => (logs += d));

  let browser;
  try {
    let ready = false;
    for (let i = 0; i < 200; i++) {
      if (server.exitCode !== null) throw new Error(`Payroll.Api exited early:\n${logs.slice(-2000)}`);
      try {
        if ((await fetch(`${baseUrl}/health`)).ok) {
          ready = true;
          break;
        }
      } catch {}
      await new Promise((r) => setTimeout(r, 150));
    }
    assert.ok(ready, `Payroll.Api did not become ready:\n${logs.slice(-2000)}`);

    const adminPassword = randomBytes(24).toString('hex');
    const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: 'qa-admin',
        password: adminPassword,
        displayNameAr: 'مدير الاختبار',
        role: 'Admin',
      }),
    });
    assert.equal(setupRes.status, 200, 'auth/setup must return 200');
    const cookieHeader = (setupRes.headers.get('set-cookie') || '').split(';')[0];

    await seedSyntheticData(baseUrl, cookieHeader);

    const executablePath = findChromiumExecutable();
    browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
    const context = await browser.newContext({
      viewport: { width: 1440, height: 900 },
      locale: 'ar-SA',
    });
    const [cName, cVal] = cookieHeader.split('=');
    await context.addCookies([
      { name: cName.trim(), value: cVal.trim(), domain: '127.0.0.1', path: '/', httpOnly: true },
    ]);

    const page = await context.newPage();
    const results = [];
    for (const screen of SCREENS) {
      const r = await inspectScreen(page, screen, baseUrl);
      results.push(r);
      const statusTag = r.issues.length ? `FAIL(${r.issues.length})` : 'OK';
      console.log(`[${statusTag}] ${r.id} (${r.label})`);
    }

    writeFileSync(JSON_OUT, JSON.stringify({ generatedAt: new Date().toISOString(), total: results.length, serverErrors: logs.split('\n').filter((l) => /fail:|Exception|500|Payroll request failed/i.test(l)).slice(-40), results }, null, 2));
    console.log(`\nG3 screens pass complete: ${results.length} screens inspected. Output: ${JSON_OUT}`);
  } finally {
    if (browser) await browser.close();
    if (server.exitCode === null) {
      server.kill('SIGTERM');
      await Promise.race([once(server, 'exit'), new Promise((r) => setTimeout(r, 5000))]);
    }
    rmSync(dataDir, { recursive: true, force: true });
  }
}

await main();
