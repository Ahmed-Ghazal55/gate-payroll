import type {
  NinjaTermsSeed,
  PayBandItem,
  PayEligibilityCreateInput,
  PayEligibilityRule,
  PaySchemeCategoryCell,
  PaySchemeCreateInput,
  PayTermsCreateInput,
} from '../../lib/types';

export const PAY_CATEGORIES_META: ReadonlyArray<{
  key: string;
  labelAr: string;
  sponsorshipAr: string;
  vehicleAr: string;
  isPrivateVehicle: boolean;
}> = [
  {
    key: 'sponsored_company_car',
    labelAr: 'كفالة × سيارة شركة',
    sponsorshipAr: 'على الكفالة',
    vehicleAr: 'سيارة شركة',
    isPrivateVehicle: false,
  },
  {
    key: 'sponsored_company_bike',
    labelAr: 'كفالة × دباب شركة',
    sponsorshipAr: 'على الكفالة',
    vehicleAr: 'دباب شركة',
    isPrivateVehicle: false,
  },
  {
    key: 'sponsored_private',
    labelAr: 'كفالة × مركبة خاصة',
    sponsorshipAr: 'على الكفالة',
    vehicleAr: 'مركبة خاصة',
    isPrivateVehicle: true,
  },
  {
    key: 'external_company_car',
    labelAr: 'خارجي × سيارة شركة',
    sponsorshipAr: 'خارجي (أجير / متعاون)',
    vehicleAr: 'سيارة شركة',
    isPrivateVehicle: false,
  },
  {
    key: 'external_company_bike',
    labelAr: 'خارجي × دباب شركة',
    sponsorshipAr: 'خارجي (أجير / متعاون)',
    vehicleAr: 'دباب شركة',
    isPrivateVehicle: false,
  },
  {
    key: 'external_private',
    labelAr: 'خارجي × مركبة خاصة',
    sponsorshipAr: 'خارجي (أجير / متعاون)',
    vehicleAr: 'مركبة خاصة',
    isPrivateVehicle: true,
  },
];

export const SCHEME_TYPES_META: ReadonlyArray<{
  value: 'daily_commission' | 'percent_of_gross' | 'target_salary' | 'monthly_commission' | 'percent_monthly_with_vehicle';
  labelAr: string;
  descriptionAr: string;
}> = [
  {
    value: 'daily_commission',
    labelAr: 'عمولة يومية (daily_commission)',
    descriptionAr: 'عمولة يومية ثابتة للشركة عن كل يوم تشغيل، والباقي للمندوب.',
  },
  {
    value: 'percent_of_gross',
    labelAr: 'نسبة من الإجمالي (percent_of_gross)',
    descriptionAr: 'نسبة مئوية للشركة من إجمالي دخل المنصة قبل أي استقطاع.',
  },
  {
    value: 'target_salary',
    labelAr: 'راتب على تارجت وشرائح (target_salary)',
    descriptionAr: 'شرائح طلبات براتب أساسي وسعر للطلب الإضافي مع سقف وقود وبدل إنترنت.',
  },
  {
    value: 'monthly_commission',
    labelAr: 'عمولة شهرية (monthly_commission)',
    descriptionAr: 'عمولة شهرية للشركة (ثابتة أو بالتناسب مع أيام العمل).',
  },
  {
    value: 'percent_monthly_with_vehicle',
    labelAr: 'نسبة شهرية مع مركبة الشركة (percent_monthly_with_vehicle)',
    descriptionAr: 'نسبة للشركة من الإجمالي مع إلزامية استقطاع إيجار مركبة الشركة.',
  },
];

export interface BandFormRow {
  min: string;
  max: string;
  base: string;
  perOrderRate: string;
  extraRate: string;
  fuelCap: string;
  internet: string;
}

export interface CategoryCellFormState {
  enabled: boolean;
  dailyCommission: string;
  monthlyCommission: string;
  companyPercent: string;
  prorate: boolean;
  chargeCompanyVehicle: boolean;
  fallbackRate: string;
  operatingCost: string;
  operatingCostAccount: string;
  fuelCap: string;
  internet: string;
  bands: BandFormRow[];
}

export interface SchemeFormState {
  code: string;
  name: string;
  type: 'daily_commission' | 'percent_of_gross' | 'target_salary' | 'monthly_commission' | 'percent_monthly_with_vehicle';
  accountingRole: 'principal' | 'agent';
  eligibilitySetId: string;
  baseVersionId: string;
  categories: Record<string, CategoryCellFormState>;
}

export interface NinjaLevelFormRow {
  maxBelow: string;
  missingOrderRate: string;
}

export interface NinjaCapabilityFormState {
  contractAmount: string;
  contractTarget: string;
  extraOrderRate: string;
  perOrderRate: string;
  levels: NinjaLevelFormRow[];
}

