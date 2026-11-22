import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {
  BEARER_LABELS,
  DOCUMENT_STATUS_LABELS,
  DOCUMENT_TYPE_LABELS,
  GOV_CATEGORY_LABELS,
  accountOptions,
  defaultDateInPeriod,
  documentStatusLabel,
  hasAllowedAction,
  isBankOrCashCode,
} from '../../src/features/payments/payment-labels.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const sidebar = read('src/components/layout/Sidebar.tsx');
const header = read('src/components/layout/Header.tsx');
const apiClient = read('src/lib/api.ts');
const contract = read('src/lib/api-contract.ts');

const PAYMENT_ROUTES = [
  'payments',
  'payments/rents',
  'payments/installments',
  'payments/purchases',
  'payments/fuel',
  'payments/hr-requests',
  'payments/advances',
  'payments/mada',
  'payments/hs-custody',
  'payments/traffic-violations',
  'payments/suppliers',
  'payments/due-calendar',
  'payments/government',
  'payments/prepaid-wallets',
  'payments/quick',
  'payments/recurring',
  'payments/recurring/runs',
];

const PAYMENT_WORKSPACES = [
  {
    file: 'src/features/payments/RentsWorkspace.tsx',
    endpoints: ['listPaymentLeases', 'createPaymentLease', 'getPaymentLeaseSchedule', 'recognizeLeaseRent', 'listPaymentProperties', 'createPaymentProperty', 'archivePaymentContract'],
  },
  {
    file: 'src/features/payments/InstallmentsWorkspace.tsx',
    endpoints: ['listFinanceContracts', 'createFinanceContract', 'getFinanceContractSchedule', 'listPaymentFinanciers', 'createPaymentFinancier', 'archivePaymentContract', 'getDebts'],
  },
  {
    file: 'src/features/payments/PurchasesWorkspace.tsx',
    endpoints: ['getOperationsPurchaseInvoices', 'listPaymentDocuments', 'createPaymentDocument'],
  },
  {
    file: 'src/features/payments/HrRequestsWorkspace.tsx',
    endpoints: ['listHrFees', 'createHrFee'],
  },
  {
    file: 'src/features/payments/AdvancesWorkspace.tsx',
    endpoints: ['listPaymentDocuments', 'createPaymentAdvance', 'getConfigListGroup'],
  },
  {
    file: 'src/features/payments/HsCustodyWorkspace.tsx',
    endpoints: ['listPaymentDocuments'],
  },
  {
    file: 'src/features/payments/SuppliersWorkspace.tsx',
    endpoints: ['listPaymentSuppliers', 'getPaymentSupplierLedger', 'listPaymentDocuments', 'getPaymentVoucher', 'payPaymentVoucher', 'cancelPaymentVoucher'],
  },
  {
    file: 'src/features/payments/DueCalendarWorkspace.tsx',
    endpoints: ['getPaymentDueCalendar', 'getPaymentDocument'],
  },
  {
    file: 'src/features/payments/TrafficViolationsWorkspace.tsx',
    endpoints: ['listTrafficViolations', 'createTrafficViolation'],
  },
  {
    file: 'src/features/payments/GovernmentPaymentsWorkspace.tsx',
    endpoints: ['listGovernmentPayments', 'createGovernmentPayment'],
  },
  {
    file: 'src/features/payments/QuickPaymentWorkspace.tsx',
    endpoints: ['createQuickPayment', 'submitPaymentDocument', 'listPaymentDocuments'],
  },
  {
    file: 'src/features/payments/PaymentsWalletWorkspace.tsx',
    endpoints: ['listMadaWallets', 'listFuelWallets', 'listPrepaidWallets', 'createMadaTopup', 'fundFuelWallet', 'createPrepaidWalletMovement', 'reconcileMada', 'reconcileFuelWallet', 'issueMadaCard'],
  },
];

test('SC2: every Payments sub-page route exists and is wired in Sidebar and Header breadcrumbs', () => {
  for (const route of PAYMENT_ROUTES) {
    assert.ok(existsSync(path.join(root, 'src/app', route, 'page.tsx')), `missing page for /${route}`);
    if (route !== 'payments') {
      assert.match(sidebar, new RegExp(`/${route.replace(/\//g, '\\/')}`), `Sidebar missing /${route}`);
    }
  }
  assert.match(sidebar, /clean\.startsWith\('\/payments\/'\)/, 'Sidebar must highlight payments group on all /payments/* routes');
  assert.match(header, /payments:\s*'المدفوعات والمشتريات'/, 'Header breadcrumb missing payments section');
  assert.match(header, /href:\s*'\/payments\/quick'/, 'Header quick shortcut missing /payments/quick');
});

