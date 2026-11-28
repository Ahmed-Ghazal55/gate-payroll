import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const panel = readFileSync(path.join(root, 'src/features/treasury/OwnerExternalBoxPanel.tsx'), 'utf8');

test('owner box panel defaults wallet leaf to collection (JC1e N1; was custody in JC1d)', () => {
  assert.match(panel, /function defaultSourceForLeaf/);
  assert.match(panel, /WALLET_TOPUP_COLLECTIONS/);
  assert.match(panel, /custody_wallet_topup/);
  assert.match(panel, /jahez_account_opening_fee/);
  assert.match(panel, /selectLeaf/);
  assert.match(panel, /defaultSourceForLeaf\(leaf\)/);
  // Wallet leaf default is rider-cash collection, not custody expense on arrival.
  assert.match(
    panel,
    /WALLET_TOPUP_CODE \|\| purpose === 'wallet_topup_collection'\) return 'collection'/
  );
});

test('owner box panel calls reverse for posted entries (JC1d new #2)', () => {
  assert.match(panel, /reverseOwnerExternalBoxEntry/);
  assert.match(panel, /doReverseEntry/);
  assert.match(panel, /قيد عكسي/);
  assert.match(panel, /status === 'posted'/);
});