export interface TermsFormState {
  name: string;
  billingModel: 'per_order' | 'slabs' | 'flat_target' | 'ninja_target';
  baseVersionId: string;
  companyCostAccount: string;
  // per_order
  pricePerOrder: string;
  perKmRate: string;
  includedKm: string;
  maxBillableKm: string;
  // flat_target
  targetOrders: string;
  monthlyAmount: string;
  extraRate: string;
  shortfallPerOrder: string;
  prorate: boolean;
  // slabs
  metric: string;
  bands: BandFormRow[];
  // ninja_target
  monthDays: 'calendar' | 'standard';
  car: NinjaCapabilityFormState;
  bike: NinjaCapabilityFormState;
  attendanceMinRatio: string;
  attendanceDeductionPercent: string;
  shortDayDeductionPercent: string;
  vatPercent: string;
}

export interface EligibilityRuleFormRow {
  metric: string;
  operator: string;
  threshold: string;
  effectType: 'zero' | 'next_band' | 'per_order';
  included: boolean;
}

export interface EligibilityFormState {
  code: string;
  name: string;
  baseVersionId: string;
  rules: EligibilityRuleFormRow[];
}

export function createEmptyBandRow(): BandFormRow {
  return {
    min: '',
    max: '',
    base: '',
    perOrderRate: '',
    extraRate: '',
    fuelCap: '',
    internet: '',
  };
}

export function createEmptyCategoryCell(isPrivateVehicle = false): CategoryCellFormState {
  return {
    enabled: false,
    dailyCommission: '',
    monthlyCommission: '',
    companyPercent: '',
    prorate: false,
    chargeCompanyVehicle: !isPrivateVehicle,
    fallbackRate: '',
    operatingCost: '',
    operatingCostAccount: '',
    fuelCap: '',
    internet: '',
    bands: [createEmptyBandRow()],
  };
}

export function createEmptySchemeForm(appSlug: string): SchemeFormState {
  const categories: Record<string, CategoryCellFormState> = {};
  for (const cat of PAY_CATEGORIES_META) {
    categories[cat.key] = createEmptyCategoryCell(cat.isPrivateVehicle);
  }
  const defaultRole: 'principal' | 'agent' =
    appSlug === 'ninja' || appSlug === 'chefz' || appSlug === 'amazon' ? 'principal' : 'agent';
  return {
    code: '',
    name: '',
    type: 'daily_commission',
    accountingRole: defaultRole,
    eligibilitySetId: '',
    baseVersionId: '',
    categories,
  };
}

export function createEmptyTermsForm(appSlug: string): TermsFormState {
  return {
    name: '',
    billingModel: appSlug === 'ninja' ? 'ninja_target' : appSlug === 'keeta' ? 'slabs' : 'per_order',
    baseVersionId: '',
    companyCostAccount: '',
    pricePerOrder: '',
    perKmRate: '',
    includedKm: '',
    maxBillableKm: '',
    targetOrders: '',
    monthlyAmount: '',
    extraRate: '',
    shortfallPerOrder: '',
    prorate: false,
    metric: 'orders',
    bands: [createEmptyBandRow()],
    monthDays: 'calendar',
    car: {
      contractAmount: '',
      contractTarget: '',
      extraOrderRate: '',
      perOrderRate: '',
      levels: [{ maxBelow: '', missingOrderRate: '' }],
    },
    bike: {
      contractAmount: '',
      contractTarget: '',
      extraOrderRate: '',
      perOrderRate: '',
      levels: [{ maxBelow: '', missingOrderRate: '' }],
    },
    attendanceMinRatio: '',
    attendanceDeductionPercent: '',
    shortDayDeductionPercent: '',
    vatPercent: '',
  };
}

export function termsFormFromNinjaSeed(seed: NinjaTermsSeed, baseVersionId = ''): TermsFormState {
  const empty = createEmptyTermsForm('ninja');
  const p = seed.parameters;
  const car = p.capabilities?.car;
  const bike = p.capabilities?.bike;
  return {
    ...empty,
    name: seed.name || '',
    billingModel: 'ninja_target',
    baseVersionId,
    companyCostAccount: p.companyCostAccount || '',
    monthDays: p.monthDays || 'calendar',
    car: {
      contractAmount: car?.contractAmount !== undefined ? String(car.contractAmount) : '',
      contractTarget: car?.contractTarget !== undefined ? String(car.contractTarget) : '',
      extraOrderRate: car?.extraOrderRate !== undefined ? String(car.extraOrderRate) : '',
      perOrderRate: car?.perOrderRate !== undefined ? String(car.perOrderRate) : '',
      levels:
        car?.levels && car.levels.length > 0
          ? car.levels.map((l) => ({
              maxBelow: String(l.maxBelow),
              missingOrderRate: String(l.missingOrderRate),
            }))
          : [{ maxBelow: '', missingOrderRate: '' }],
    },
    bike: {
      contractAmount: bike?.contractAmount !== undefined ? String(bike.contractAmount) : '',
      contractTarget: bike?.contractTarget !== undefined ? String(bike.contractTarget) : '',
      extraOrderRate: bike?.extraOrderRate !== undefined ? String(bike.extraOrderRate) : '',
      perOrderRate: bike?.perOrderRate !== undefined ? String(bike.perOrderRate) : '',
      levels:
        bike?.levels && bike.levels.length > 0
          ? bike.levels.map((l) => ({
              maxBelow: String(l.maxBelow),
              missingOrderRate: String(l.missingOrderRate),
            }))
          : [{ maxBelow: '', missingOrderRate: '' }],
    },
    attendanceMinRatio: p.attendance?.minRatio !== undefined ? String(p.attendance.minRatio) : '',
    attendanceDeductionPercent: p.attendance?.deductionPercent !== undefined ? String(p.attendance.deductionPercent) : '',
    shortDayDeductionPercent:
      p.shortDays?.deductionPercentOfDailyAmount !== undefined ? String(p.shortDays.deductionPercentOfDailyAmount) : '',
    vatPercent: p.vatPercent !== undefined ? String(p.vatPercent) : '',
  };
}

