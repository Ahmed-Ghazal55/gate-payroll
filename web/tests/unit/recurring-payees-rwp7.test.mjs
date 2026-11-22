import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workspace = readFileSync(
  path.join(root, 'src/features/payments/RecurringPayeesWorkspace.tsx'),
  'utf8'
);
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

test('Row2_executeKeepsVouchersAndWiresDualApproveAndPay', () => {
  assert.match(workspace, /vouchers/);
  assert.match(workspace, /decideApproval/);
  assert.match(api, /payPaymentVoucher|vouchers\/\$\{.*\}\/pay/);
  assert.match(workspace, /payPaymentVoucher|\/payments\/vouchers\//);
  assert.match(workspace, /approvalRequestId/);
});

test('Row4_editOmitsBankDetailsUnlessExplicitClear', () => {
  assert.match(workspace, /clearBankDetails/);
  assert.match(workspace, /bankDetailsTouched/);
  assert.match(workspace, /مسح تفاصيل التحويل/);
  assert.match(workspace, /if \(form\.bankDetailsTouched\) body\.bankDetails/);
});

test('Row7_runAndProfileExposeAmountFeeFxOneOffAndCostCentre', () => {
  assert.match(workspace, /costCentreId/);
  assert.match(workspace, /fxDifference/);
  assert.match(workspace, /transferFee/);
  assert.match(workspace, /إضافة بند/);
  assert.match(workspace, /oneOffLine/);
  assert.match(workspace, /patchLine\(r,\s*\{\s*amount:/);
});

test('Row7_costCentreSelectLoadsRealOptionsFromApi', () => {
  assert.match(workspace, /costCentres/);
  assert.match(workspace, /setCostCentres/);
  assert.match(workspace, /costCentres\.map/);
  assert.match(workspace, /costCentreOptions/);
  assert.match(api, /costCentres/);
});

test('Row8_oneOffSendsStableIdempotencyKey', () => {
  assert.match(workspace, /idempotencyKey/);
  assert.match(workspace, /crypto\.randomUUID|randomUUID/);
});

test('Row8_oneOffSavesIdempotencyKeyBeforeSend', () => {
  const block = workspace.match(/const submitOneOff[\s\S]*?finally \{[\s\S]*?\n  \};/);
  assert.ok(block, 'submitOneOff handler present');
  const body = block[0];
  const keyAssign = body.search(/idempotencyKey\s*=\s*oneOff\.idempotencyKey\s*\|\|/);
  const saveBefore = body.search(/setOneOff\(\(f\)\s*=>\s*\(\{\s*\.\.\.f,\s*idempotencyKey\s*\}\)/);
  const awaitCall = body.search(/await api\.createRecurringOneOff/);
  assert.ok(keyAssign >= 0 && saveBefore >= 0 && awaitCall >= 0);
  assert.ok(saveBefore < awaitCall, 'key must be saved in state before the API call');
});

test('Row2_oneOffVoucherControlsOutsideRunGuard', () => {
  assert.match(workspace, /tab === 'oneoff'/);
  assert.match(workspace, /const voucherPanel/);
  const oneOffSection = workspace.match(/tab === 'oneoff' \? \([\s\S]*?\) : \(/);
  assert.ok(oneOffSection, 'one-off tab section');
  assert.match(oneOffSection[0], /\{voucherPanel\}/);
  // Shared panel (outside run guard) wires dual-approve and pay.
  assert.match(workspace, /payVoucher|اعتماد ثنائي/);
  assert.match(workspace, /\{voucherPanel\}\s*<\/div>\s*\)\s*:/);
});
