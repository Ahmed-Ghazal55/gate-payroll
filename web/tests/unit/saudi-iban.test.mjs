import { describe, it } from 'node:test';
import assert from 'node:assert/strict';

// Mirrors web/src/lib/saudi-banks.ts IBAN helpers (keep in sync).
const BANKS = {
  '80': { ar: 'مصرف الراجحي', swift: 'RJHI' },
  '10': { ar: 'البنك الأهلي السعودي', swift: 'NCBK' },
};

const cleanIban = (iban) => (iban || '').replace(/\s+/g, '').toUpperCase();

const formatIban = (iban) => {
  const clean = cleanIban(iban);
  return clean ? clean.replace(/(.{4})/g, '$1 ').trim() : '—';
};

const bankCodeOfIban = (iban) => {
  const clean = cleanIban(iban);
  return /^SA\d{4}/.test(clean) ? clean.slice(4, 6) : '';
};

const isValidSaudiIban = (iban) => {
  const clean = cleanIban(iban);
  if (!/^SA[0-9]{22}$/.test(clean)) return false;
  const rearranged = clean.slice(4) + '2810' + clean.slice(2, 4);
  let remainder = 0;
  for (const ch of rearranged) {
    remainder = (remainder * 10 + Number(ch)) % 97;
  }
  return remainder === 1;
};

const bankLabel = (input) => {
  const code = (input.bankCode || '').trim() || bankCodeOfIban(input.iban);
  const known = BANKS[code];
  if (known) return `${known.ar} (${known.swift})`;
  if (input.bank) return input.bank;
  return code ? `بنك غير معرّف — كود ${code}` : '—';
};

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