export function createEmptyEligibilityForm(): EligibilityFormState {
  return {
    code: 'default',
    name: '',
    baseVersionId: '',
    rules: [
      {
        metric: 'orders',
        operator: 'gte',
        threshold: '',
        effectType: 'zero',
        included: true,
      },
    ],
  };
}

export function termsFormFromDoc(doc: any, baseVersionId = ''): TermsFormState {
  const empty = createEmptyTermsForm(doc.app || 'hs');
  const d = doc.data || {};
  const p = (d.parameters || {}) as Record<string, any>;
  const car = p.capabilities?.car;
  const bike = p.capabilities?.bike;

  return {
    ...empty,
    name: d.name || '',
    billingModel: d.billingModel || empty.billingModel,
    baseVersionId: baseVersionId || doc.id || '',
    companyCostAccount: p.companyCostAccount || '',
    pricePerOrder: p.pricePerOrder !== undefined ? String(p.pricePerOrder) : '',
    perKmRate: p.perKmRate !== undefined ? String(p.perKmRate) : '',
    includedKm: p.includedKm !== undefined ? String(p.includedKm) : '',
    maxBillableKm: p.maxBillableKm !== undefined ? String(p.maxBillableKm) : '',
    targetOrders: p.targetOrders !== undefined ? String(p.targetOrders) : '',
    monthlyAmount: p.monthlyAmount !== undefined ? String(p.monthlyAmount) : '',
    extraRate: p.extraRate !== undefined ? String(p.extraRate) : '',
    shortfallPerOrder: p.shortfallPerOrder !== undefined ? String(p.shortfallPerOrder) : '',
    prorate: Boolean(p.prorate),
    metric: p.metric || 'orders',
    bands:
      Array.isArray(p.bands) && p.bands.length > 0
        ? p.bands.map((b: any) => ({
            min: b.min !== undefined ? String(b.min) : '',
            max: b.max !== undefined ? String(b.max) : '',
            base: b.base !== undefined ? String(b.base) : '',
            perOrderRate: b.perOrderRate !== undefined ? String(b.perOrderRate) : '',
            extraRate: b.extraRate !== undefined ? String(b.extraRate) : '',
            fuelCap: b.fuelCap !== undefined ? String(b.fuelCap) : '',
            internet: b.internet !== undefined ? String(b.internet) : '',
          }))
        : [createEmptyBandRow()],
    monthDays: p.monthDays || 'calendar',
    car: {
      contractAmount: car?.contractAmount !== undefined ? String(car.contractAmount) : '',
      contractTarget: car?.contractTarget !== undefined ? String(car.contractTarget) : '',
      extraOrderRate: car?.extraOrderRate !== undefined ? String(car.extraOrderRate) : '',
      perOrderRate: car?.perOrderRate !== undefined ? String(car.perOrderRate) : '',
      levels:
        car?.levels && car.levels.length > 0
          ? car.levels.map((l: any) => ({
              maxBelow: String(l.maxBelow),
              missingOrderRate: String(l.missingOrderRate),
            }))
          : [{ maxBelow: '', missingOrderRate: '' }],
    },
    bike: {
      contractAmount: bike?.contractAmount !== undefined ? String(bike.contractAmount) : '',
      contractTarget: bike?.contractTarget !== undefined ? String(bike.contractTarget) : '',
      extraOrderRate: bike?.extraOrderRate !== undefined ? String(bike.extraOrderRate) : '',
      perOrderRate: bike?.perOrderRate !== undefined ? String(bike.perOrderRate) : '',
      levels:
        bike?.levels && bike.levels.length > 0
          ? bike.levels.map((l: any) => ({
              maxBelow: String(l.maxBelow),
              missingOrderRate: String(l.missingOrderRate),
            }))
          : [{ maxBelow: '', missingOrderRate: '' }],
    },
    attendanceMinRatio: p.attendance?.minRatio !== undefined ? String(p.attendance.minRatio) : '',
    attendanceDeductionPercent: p.attendance?.deductionPercent !== undefined ? String(p.attendance.deductionPercent) : '',
    shortDayDeductionPercent:
      p.shortDays?.deductionPercentOfDailyAmount !== undefined ? String(p.shortDays.deductionPercentOfDailyAmount) : '',
    vatPercent: p.vatPercent !== undefined ? String(p.vatPercent) : '',
  };
}