test('SC2: api.ts derives payment types from api-contract.ts and calls only real contract endpoints', () => {
  assert.match(apiClient, /PaymentDocumentListResponse\s*=\s*ApiResp<'\/api\/payments\/documents'>/);
  assert.match(apiClient, /PaymentVoucherItem\s*=\s*ApiResp<'\/api\/payments\/vouchers\/\{id\}'>/);
  assert.match(apiClient, /PaymentDueCalendarResponse\s*=\s*ApiResp<'\/api\/payments\/due-calendar'>/);
  assert.match(apiClient, /FinanceCoaResponse\s*=\s*ApiResp<'\/api\/finance\/coa'>/);

  const requiredContractPaths = [
    '/api/payments/documents',
    '/api/payments/documents/{id}',
    '/api/payments/documents/{id}/submit',
    '/api/payments/documents/{id}/approve',
    '/api/payments/documents/{id}/cancel',
    '/api/payments/documents/{id}/payment-requests',
    '/api/payments/vouchers/{id}',
    '/api/payments/vouchers/{id}/pay',
    '/api/payments/vouchers/{id}/cancel',
    '/api/payments/quick',
    '/api/payments/suppliers',
    '/api/payments/suppliers/{id}/ledger',
    '/api/payments/hr-fees',
    '/api/payments/traffic-violations',
    '/api/payments/government',
    '/api/payments/properties',
    '/api/payments/financiers',
    '/api/payments/leases',
    '/api/payments/leases/{id}/schedule',
    '/api/payments/leases/{id}/recognize',
    '/api/payments/finance-contracts',
    '/api/payments/finance-contracts/{id}/schedule',
    '/api/payments/contracts/{kind}/{id}/archive',
    '/api/payments/due-calendar',
    '/api/payments/advances',
    '/api/payments/fuel-wallets',
    '/api/payments/prepaid-wallets',
    '/api/payments/mada/wallets',
    '/api/finance/coa',
  ];
  for (const p of requiredContractPaths) {
    assert.ok(contract.includes(`"${p}"`), `api-contract.ts missing ${p}`);
  }
});

test('SC2: every payment workspace wires real endpoints, allowedActions, formatSar, usePeriod, SectionTabs, DataTable, and Arabic-only UI', () => {
  const workflowPanel = read('src/features/payments/PaymentWorkflowPanel.tsx');
  for (const action of ['edit_draft', 'submit', 'cancel_draft', 'approve', 'create_payment_request', 'pay_voucher']) {
    assert.match(workflowPanel, new RegExp(`'${action}'`), `PaymentWorkflowPanel missing allowedAction ${action}`);
  }
  assert.match(workflowPanel, /editPaymentDocument/);
  assert.match(workflowPanel, /submitPaymentDocument/);
  assert.match(workflowPanel, /cancelPaymentDocument/);
  assert.match(workflowPanel, /approvePaymentDocument/);
  assert.match(workflowPanel, /createPaymentRequest/);
  assert.match(workflowPanel, /payPaymentVoucher/);
  assert.match(workflowPanel, /cancelPaymentVoucher/);
  assert.match(workflowPanel, /decideApproval/);

  for (const ws of PAYMENT_WORKSPACES) {
    const src = read(ws.file);
    for (const ep of ws.endpoints) {
      assert.match(src, new RegExp(`\\b${ep}\\b`), `${ws.file} must call ${ep}`);
    }
    assert.match(src, /formatSar/, `${ws.file} must format SAR via formatSar`);
    assert.match(src, /usePeriod/, `${ws.file} must use single URL period hook`);
    assert.match(src, /SectionTabs/, `${ws.file} must use in-page SectionTabs`);
    assert.match(src, /DataTable/, `${ws.file} must use DataTable with ColumnFilterMenu`);
    assert.match(src, /PaymentDocumentActions|hasAllowedAction/, `${ws.file} must enforce allowedActions`);
    assert.doesNotMatch(src, /<select\b/, `${ws.file} must use SearchableSelect instead of native <select>`);
    assert.doesNotMatch(src, /U-W3-|S-PAY|BR\s*§/, `${ws.file} must not expose spec IDs`);
  }

  const recurring = read('src/features/payments/RecurringPayeesWorkspace.tsx');
  assert.doesNotMatch(
    recurring,
    /إضافة بند لمرة \(oneOffLine\)|سندات الصرف \(vouchers\)/,
    'RecurringPayeesWorkspace must not show English glosses in headings'
  );
});

test('SC2: payment-engine helpers map Arabic labels and validate actions/dates/accounts', () => {
  assert.equal(hasAllowedAction({ allowedActions: ['submit', 'cancel_draft'] }, 'submit'), true);
  assert.equal(hasAllowedAction({ allowedActions: ['submit'] }, 'approve'), false);
  assert.equal(hasAllowedAction(null, 'submit'), false);

  assert.equal(documentStatusLabel({ status: 1, amount: 100 }), 'مسودة');
  assert.equal(documentStatusLabel({ status: 3, amount: 100, amountPaid: 100 }), 'مسدد بالكامل');
  assert.equal(DOCUMENT_STATUS_LABELS[5], 'ملغى');
  assert.equal(DOCUMENT_TYPE_LABELS.traffic_violation, 'مخالفة مرورية');
  assert.equal(BEARER_LABELS.split, 'مشترك');
  assert.equal(GOV_CATEGORY_LABELS.vat, 'ضريبة القيمة المضافة');

  assert.equal(defaultDateInPeriod('2026-10'), '2026-10-01');
  assert.equal(isBankOrCashCode('111101'), true);
  assert.equal(isBankOrCashCode('113101'), true);
  assert.equal(isBankOrCashCode('521102'), false);

  const opts = accountOptions(
    [
      { id: '1', code: '111101', nameAr: 'الصندوق', isPostable: true, isActive: true },
      { id: '2', code: '521102', nameAr: 'إيجار سكن إداري', isPostable: true, isActive: true },
    ],
    (a) => isBankOrCashCode(a.code)
  );
  assert.deepEqual(opts, [{ value: '111101', label: '111101 — الصندوق' }]);
});
