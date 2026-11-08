import assert from 'node:assert/strict';
import {
  createEmptyTermsForm,
  createEmptySchemeForm,
  createEmptyEligibilityForm,
  validateTermsForm,
  validateSchemeForm,
  validateEligibilityForm,
} from '../src/features/payroll/PaySettingsValidation.ts';

console.log('--- Testing Pay Settings Empty Form Validation (S-1) ---');

// 1. Terms Form: Empty form blocks submission
const emptyTerms = createEmptyTermsForm('ninja');
const termsRes = validateTermsForm(emptyTerms, '2026-10');
assert.equal(termsRes.ok, false, 'Empty terms form must fail validation');
assert.match(termsRes.error, /اسم عقد المنصة/, 'Error message must specify name is required');
console.log('✓ Empty terms name correctly blocks submission:', termsRes.error);

// 2. Terms Form: Named form with empty required cells blocks submission
const namedTerms = { ...emptyTerms, name: 'عقد نينجا التجريبي' };
const namedTermsRes = validateTermsForm(namedTerms, '2026-10');
assert.equal(namedTermsRes.ok, false, 'Terms form with empty parameters must fail validation');
assert.match(namedTermsRes.error, /مطلوب ولا يمكن تركه فارغاً|يرجى/, 'Error message must specify missing required parameter');
console.log('✓ Terms with empty cells correctly blocks submission:', namedTermsRes.error);

// 3. Scheme Form: Empty form blocks submission
const emptyScheme = createEmptySchemeForm('ninja');
const schemeRes = validateSchemeForm(emptyScheme, '2026-10');
assert.equal(schemeRes.ok, false, 'Empty scheme form must fail validation');
assert.match(schemeRes.error, /اسم النظام الداخلي/, 'Error message must specify name is required');
console.log('✓ Empty scheme name correctly blocks submission:', schemeRes.error);

// 4. Scheme Form: Named scheme without code or active category blocks submission
const namedSchemeNoCode = { ...emptyScheme, name: 'نظام عمولة نينجا' };
const noCodeRes = validateSchemeForm(namedSchemeNoCode, '2026-10');
assert.equal(noCodeRes.ok, false, 'Scheme without valid code must fail validation');
assert.match(noCodeRes.error, /رمز النظام/, 'Error message must specify code is required');
console.log('✓ Missing scheme code correctly blocks submission:', noCodeRes.error);

const namedSchemeNoCats = { ...emptyScheme, name: 'نظام عمولة نينجا', code: 'ninja-comm-01' };
const noCatsRes = validateSchemeForm(namedSchemeNoCats, '2026-10');
assert.equal(noCatsRes.ok, false, 'Scheme with no enabled categories must fail validation');
assert.match(noCatsRes.error, /تفعيل فئة واحدة على الأقل/, 'Error message must specify at least one category required');
console.log('✓ Scheme with no active categories correctly blocks submission:', noCatsRes.error);

// 5. Scheme Form: Enabled category with empty required rate blocks submission
const schemeWithEmptyCat = {
  ...namedSchemeNoCats,
  categories: {
    ...namedSchemeNoCats.categories,
    sponsored_company_car: {
      ...namedSchemeNoCats.categories.sponsored_company_car,
      enabled: true,
      dailyCommission: '', // empty cell!
    },
  },
};
const emptyCatRes = validateSchemeForm(schemeWithEmptyCat, '2026-10');
assert.equal(emptyCatRes.ok, false, 'Enabled category with empty required cell must fail validation');
assert.match(emptyCatRes.error, /مطلوب ولا يمكن تركه فارغاً/, 'Error message must block empty rate');
console.log('✓ Enabled category with empty rate correctly blocks submission:', emptyCatRes.error);

// 6. Eligibility Form: Empty form blocks submission
const emptyEligibility = createEmptyEligibilityForm();
const elRes = validateEligibilityForm(emptyEligibility, 'ninja', '2026-10');
assert.equal(elRes.ok, false, 'Empty eligibility form must fail validation');
assert.match(elRes.error, /اسم جدول شروط الأهلية/, 'Error message must specify name is required');
console.log('✓ Empty eligibility name correctly blocks submission:', elRes.error);

// 7. Eligibility Form: Empty rule threshold blocks submission
const namedEligibilityEmptyThreshold = {
  ...emptyEligibility,
  name: 'شروط أهلية نينجا',
  code: 'ninja-el-01',
  rules: [
    {
      metric: 'orders',
      operator: 'gte',
      threshold: '', // empty threshold!
      effectType: 'zero',
      included: true,
    },
  ],
};
const elEmptyThRes = validateEligibilityForm(namedEligibilityEmptyThreshold, 'ninja', '2026-10');
assert.equal(elEmptyThRes.ok, false, 'Eligibility rule with empty threshold must fail validation');
assert.match(elEmptyThRes.error, /مطلوب ولا يمكن تركه فارغاً/, 'Error message must block empty threshold');
console.log('✓ Eligibility rule with empty threshold correctly blocks submission:', elEmptyThRes.error);

console.log('--- All S-1 validation assertions passed successfully! ---');