export function schemeFormFromDoc(doc: any, baseVersionId = ''): SchemeFormState {
  const d = doc.data || {};
  const empty = createEmptySchemeForm(doc.app || 'hs');
  const storedCats = (d.parameters?.categories || {}) as Record<string, any>;
  const categories: Record<string, CategoryCellFormState> = {};

  for (const cat of PAY_CATEGORIES_META) {
    const stored = storedCats[cat.key];
    if (stored && stored.enabled) {
      categories[cat.key] = {
        enabled: true,
        dailyCommission: stored.dailyCommission !== undefined ? String(stored.dailyCommission) : '',
        monthlyCommission: stored.monthlyCommission !== undefined ? String(stored.monthlyCommission) : '',
        companyPercent: stored.companyPercent !== undefined ? String(stored.companyPercent) : '',
        prorate: Boolean(stored.prorate),
        chargeCompanyVehicle: Boolean(stored.chargeCompanyVehicle),
        fallbackRate: stored.fallbackRate !== undefined ? String(stored.fallbackRate) : '',
        operatingCost: stored.operatingCost !== undefined ? String(stored.operatingCost) : '',
        operatingCostAccount: stored.operatingCostAccount || '',
        fuelCap: stored.fuelCap !== undefined ? String(stored.fuelCap) : '',
        internet: stored.internet !== undefined ? String(stored.internet) : '',
        bands:
          Array.isArray(stored.bands) && stored.bands.length > 0
            ? stored.bands.map((b: any) => ({
                min: b.min !== undefined ? String(b.min) : '',
                max: b.max !== undefined ? String(b.max) : '',
                base: b.base !== undefined ? String(b.base) : '',
                perOrderRate: b.perOrderRate !== undefined ? String(b.perOrderRate) : '',
                extraRate: b.extraRate !== undefined ? String(b.extraRate) : '',
                fuelCap: b.fuelCap !== undefined ? String(b.fuelCap) : '',
                internet: b.internet !== undefined ? String(b.internet) : '',
              }))
            : [createEmptyBandRow()],
      };
    } else {
      categories[cat.key] = createEmptyCategoryCell(cat.isPrivateVehicle);
    }
  }

  return {
    code: doc.code || '',
    name: d.name || '',
    type: d.type || 'daily_commission',
    accountingRole: d.accountingRole || 'agent',
    eligibilitySetId: d.eligibilitySetId || '',
    baseVersionId: baseVersionId || doc.id || '',
    categories,
  };
}

export function eligibilityFormFromDoc(doc: any, baseVersionId = ''): EligibilityFormState {
  const d = doc.data || {};
  const rulesStored = Array.isArray(d.rules) ? d.rules : [];
  return {
    code: doc.code || 'default',
    name: d.name || '',
    baseVersionId: baseVersionId || doc.id || '',
    rules:
      rulesStored.length > 0
        ? rulesStored.map((r: any) => ({
            metric: r.metric || '',
            operator: r.operator || 'gte',
            threshold: r.threshold !== undefined ? String(r.threshold) : '',
            effectType: r.effect?.type || 'zero',
            included: r.included !== undefined ? Boolean(r.included) : true,
          }))
        : [
            {
              metric: 'orders',
              operator: 'gte',
              threshold: '',
              effectType: 'zero',
              included: true,
            },
          ],
  };
}

export interface PaySettingDiffItem {
  path: string;
  labelAr: string;
  prevValue: string;
  curValue: string;
  kind: 'changed' | 'added' | 'removed';
}

const FIELD_LABELS_AR: Record<string, string> = {
  name: 'اسم الإعداد',
  type: 'نوع النظام',
  billingModel: 'نموذج الفوترة',
  accountingRole: 'صفة المحاسبة',
  eligibilitySetId: 'شروط الأهلية المرتبطة',
  companyCostAccount: 'حساب تكلفة الشركة',
  pricePerOrder: 'سعر الطلب',
  perKmRate: 'سعر الكيلومتر',
  includedKm: 'الكيلومترات المشمولة',
  maxBillableKm: 'سقف الكيلومترات',
  targetOrders: 'الطلبات المستهدفة',
  monthlyAmount: 'المبلغ الشهري',
  extraRate: 'سعر الطلب الإضافي',
  shortfallPerOrder: 'خصم الطلب الناقص',
  prorate: 'احتساب بالتناسب',
  metric: 'المقياس',
  monthDays: 'أسلوب حساب الشهر',
  attendanceMinRatio: 'نسبة الحضور المطلوبة',
  attendanceDeductionPercent: 'نسبة خصم الحضور',
  shortDayDeductionPercent: 'نسبة خصم اليوم القصير',
  vatPercent: 'نسبة الضريبة المضافة VAT',
  dailyCommission: 'العمولة اليومية',
  monthlyCommission: 'العمولة الشهرية',
  companyPercent: 'نسبة الشركة %',
  chargeCompanyVehicle: 'استقطاع مركبة الشركة',
  fallbackRate: 'سعر الطلب البديل',
  operatingCost: 'تكلفة التشغيل المستردة',
  operatingCostAccount: 'حساب إيراد تكلفة التشغيل',
  fuelCap: 'سقف الوقود',
  internet: 'بدل الإنترنت',
  threshold: 'الحد الأدنى',
  operator: 'معامل المقارنة',
  effect: 'أثر عدم التحقق',
  contractAmount: 'مبلغ العقد',
  contractTarget: 'الهدف الشهري للطلبات',
  perOrderRate: 'سعر الطلب',
};

