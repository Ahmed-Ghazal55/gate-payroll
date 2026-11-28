import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const panel = readFileSync(path.join(root, 'src/features/treasury/OwnerExternalBoxPanel.tsx'), 'utf8');

test('wallet leaf: collection first; custody source forces direction out (JC1e HIGH #1)', () => {
  assert.match(panel, /function defaultDirectionForSource/);
  assert.match(panel, /function isCustodySource/);
  assert.match(panel, /isCustodySource\(sourceKind\) \? 'out' : 'in'/);
  assert.match(panel, /setSourceKind/);
  assert.match(panel, /تحصيل نقد المندوب \(دخول — بدون مصروف\)/);
  assert.match(panel, /شحن من العهدة \(خروج — مصروف شركة\)/);
});

test('opening-fee cash-only option; other leaves no special journal sources (JC1e #3)', () => {
  assert.match(panel, /تحصيل نقدي فقط \(بدون قيد شركة\)/);
  // Generic leaves only offer collection — not custody/opening-fee sources alone.
  const otherLeafBlock = panel.slice(
    panel.indexOf(": (\n              <>\n                <option value=\"collection\">تحصيل / حركة نقدية</option>")
  );
  assert.ok(otherLeafBlock.length > 0, 'other-leaf collection-only menu present');
  assert.doesNotMatch(
    otherLeafBlock.slice(0, 280),
    /custody_wallet_topup[\s\S]{0,80}jahez_account_opening_fee/
  );
});
