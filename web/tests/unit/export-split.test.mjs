import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { splitForExport, isIbanColumn } from '../../src/lib/export-split.ts';

// Checksum fixture only (public sample format, not personal data).
const validRajhi = 'SA0380000000608010167519';

describe('Excel export split (R1 / F-R2)', () => {
  it('splits an IBAN column into IBAN / bank code / bank name', () => {
    const t = splitForExport(['الاسم', 'الآيبان', 'المبلغ'], [['مندوب تجريبي', 'SA03 8000 0000 6080 1016 7519', 1500]], [2]);
    assert.deepEqual(t.headers, ['الاسم', 'الآيبان', 'كود البنك', 'اسم البنك', 'المبلغ']);
    assert.deepEqual(t.rows[0], ['مندوب تجريبي', validRajhi, 'RJHI', 'مصرف الراجحي', 1500]);
    // numeric column index moves with the two added columns
    assert.deepEqual(t.numericColumns, [4]);
  });

  it('keeps iqama and phone as text even when marked numeric', () => {
    const t = splitForExport(['الإقامة', 'الجوال'], [['2000000001', '0500000000']], [0, 1]);
    assert.deepEqual(t.numericColumns, []);
    assert.deepEqual(t.rows[0], ['2000000001', '0500000000']);
  });

  it('writes an empty cell for null / missing values, never 0', () => {
    const t = splitForExport(['الآيبان', 'الملاحظة'], [[null, undefined]]);
    assert.deepEqual(t.rows[0], ['', '', '', '']);
  });

  it('detects an IBAN column by its values when the header does not say IBAN', () => {
    assert.equal(isIbanColumn('رقم الحساب البنكي', [validRajhi, '', null]), true);
    assert.equal(isIbanColumn('البنك', ['مصرف الراجحي (RJHI)']), false);
    assert.equal(isIbanColumn('الاسم', ['مندوب']), false);
  });

  it('leaves the bank cells empty for a non-Saudi or broken IBAN', () => {
    const t = splitForExport(['الآيبان'], [['—'], ['XX12']]);
    assert.deepEqual(t.rows, [['', '', ''], ['XX12', '', '']]);
  });
});