export function diffPaySettings(
  prevData: unknown,
  curData: unknown,
  prefix = ''
): PaySettingDiffItem[] {
  const diffs: PaySettingDiffItem[] = [];
  if (prevData === curData) return diffs;

  const isObject = (val: unknown): val is Record<string, unknown> =>
    typeof val === 'object' && val !== null && !Array.isArray(val);

  if (isObject(prevData) && isObject(curData)) {
    const allKeys = Array.from(new Set([...Object.keys(prevData), ...Object.keys(curData)]));
    for (const key of allKeys) {
      const pVal = prevData[key];
      const cVal = curData[key];
      const currentPath = prefix ? `${prefix}.${key}` : key;
      const labelAr = FIELD_LABELS_AR[key] || key;

      if (pVal === undefined && cVal !== undefined) {
        diffs.push({
          path: currentPath,
          labelAr,
          prevValue: '— (غير موجود)',
          curValue: typeof cVal === 'object' ? JSON.stringify(cVal) : String(cVal),
          kind: 'added',
        });
      } else if (pVal !== undefined && cVal === undefined) {
        diffs.push({
          path: currentPath,
          labelAr,
          prevValue: typeof pVal === 'object' ? JSON.stringify(pVal) : String(pVal),
          curValue: '— (محذوف)',
          kind: 'removed',
        });
      } else if (isObject(pVal) && isObject(cVal)) {
        diffs.push(...diffPaySettings(pVal, cVal, currentPath));
      } else if (Array.isArray(pVal) && Array.isArray(cVal)) {
        const pStr = JSON.stringify(pVal);
        const cStr = JSON.stringify(cVal);
        if (pStr !== cStr) {
          diffs.push({
            path: currentPath,
            labelAr,
            prevValue: pStr,
            curValue: cStr,
            kind: 'changed',
          });
        }
      } else if (pVal !== cVal) {
        diffs.push({
          path: currentPath,
          labelAr,
          prevValue: String(pVal ?? '—'),
          curValue: String(cVal ?? '—'),
          kind: 'changed',
        });
      }
    }
  } else if (JSON.stringify(prevData) !== JSON.stringify(curData)) {
    diffs.push({
      path: prefix || 'root',
      labelAr: FIELD_LABELS_AR[prefix] || prefix || 'الإعداد',
      prevValue: JSON.stringify(prevData),
      curValue: JSON.stringify(curData),
      kind: 'changed',
    });
  }

  return diffs;
}

function parseRequiredNum(raw: string, fieldLabel: string, min = 0, max = 1000000000): { ok: true; value: number } | { ok: false; error: string } {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return { ok: false, error: `الحقل «${fieldLabel}» مطلوب ولا يمكن تركه فارغاً.` };
  }
  const num = Number(trimmed);
  if (Number.isNaN(num) || !Number.isFinite(num)) {
    return { ok: false, error: `الحقل «${fieldLabel}» يتطلب قيمة رقمية صحيحة.` };
  }
  if (num < min || num > max) {
    return { ok: false, error: `قيمة «${fieldLabel}» خارج النطاق المسموح (${min} إلى ${max}).` };
  }
  return { ok: true, value: Math.round(num * 10000) / 10000 };
}

