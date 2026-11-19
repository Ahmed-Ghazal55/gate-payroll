import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  bankCodeOfIban,
  bankLabel,
  formatIban,
  isValidSaudiIban,
} from '../../src/lib/saudi-banks.ts';

describe('saudi IBAN helpers (F-R3)', () => {
  // Known-valid Al Rajhi sample used only as a checksum fixture (not personal data).
  const validRajhi = 'SA0380000000608010167519';

  it('detects bank code 80 from a Saudi IBAN', () => {
    assert.equal(bankCodeOfIban(validRajhi), '80');
    assert.equal(bankLabel({ iban: validRajhi }), 'مصرف الراجحي (RJHI)');
  });

  it('formats IBAN in groups of four', () => {
    assert.equal(formatIban(validRajhi), 'SA03 8000 0000 6080 1016 7519');
  });

  it('rejects the all-zero fixture and short values', () => {
    assert.equal(isValidSaudiIban('SA0000000000000000000000'), false);
    assert.equal(isValidSaudiIban('SA038000'), false);
    assert.equal(isValidSaudiIban(''), false);
  });

  it('accepts a checksum-valid Saudi IBAN', () => {
    assert.equal(isValidSaudiIban(validRajhi), true);
  });

  it('prefers API bankCode over the IBAN body', () => {
    assert.equal(bankLabel({ bankCode: '10', iban: validRajhi }), 'البنك الأهلي السعودي (NCBK)');
  });
});
