import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const logic = await import('../../src/features/accounting/accounting-panels-logic.ts');

test('AGYFIX2 Row 1 (HIGH): platform panel removes 512201-512206 cost codes, shows revenue/receivables only, and loads target chart only', () => {
  for (const entry of logic.PLATFORM_ACCOUNT_MAP) {
    assert.equal('costCodes' in entry, false);
  }
  const mapJson = JSON.stringify(logic.PLATFORM_ACCOUNT_MAP);
  for (const code of ['512201', '512202', '512203', '512204', '512205', '512206']) {
    assert.equal(mapJson.includes(code), false, `must not reference government-fee code ${code}`);
  }

  const rows = logic.buildAppProfitabilityRows({
    version: '1.0',
    view: 'target',
    from: '2026-10',
    to: '2026-10',
    companies: [
      {
        sponsorId: 'bw-id',
        companyCode: 'BW',
        rows: [
          { code: '112804', nameAr: 'ذمم كيتا', closingDebit: 1500, closingCredit: 200 },
          { code: '411501', nameAr: 'عمولة كيتا', periodDebit: 100, periodCredit: 2500 },
          { code: '512201', nameAr: 'تصاريح عمل', periodDebit: 9000, periodCredit: 0 },
        ],
      },
    ],
  });

  const keeta = rows.find((r) => r.app === 'keeta');
  assert.ok(keeta);
  assert.equal(keeta.receivableClosing, 1300);
  assert.equal(keeta.revenueAmount, 2400);
  assert.equal('directCostAmount' in keeta, false);
  assert.equal('netMargin' in keeta, false);

  const summary = logic.summarizeAppProfitability(rows);
  assert.equal(summary.revenue, 2400);
  assert.equal(summary.rec, 1300);
  assert.equal('cost' in summary, false);
  assert.equal('net' in summary, false);

  const panelSrc = read('src/features/accounting/AccountingExtendedPanels.tsx');
  assert.doesNotMatch(panelSrc, /51220[1-6]/);
  assert.doesNotMatch(panelSrc, /التكاليف المباشرة/);
  assert.doesNotMatch(panelSrc, /صافي الهامش/);
  assert.match(panelSrc, /api\.getTrialBalance\(\{\s*view:\s*'target'/);

  const pageSrc = read('src/app/accounting/page.tsx');
  assert.doesNotMatch(pageSrc, /activeTab === 'app_profitability'[\s\S]*?loadTrialBalance\(\)/);
});

test('AGYFIX2 Row 3 (MEDIUM): general ledger company filter matches line sponsorId only (by id or code)', () => {
  const companies = [
    { id: '019c-bw', code: 'BW', nameAr: 'البوابا', shortNameAr: 'البوابا' },
    { id: '019c-ex', code: 'EX', nameAr: 'إكسبرس', shortNameAr: 'إكسبرس' },
  ];
  const journals = [
    {
      id: 'j-cross',
      journalNumber: 'JV-1',
      period: '2026-10',
      date: '2026-10-15',
      description: 'قيد مشترك',
      originSponsorId: '019c-bw',
      creditorSponsorId: '019c-ex',
      createdAt: '2026-10-15T00:00:00Z',
      createdBy: 'synthetic',
      lines: [
        { accountCode: '111101', sponsorId: '019c-bw', debit: 500, credit: 0 },
        { accountCode: '112702', sponsorId: '019c-ex', debit: 0, credit: 500 },
        { accountCode: '411101', sponsorId: 'BW', debit: 0, credit: 200 },
      ],
    },
  ];

  const byCode = logic.buildGeneralLedgerRows({
    journals,
    periodMonth: '2026-10',
    accountCode: 'all',
    selectedSponsor: 'BW',
    companies,
    coaAccounts: [],
    companyName: (s) => s || '',
  });
  assert.equal(byCode.length, 2);
  assert.equal(byCode.reduce((s, r) => s + r.debit, 0), 500);
  assert.equal(byCode.reduce((s, r) => s + r.credit, 0), 200);

  const byId = logic.buildGeneralLedgerRows({
    journals,
    periodMonth: '2026-10',
    accountCode: 'all',
    selectedSponsor: '019c-bw',
    companies,
    coaAccounts: [],
    companyName: (s) => s || '',
  });
  assert.equal(byId.length, 2);

  const exRows = logic.buildGeneralLedgerRows({
    journals,
    periodMonth: '2026-10',
    accountCode: 'all',
    selectedSponsor: 'EX',
    companies,
    coaAccounts: [],
    companyName: (s) => s || '',
  });
  assert.equal(exRows.length, 1);
  assert.equal(exRows[0].accountCode, '112702');
});

test('AGYFIX2 Row 4 (MEDIUM): balance sheet displays gap, warns when unbalanced, and keeps 3999 as display plug', () => {
  const sections = logic.buildBalanceSheetSections(
    {
      version: '1.0',
      view: 'target',
      from: '2026-10',
      to: '2026-10',
      companies: [
        {
          sponsorId: 'bw',
          companyCode: 'BW',
          rows: [
            { code: '1', nameAr: 'الأصول', level: 1, posting: false, closingDebit: 1000, closingCredit: 0 },
            { code: '2', nameAr: 'الالتزامات', level: 1, posting: false, closingDebit: 0, closingCredit: 300 },
            { code: '3', nameAr: 'حقوق الملكية', level: 1, posting: false, closingDebit: 0, closingCredit: 400 },
          ],
        },
        {
          sponsorId: 'ex',
          companyCode: 'EX',
          rows: [
            { code: '1', nameAr: 'الأصول', level: 1, posting: false, closingDebit: 1000, closingCredit: 0 },
            { code: '2', nameAr: 'الالتزامات', level: 1, posting: false, closingDebit: 0, closingCredit: 400 },
            { code: '3', nameAr: 'حقوق الملكية', level: 1, posting: false, closingDebit: 0, closingCredit: 500 },
          ],
        },
      ],
    },
    {
      companies: [
        { sponsorId: 'bw', companyCode: 'BW', net: 300 },
        { sponsorId: 'ex', companyCode: 'EX', net: 50 },
      ],
    },
    (code) => code || ''
  );

  const bw = sections.find((s) => s.companyLabel === 'BW');
  const ex = sections.find((s) => s.companyLabel === 'EX');
  assert.ok(bw && ex);
  assert.equal(bw.diff, 0);
  assert.equal(bw.isBalanced, true);
  assert.equal(ex.diff, 50);
  assert.equal(ex.isBalanced, false);
  const plug = ex.lines.find((l) => l.code === '3999');
  assert.ok(plug);
  assert.equal(plug.isDisplayPlug, true);
  assert.match(plug.nameAr, /بند عرض/);

  const panelSrc = read('src/features/accounting/AccountingExtendedPanels.tsx');
  assert.match(panelSrc, /فرق الميزانية/);
  assert.match(panelSrc, /الميزانية العمومية غير متوازنة/);
  assert.match(panelSrc, /sec\.isBalanced/);
});