function parseBands(rows: BandFormRow[], contextLabel: string): { ok: true; bands: PayBandItem[] } | { ok: false; error: string } {
  if (!rows || rows.length === 0) {
    return { ok: false, error: `يجب إضافة شريحة واحدة على الأقل في ${contextLabel}.` };
  }
  const bands: PayBandItem[] = [];
  const starts = new Set<number>();
  for (let i = 0; i < rows.length; i++) {
    const r = rows[i];
    const rowLabel = `${contextLabel} (شريحة #${i + 1})`;
    const minRes = parseRequiredNum(r.min, `بداية الشريحة في ${rowLabel}`, 0);
    if (!minRes.ok) return minRes;
    if (starts.has(minRes.value)) {
      return { ok: false, error: `بداية الشريحة (${minRes.value}) مكررة في ${rowLabel}.` };
    }
    starts.add(minRes.value);

    const band: PayBandItem = { min: minRes.value };
    if (r.max.trim() !== '') {
      const maxRes = parseRequiredNum(r.max, `نهاية الشريحة في ${rowLabel}`, minRes.value);
      if (!maxRes.ok) return maxRes;
      band.max = maxRes.value;
    }

    const hasBase = r.base.trim() !== '';
    const hasPerOrder = r.perOrderRate.trim() !== '';
    if (!hasBase && !hasPerOrder) {
      return { ok: false, error: `يرجى تحديد المبلغ الأساسي أو سعر الطلب في ${rowLabel}.` };
    }
    if (hasBase) {
      const baseRes = parseRequiredNum(r.base, `المبلغ الأساسي في ${rowLabel}`, 0);
      if (!baseRes.ok) return baseRes;
      band.base = baseRes.value;
    }
    if (hasPerOrder) {
      const poRes = parseRequiredNum(r.perOrderRate, `سعر الطلب في ${rowLabel}`, 0);
      if (!poRes.ok) return poRes;
      band.perOrderRate = poRes.value;
    }
    if (r.extraRate.trim() !== '') {
      const exRes = parseRequiredNum(r.extraRate, `سعر الطلب الإضافي في ${rowLabel}`, 0);
      if (!exRes.ok) return exRes;
      band.extraRate = exRes.value;
    }
    if (r.fuelCap.trim() !== '') {
      const fcRes = parseRequiredNum(r.fuelCap, `سقف الوقود في ${rowLabel}`, 0);
      if (!fcRes.ok) return fcRes;
      band.fuelCap = fcRes.value;
    }
    if (r.internet.trim() !== '') {
      const intRes = parseRequiredNum(r.internet, `بدل الإنترنت في ${rowLabel}`, 0);
      if (!intRes.ok) return intRes;
      band.internet = intRes.value;
    }
    bands.push(band);
  }
  const ordered = [...bands].sort((a, b) => a.min - b.min);
  for (let i = 0; i < ordered.length - 1; i++) {
    const curMax = ordered[i].max;
    if (curMax !== undefined && curMax >= ordered[i + 1].min) {
      return { ok: false, error: `الشرائح متداخلة في ${contextLabel}: نهاية الشريحة (${curMax}) تتداخل مع (${ordered[i + 1].min}).` };
    }
  }
  return { ok: true, bands };
}

export function validateTermsForm(
  form: TermsFormState,
  period: string
): { ok: true; input: PayTermsCreateInput } | { ok: false; error: string } {
  const name = form.name.trim();
  if (!name) {
    return { ok: false, error: 'يرجى إدخال اسم عقد المنصة قبل الحفظ.' };
  }
  const parameters: Record<string, unknown> = {};
  if (form.companyCostAccount.trim()) {
    if (!form.companyCostAccount.trim().startsWith('5')) {
      return { ok: false, error: 'حساب تكلفة الشركة يجب أن يبدأ بالرقم 5.' };
    }
    parameters.companyCostAccount = form.companyCostAccount.trim();
  }

  if (form.billingModel === 'per_order') {
    const priceRes = parseRequiredNum(form.pricePerOrder, 'سعر الطلب الأساسي');
    if (!priceRes.ok) return priceRes;
    parameters.pricePerOrder = priceRes.value;

    if (form.perKmRate.trim() !== '' || form.includedKm.trim() !== '' || form.maxBillableKm.trim() !== '') {
      const kmRate = parseRequiredNum(form.perKmRate, 'سعر الكيلومتر');
      if (!kmRate.ok) return kmRate;
      const incKm = parseRequiredNum(form.includedKm, 'الكيلومترات المشمولة');
      if (!incKm.ok) return incKm;
      const maxKm = parseRequiredNum(form.maxBillableKm, 'الحد الأعلى للكيلومترات', incKm.value);
      if (!maxKm.ok) return maxKm;
      parameters.perKmRate = kmRate.value;
      parameters.includedKm = incKm.value;
      parameters.maxBillableKm = maxKm.value;
    }
  } else if (form.billingModel === 'flat_target') {
    const targetRes = parseRequiredNum(form.targetOrders, 'عدد الطلبات المستهدف (Target Orders)', 1);
    if (!targetRes.ok) return targetRes;
    const amountRes = parseRequiredNum(form.monthlyAmount, 'المبلغ الشهري للعقد');
    if (!amountRes.ok) return amountRes;
    const extraRes = parseRequiredNum(form.extraRate, 'سعر الطلب الإضافي');
    if (!extraRes.ok) return extraRes;
    const shortfallRes = parseRequiredNum(form.shortfallPerOrder, 'خصم الطلب الناقص');
    if (!shortfallRes.ok) return shortfallRes;

    parameters.targetOrders = targetRes.value;
    parameters.monthlyAmount = amountRes.value;
    parameters.extraRate = extraRes.value;
    parameters.shortfallPerOrder = shortfallRes.value;
    parameters.prorate = form.prorate;
  } else if (form.billingModel === 'slabs') {
    if (!form.metric.trim()) {
      return { ok: false, error: 'يرجى تحديد مقياس الشرائح (Metric).' };
    }
    const bandsRes = parseBands(form.bands, 'شرائح عقد المنصة');
    if (!bandsRes.ok) return bandsRes;
    parameters.metric = form.metric.trim();
    parameters.bands = bandsRes.bands;
  } else if (form.billingModel === 'ninja_target') {
    const capabilities: Record<string, unknown> = {};
    for (const [capKey, capLabel, capState] of [
      ['car', 'سيارة (Car)', form.car],
      ['bike', 'دباب (Bike)', form.bike],
    ] as const) {
      const amtRes = parseRequiredNum(capState.contractAmount, `مبلغ العقد (${capLabel})`, 1);
      if (!amtRes.ok) return amtRes;
      const tgtRes = parseRequiredNum(capState.contractTarget, `الهدف الشهري (${capLabel})`, 1);
      if (!tgtRes.ok) return tgtRes;
      const extRes = parseRequiredNum(capState.extraOrderRate, `سعر الطلب الإضافي (${capLabel})`, 0);
      if (!extRes.ok) return extRes;
      const poRes = parseRequiredNum(capState.perOrderRate, `سعر الطلب تحت آخر مستوى (${capLabel})`, 0);
      if (!poRes.ok) return poRes;

      if (!capState.levels || capState.levels.length === 0) {
        return { ok: false, error: `يرجى إضافة مستويات الخصم تحت الهدف لـ ${capLabel}.` };
      }
      const parsedLevels: Array<{ maxBelow: number; missingOrderRate: number }> = [];
      let lastMax = 0;
      for (let i = 0; i < capState.levels.length; i++) {
        const lv = capState.levels[i];
        const mbRes = parseRequiredNum(lv.maxBelow, `نسبة النقص للمستوى #${i + 1} (${capLabel})`, 0.0001, 1);
        if (!mbRes.ok) return mbRes;
        if (mbRes.value <= lastMax) {
          return { ok: false, error: `مستويات الخصم لـ ${capLabel} يجب أن تكون تصاعدية بلا تداخل.` };
        }
        lastMax = mbRes.value;
        const mrRes = parseRequiredNum(lv.missingOrderRate, `خصم الطلب الناقص للمستوى #${i + 1} (${capLabel})`, 0);
        if (!mrRes.ok) return mrRes;
        parsedLevels.push({ maxBelow: mbRes.value, missingOrderRate: mrRes.value });
      }
      capabilities[capKey] = {
        contractAmount: amtRes.value,
        contractTarget: tgtRes.value,
        extraOrderRate: extRes.value,
        perOrderRate: poRes.value,
        levels: parsedLevels,
      };
    }

    const attRatioRes = parseRequiredNum(form.attendanceMinRatio, 'الحد الأدنى لنسبة الحضور (0–1)', 0, 1);
    if (!attRatioRes.ok) return attRatioRes;
    const attDedRes = parseRequiredNum(form.attendanceDeductionPercent, 'نسبة خصم الحضور %', 0, 100);
    if (!attDedRes.ok) return attDedRes;
    const shortDedRes = parseRequiredNum(form.shortDayDeductionPercent, 'نسبة خصم اليوم الأقل من 8 ساعات %', 0, 100);
    if (!shortDedRes.ok) return shortDedRes;
    const vatRes = parseRequiredNum(form.vatPercent, 'نسبة الضريبة المضافة VAT %', 0, 100);
    if (!vatRes.ok) return vatRes;

    parameters.monthDays = form.monthDays;
    parameters.capabilities = capabilities;
    parameters.attendance = {
      minRatio: attRatioRes.value,
      deductionPercent: attDedRes.value,
    };
    parameters.shortDays = {
      deductionPercentOfDailyAmount: shortDedRes.value,
    };
    parameters.vatPercent = vatRes.value;
  }

  const input: PayTermsCreateInput = {
    period,
    code: 'default',
    data: {
      name,
      billingModel: form.billingModel,
      parameters,
    },
  };
  if (form.baseVersionId) {
    input.baseVersionId = form.baseVersionId;
  }
  return { ok: true, input };
}

export function validateSchemeForm(
  form: SchemeFormState,
  period: string
): { ok: true; input: PaySchemeCreateInput } | { ok: false; error: string } {
  const name = form.name.trim();
  if (!name) {
    return { ok: false, error: 'يرجى إدخال اسم النظام الداخلي قبل الحفظ.' };
  }
  const code = form.code.trim();
  if (!code || !/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/.test(code)) {
    return {
      ok: false,
      error: 'يرجى إدخال رمز النظام بالحروف الإنجليزية أو الأرقام (مثال: ninja-daily أو keeta-salary).',
    };
  }

  const categoriesOut: Record<string, PaySchemeCategoryCell> = {};
  let enabledCount = 0;

  for (const catMeta of PAY_CATEGORIES_META) {
    const cellState = form.categories[catMeta.key];
    if (!cellState || !cellState.enabled) {
      categoriesOut[catMeta.key] = { enabled: false };
      continue;
    }
    enabledCount++;
    const chargeVehicle =
      form.type === 'percent_monthly_with_vehicle' && !catMeta.isPrivateVehicle
        ? true
        : Boolean(cellState.chargeCompanyVehicle);

    const cellOut: PaySchemeCategoryCell = {
      enabled: true,
      chargeCompanyVehicle: chargeVehicle,
    };

    if (form.type === 'daily_commission') {
      const dcRes = parseRequiredNum(cellState.dailyCommission, `العمولة اليومية للفئة «${catMeta.labelAr}»`, 0);
      if (!dcRes.ok) return dcRes;
      cellOut.dailyCommission = dcRes.value;
    } else if (form.type === 'percent_of_gross' || form.type === 'percent_monthly_with_vehicle') {
      const cpRes = parseRequiredNum(cellState.companyPercent, `نسبة الشركة % للفئة «${catMeta.labelAr}»`, 0, 100);
      if (!cpRes.ok) return cpRes;
      cellOut.companyPercent = cpRes.value;
      if (form.type === 'percent_monthly_with_vehicle') {
        cellOut.prorate = Boolean(cellState.prorate);
      }
    } else if (form.type === 'monthly_commission') {
      const mcRes = parseRequiredNum(cellState.monthlyCommission, `العمولة الشهرية للفئة «${catMeta.labelAr}»`, 0);
      if (!mcRes.ok) return mcRes;
      cellOut.monthlyCommission = mcRes.value;
      cellOut.prorate = Boolean(cellState.prorate);
    } else if (form.type === 'target_salary') {
      const bandsRes = parseBands(cellState.bands, `الفئة «${catMeta.labelAr}»`);
      if (!bandsRes.ok) return bandsRes;
      cellOut.bands = bandsRes.bands;
    }

    if (cellState.fallbackRate.trim() !== '') {
      const fbRes = parseRequiredNum(cellState.fallbackRate, `سعر الطلب البديل للفئة «${catMeta.labelAr}»`, 0);
      if (!fbRes.ok) return fbRes;
      cellOut.fallbackRate = fbRes.value;
    }
    if (cellState.fuelCap.trim() !== '') {
      const fcRes = parseRequiredNum(cellState.fuelCap, `سقف الوقود للفئة «${catMeta.labelAr}»`, 0);
      if (!fcRes.ok) return fcRes;
      cellOut.fuelCap = fcRes.value;
    }
    if (cellState.internet.trim() !== '') {
      const intRes = parseRequiredNum(cellState.internet, `بدل الإنترنت للفئة «${catMeta.labelAr}»`, 0);
      if (!intRes.ok) return intRes;
      cellOut.internet = intRes.value;
    }
    if (cellState.operatingCost.trim() !== '') {
      const ocRes = parseRequiredNum(cellState.operatingCost, `تكلفة التشغيل للفئة «${catMeta.labelAr}»`, 0);
      if (!ocRes.ok) return ocRes;
      cellOut.operatingCost = ocRes.value;
      if (ocRes.value > 0 && !cellState.operatingCostAccount.trim().startsWith('4')) {
        return {
          ok: false,
          error: `تكلفة التشغيل للفئة «${catMeta.labelAr}» تتطلب تحديد حساب إيراد يبدأ بالرقم 4.`,
        };
      }
      if (cellState.operatingCostAccount.trim()) {
        cellOut.operatingCostAccount = cellState.operatingCostAccount.trim();
      }
    }

    categoriesOut[catMeta.key] = cellOut;
  }

  if (enabledCount === 0) {
    return {
      ok: false,
      error: 'يرجى تفعيل فئة واحدة على الأقل من الفئات الست وإدخال قيمتها قبل الحفظ.',
    };
  }

  const input: PaySchemeCreateInput = {
    period,
    code,
    data: {
      name,
      type: form.type,
      accountingRole: form.accountingRole,
      parameters: {
        categories: categoriesOut,
      },
    },
  };
  if (form.eligibilitySetId.trim()) {
    input.data.eligibilitySetId = form.eligibilitySetId.trim();
  }
  if (form.baseVersionId.trim()) {
    input.baseVersionId = form.baseVersionId.trim();
  }
  return { ok: true, input };
}

export function validateEligibilityForm(
  form: EligibilityFormState,
  appSlug: string,
  period: string
): { ok: true; input: PayEligibilityCreateInput } | { ok: false; error: string } {
  const name = form.name.trim();
  if (!name) {
    return { ok: false, error: 'يرجى إدخال اسم جدول شروط الأهلية قبل الحفظ.' };
  }
  const code = form.code.trim() || 'default';
  if (!/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/.test(code)) {
    return { ok: false, error: 'رمز شروط الأهلية غير صالح (استخدم حروفاً إنجليزية وأرقاماً).' };
  }
  if (appSlug !== 'jahez' && (!form.rules || form.rules.length === 0)) {
    return { ok: false, error: 'يرجى إضافة شرط أهلية واحد على الأقل قبل الحفظ.' };
  }

  const rulesOut: PayEligibilityRule[] = [];
  for (let i = 0; i < form.rules.length; i++) {
    const r = form.rules[i];
    if (!r.metric.trim()) {
      return { ok: false, error: `يرجى اختيار المقياس للشرط رقم #${i + 1}.` };
    }
    const thRes = parseRequiredNum(r.threshold, `الحد الأدنى (Threshold) للشرط #${i + 1}`, -1000000000, 1000000000);
    if (!thRes.ok) return thRes;
    rulesOut.push({
      metric: r.metric.trim(),
      operator: r.operator,
      threshold: thRes.value,
      effect: { type: r.effectType },
      included: r.included,
    });
  }

  const input: PayEligibilityCreateInput = {
    period,
    code,
    data: {
      name,
      rules: rulesOut,
    },
  };
  if (form.baseVersionId.trim()) {
    input.baseVersionId = form.baseVersionId.trim();
  }
  return { ok: true, input };
}
