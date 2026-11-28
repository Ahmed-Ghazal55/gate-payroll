'use client';

import React, { useEffect, useMemo, useState } from 'react';
import {
  FileText,
  Calendar,
  Clock,
  Layers,
  Award,
  ShieldCheck,
  AlertTriangle,
  Plus,
  Save,
  CheckCircle2,
  User,
  TrendingUp,
  Link2,
} from 'lucide-react';
import { useToast } from '../../components/ui/Toast';
import { DataTable } from '../../components/ui/DataTable';
import type { PayConfigDoc } from '../../lib/types';
import { formatSar } from '../../lib/money';

export interface RuleRecordWorkspaceProps {
  appSlug: string;
  appNameAr: string;
  period: string;
  onOpenExceptionModal: () => void;
  activeEligibilityDoc?: any | null;
  activeTermsDoc?: PayConfigDoc | null;
  activeSchemeDoc?: PayConfigDoc | null;
  activeGlobalDoc?: PayConfigDoc | null;
  onOpenSection?: (section: 'eligibility' | 'record' | 'global') => void;
}

function platformSelectValue(slug: string): string {
  if (slug === 'hs' || slug === 'hungerstation') return 'hs';
  if (slug === 'thechefz' || slug === 'chefz') return 'chefz';
  return slug;
}

function platformLabel(slug: string, nameAr: string): string {
  const key = platformSelectValue(slug);
  if (nameAr && nameAr !== slug && nameAr !== key) return nameAr;
  if (key === 'ninja') return 'نينجا';
  if (key === 'keeta') return 'كيتا';
  if (key === 'hs') return 'هنقرستيشن';
  if (key === 'jahez') return 'جاهز';
  if (key === 'chefz') return 'ذا شيفز';
  if (key === 'amazon') return 'أمازون';
  return nameAr || slug;
}

export function RuleRecordWorkspace({
  appSlug,
  appNameAr,
  period,
  onOpenExceptionModal,
  activeEligibilityDoc,
  activeTermsDoc,
  activeSchemeDoc,
  activeGlobalDoc,
  onOpenSection,
}: RuleRecordWorkspaceProps) {
  const { toast } = useToast();
  const displayName = platformLabel(appSlug, appNameAr);

  // Rule Record Header state — kept in sync with selected platform/period (no stale HungerStation title)
  const [ruleName, setRuleName] = useState<string>(`لائحة قواعد وتشغيل ${displayName} · ${period}`);
  const [selectedApp, setSelectedApp] = useState<string>(platformSelectValue(appSlug));
  const [selectedCity, setSelectedCity] = useState<string>('all');
  const [validFrom, setValidFrom] = useState<string>(`${period}-01`);
  const [validTo, setValidTo] = useState<string>('');
  const [isActive, setIsActive] = useState<boolean>(true);
  const [onMissingReport, setOnMissingReport] = useState<string>('flag_decision');

  useEffect(() => {
    const label = platformLabel(appSlug, appNameAr);
    setSelectedApp(platformSelectValue(appSlug));
    setRuleName(`لائحة قواعد وتشغيل ${label} · ${period}`);
    setValidFrom(`${period}-01`);
  }, [appSlug, appNameAr, period]);

  // Sub-tabs
  type RuleTabKey = 'daily' | 'monthly' | 'levels' | 'incentives' | 'compliance' | 'exceptions' | 'performance' | 'notes';
  const [activeTab, setActiveTab] = useState<RuleTabKey>('daily');

  // Values read from the month's registry (pay_scheme / pay_setting / pay_terms) — no duplicate settings store
  const registryDaily = useMemo(() => {
    const values = activeGlobalDoc?.data?.values;
    const standardDays = Number(values?.standardDays || 30) || 30;
    const carMonthly = Number(values?.vehicleRent?.company_car ?? 1800);
    const bikeMonthly = Number(values?.vehicleRent?.company_bike ?? 800);
    const schemeType = String(activeSchemeDoc?.data?.type || '');
    const isNinja = platformSelectValue(appSlug) === 'ninja';
    // Ninja (and percent schemes): unit is % of rider due before deductions — not SAR/day (Ghazal 11-28 NUI1b).
    const commissionIsPercent =
      isNinja ||
      schemeType === 'percent_of_gross' ||
      schemeType === 'percent_monthly_with_vehicle';
    const cats = activeSchemeDoc?.data?.parameters?.categories || {};
    const firstCell = Object.values(cats)[0] as
      | { dailyCommission?: number; companyPercent?: number }
      | undefined;
    const commissionRaw = commissionIsPercent
      ? firstCell?.companyPercent ?? firstCell?.dailyCommission ?? 10
      : firstCell?.dailyCommission ?? 10;
    const commission = Number(commissionRaw);
    const params = (activeTermsDoc?.data?.parameters || {}) as Record<string, unknown>;
    const shortDays = (params.shortDays || {}) as { deductionPercentOfDailyAmount?: number };
    const attendance = (params.attendance || {}) as {
      minRatio?: number;
      deductionPercent?: number;
    };
    return {
      dailyCommission: commission.toFixed(2),
      commissionIsPercent,
      minWorkingHours: '8',
      dailyCarRent: (carMonthly / standardDays).toFixed(2),
      dailyBikeRent: (bikeMonthly / standardDays).toFixed(2),
      shortDayDeduction: String(shortDays.deductionPercentOfDailyAmount ?? 50),
      attendanceMinRatio: String(attendance.minRatio ?? 0.8),
      shiftComplianceMin: String(attendance.deductionPercent ?? 15),
      monthDaysMode: String(params.monthDays || 'calendar'),
      housingMonthly: Number(values?.housingMonthly ?? 200),
    };
  }, [activeGlobalDoc, activeSchemeDoc, activeTermsDoc, appSlug]);

  const [dailyCommission, setDailyCommission] = useState<string>(registryDaily.dailyCommission);
  const [minWorkingHours, setMinWorkingHours] = useState<string>(registryDaily.minWorkingHours);
  const [dailyCarRent, setDailyCarRent] = useState<string>(registryDaily.dailyCarRent);
  const [dailyBikeRent, setDailyBikeRent] = useState<string>(registryDaily.dailyBikeRent);

  // Monthly rules state
  const [targetOrders, setTargetOrders] = useState<string>('450');
  const [mandatoryWorkingDays, setMandatoryWorkingDays] = useState<string>('26');
  const [monthDaysMode, setMonthDaysMode] = useState<string>(registryDaily.monthDaysMode);

  // Incentives state
  const [fuelCap, setFuelCap] = useState<string>('400.00');
  const [internetAllowance, setInternetAllowance] = useState<string>('50.00');
  const [shortDayDeduction, setShortDayDeduction] = useState<string>(registryDaily.shortDayDeduction);

  // Compliance state
  const [attendanceMinRatio, setAttendanceMinRatio] = useState<string>(registryDaily.attendanceMinRatio);
  const [shiftComplianceMin, setShiftComplianceMin] = useState<string>(registryDaily.shiftComplianceMin);

  useEffect(() => {
    setDailyCommission(registryDaily.dailyCommission);
    setMinWorkingHours(registryDaily.minWorkingHours);
    setDailyCarRent(registryDaily.dailyCarRent);
    setDailyBikeRent(registryDaily.dailyBikeRent);
    setShortDayDeduction(registryDaily.shortDayDeduction);
    setAttendanceMinRatio(registryDaily.attendanceMinRatio);
    setShiftComplianceMin(registryDaily.shiftComplianceMin);
    setMonthDaysMode(registryDaily.monthDaysMode);
  }, [registryDaily]);

  // Notes state
  const [notes, setNotes] = useState<string>(
    'تسري هذه اللائحة على كافة مناديب التشغيل المعتمدين لدى المنصة. المصدر الوحيد للاحتساب هو سجل الشروط والأنظمة والأهلية والإعدادات العامة المعتمدة لنفس الشهر — لا إعدادات مكررة هنا.'
  );

  // Account exceptions mirror (synthetic demo ids only) — real edits go through the exception modal / assignments API
  const [localExceptions, setLocalExceptions] = useState<Array<{
    id: string;
    rider: string;
    ruleType: string;
    dailyAmount: number;
    vehicleRent: number;
    validFrom: string;
  }>>([]);

  const handleSaveWorkspace = () => {
    toast(
      'هذا السجل عرض للقراءة من إعدادات الشهر المعتمدة. عدّل الشروط أو الأنظمة أو الأهلية أو الإعدادات العامة من تبويباتها ثم أعد التحميل.',
      'info'
    );
  };

  const sourceLinks = [
    {
      key: 'terms',
      label: 'شروط التشغيل',
      ready: Boolean(activeTermsDoc),
      detail: activeTermsDoc
        ? `${activeTermsDoc.status === 'approved' ? 'معتمد' : 'مسودة'} v${activeTermsDoc.version}`
        : 'غير محمّل',
    },
    {
      key: 'scheme',
      label: 'الأنظمة الداخلية',
      ready: Boolean(activeSchemeDoc),
      detail: activeSchemeDoc
        ? `${activeSchemeDoc.status === 'approved' ? 'معتمد' : 'مسودة'} v${activeSchemeDoc.version}`
        : 'غير محمّل',
    },
    {
      key: 'eligibility',
      label: 'الأهلية',
      ready: Boolean(activeEligibilityDoc),
      detail: activeEligibilityDoc
        ? `${activeEligibilityDoc.status === 'approved' ? 'معتمد' : 'مسودة'} v${activeEligibilityDoc.version || ''}`
        : 'غير محمّل',
      open: 'eligibility' as const,
    },
    {
      key: 'global',
      label: 'الإعدادات العامة',
      ready: Boolean(activeGlobalDoc),
      detail: activeGlobalDoc
        ? `${activeGlobalDoc.status === 'approved' ? 'معتمد' : 'مسودة'} v${activeGlobalDoc.version}`
        : 'غير محمّل',
      open: 'global' as const,
    },
  ];

  const tabsMeta: Array<{ key: RuleTabKey; label: string; icon: React.ReactNode }> = [
    { key: 'daily', label: 'القواعد اليومية', icon: <Clock size={14} /> },
    { key: 'monthly', label: 'القواعد الشهرية', icon: <Calendar size={14} /> },
    { key: 'levels', label: 'الشرائح والمستويات', icon: <Layers size={14} /> },
    { key: 'incentives', label: 'الحوافز والاستقطاعات', icon: <Award size={14} /> },
    { key: 'compliance', label: 'الامتثال والضوابط', icon: <ShieldCheck size={14} /> },
    { key: 'exceptions', label: 'استثناءات الحسابات', icon: <User size={14} /> },
    { key: 'performance', label: 'سجل الأداء الشهري', icon: <TrendingUp size={14} /> },
    { key: 'notes', label: 'ملاحظات وتوجيهات', icon: <FileText size={14} /> },
  ];

  return (
    <div className="space-y-5">
      {/* Registry source banner */}
      <div className="p-3.5 rounded-2xl bg-[var(--accent)]/10 border border-[var(--accent)]/20 text-xs text-[var(--ink)] space-y-2">
        <div className="flex items-start gap-2.5">
          <Link2 size={16} className="text-[var(--accent)] shrink-0 mt-0.5" />
          <div className="leading-relaxed">
            <strong>مصدر واحد لشهر {period} · منصة {displayName}:</strong> محرك الرواتب وتبويبات الشروط/الأهلية/الأنظمة تقرأ من نفس السجل المعتمد لهذا الشهر — لا إعدادات مكررة في هذه الشاشة.
          </div>
        </div>
        <div className="flex flex-wrap gap-2 ps-6">
          {sourceLinks.map((s) => (
            <button
              key={s.key}
              type="button"
              disabled={!s.open || !onOpenSection}
              onClick={() => s.open && onOpenSection?.(s.open)}
              className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg border text-[11px] font-bold transition ${
                s.ready
                  ? 'border-[var(--money-pos)]/30 bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                  : 'border-[var(--line)] bg-[var(--raised)] text-[var(--muted)]'
              } ${s.open && onOpenSection ? 'cursor-pointer hover:opacity-90' : 'cursor-default'}`}
            >
              {s.ready ? <CheckCircle2 size={12} /> : <AlertTriangle size={12} />}
              <span>
                {s.label}: {s.detail}
              </span>
            </button>
          ))}
        </div>
      </div>

      {/* Main Record Header Card */}
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--line)] pb-4">
          <div className="space-y-1">
            <span className="text-[11px] text-[var(--muted)]">سجل القواعد والضوابط الموحد</span>
            <input
              type="text"
              value={ruleName}
              onChange={(e) => setRuleName(e.target.value)}
              className="text-base sm:text-lg font-bold text-[var(--ink)] bg-transparent border-b border-transparent hover:border-[var(--line)] focus:border-[var(--accent)] focus:outline-hidden px-1"
            />
          </div>

          <div className="flex items-center gap-2">
            <label className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-bold cursor-pointer">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="h-3.5 w-3.5 rounded accent-[var(--money-pos)] cursor-pointer"
              />
              <span className={isActive ? 'text-[var(--money-pos)]' : 'text-[var(--muted)]'}>
                {isActive ? 'القاعدة نشطة' : 'القاعدة معطلة'}
              </span>
            </label>

            <button
              type="button"
              onClick={handleSaveWorkspace}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <Save size={14} />
              <span>حفظ سجل القاعدة</span>
            </button>
          </div>
        </div>

        {/* Header Metadata Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-5 gap-3 text-xs">
          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">المنصة / التطبيق:</label>
            <select
              value={selectedApp}
              onChange={(e) => setSelectedApp(e.target.value)}
              disabled
              title="تتحدد المنصة من تبويب المنصات المختار أعلاه"
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-semibold opacity-90"
            >
              <option value="ninja">نينجا</option>
              <option value="keeta">كيتا</option>
              <option value="hs">هنقرستيشن</option>
              <option value="jahez">جاهز</option>
              <option value="chefz">ذا شيفز</option>
              <option value="amazon">أمازون</option>
            </select>
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">نطاق المدن:</label>
            <select
              value={selectedCity}
              onChange={(e) => setSelectedCity(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-semibold"
            >
              <option value="all">كل المدن والمناطق</option>
              <option value="RUH">منطقة الرياض (RUH)</option>
              <option value="JED">منطقة مكة وجدة (JED)</option>
              <option value="DMM">المنطقة الشرقية (DMM)</option>
              <option value="MED">المدينة المنورة (MED)</option>
            </select>
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">سريان من تاريخ:</label>
            <input
              type="date"
              value={validFrom}
              onChange={(e) => setValidFrom(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono"
            />
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">سريان إلى تاريخ:</label>
            <input
              type="date"
              value={validTo}
              placeholder="مفتوح"
              onChange={(e) => setValidTo(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono"
            />
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">عند غياب التقرير اليومي:</label>
            <select
              value={onMissingReport}
              onChange={(e) => setOnMissingReport(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-semibold"
            >
              <option value="flag_decision">تعليق المندوب لقرار مالي</option>
              <option value="zero">تصفير اليوم (بدون استحقاق)</option>
              <option value="prev_avg">احتساب متوسط الشهر السابق</option>
            </select>
          </div>
        </div>
      </div>

      {/* Tabs Navigation */}
      <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar border-b border-[var(--line)] pb-2">
        {tabsMeta.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setActiveTab(t.key)}
            className={`px-3.5 py-2 rounded-xl text-xs font-bold transition flex items-center gap-1.5 whitespace-nowrap cursor-pointer ${
              activeTab === t.key
                ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                : 'bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)]'
            }`}
          >
            {t.icon}
            <span>{t.label}</span>
          </button>
        ))}
      </div>

      {/* Tab 1: Daily Rules */}
      {activeTab === 'daily' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">القواعد اليومية لساعات التشغيل والعمولات</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              {registryDaily.commissionIsPercent
                ? 'ضوابط احتساب اليوم التشغيلي، نسبة عمولة الشركة من مستحق المندوب قبل الاستقطاعات، وإيجارات المركبات.'
                : 'ضوابط احتساب اليوم التشغيلي، عمولة الشركة اليومية، وإيجارات المركبات.'}
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">
                {registryDaily.commissionIsPercent
                  ? 'عمولة الشركة (% من مستحق المندوب من اليوزر قبل الاستقطاعات):'
                  : 'العمولة اليومية للشركة (ر.س):'}
              </label>
              <input
                type="number"
                step="0.01"
                value={dailyCommission}
                onChange={(e) => setDailyCommission(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
                aria-label={
                  registryDaily.commissionIsPercent
                    ? 'عمولة الشركة بالنسبة المئوية'
                    : 'العمولة اليومية للشركة بالريال'
                }
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">الحد الأدنى لساعات العمل اليومية:</label>
              <input
                type="number"
                value={minWorkingHours}
                onChange={(e) => setMinWorkingHours(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">إيجار سيارة الشركة اليومي (ر.س):</label>
              <input
                type="number"
                step="0.01"
                value={dailyCarRent}
                onChange={(e) => setDailyCarRent(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">إيجار دباب الشركة اليومي (ر.س):</label>
              <input
                type="number"
                step="0.01"
                value={dailyBikeRent}
                onChange={(e) => setDailyBikeRent(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num"
              />
            </div>
          </div>
        </div>
      )}

      {/* Tab 2: Monthly Rules */}
      {activeTab === 'monthly' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">القواعد الشهرية والأيام الإلزامية</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              تحديد التارجت الشهري وأيام العمل الإلزامية المطلوبة لاستحقاق الراتب أو الحوافز.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">الهدف الشهري للطلبات:</label>
              <input
                type="number"
                value={targetOrders}
                onChange={(e) => setTargetOrders(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">أيام العمل الإلزامية شهرياً:</label>
              <input
                type="number"
                value={mandatoryWorkingDays}
                onChange={(e) => setMandatoryWorkingDays(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">أسلوب احتساب أيام الشهر:</label>
              <select
                value={monthDaysMode}
                onChange={(e) => setMonthDaysMode(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] font-semibold"
              >
                <option value="calendar">أيام تقويمية حسب أيام الشهر الفعلية (28–31)</option>
                <option value="standard">30 يوماً قياسياً لكافة الشهور</option>
              </select>
            </div>
          </div>
        </div>
      )}

      {/* Tab 3: Levels & Bands */}
      {activeTab === 'levels' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">الشرائح ومستويات الفوترة والخصم</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              مأخوذة من عقد المنصة والنظام الداخلي المعتمدين للشهر.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-[var(--raised)]/40 border border-[var(--line)] text-xs space-y-2">
            <div className="font-bold text-[var(--ink)]">بيانات العقد المعتمد للدورة:</div>
            <div className="text-[var(--muted)] leading-relaxed">
              {activeTermsDoc ? (
                <span>
                  العقد المعتمد: <strong>{activeTermsDoc.data?.name || activeTermsDoc.code}</strong> (إصدار #{activeTermsDoc.version} · نموذج: {activeTermsDoc.data?.billingModel})
                </span>
              ) : (
                <span className="text-[var(--warning)]">لم يتم العثور على عقد معتمد للدورة بعد.</span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Tab 4: Incentives & Deductions */}
      {activeTab === 'incentives' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">الحوافز وبدلات التشغيل والاستقطاعات</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              أسقف الوقود، بدلات الاتصال، ونسب خصم اليوم غير المكتمل.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">سقف الوقود الشهري (ر.س):</label>
              <input
                type="number"
                step="0.01"
                value={fuelCap}
                onChange={(e) => setFuelCap(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">بدل الإنترنت الشهري (ر.س):</label>
              <input
                type="number"
                step="0.01"
                value={internetAllowance}
                onChange={(e) => setInternetAllowance(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">نسبة خصم اليوم القصير (&lt;8 ساعات) %:</label>
              <input
                type="number"
                value={shortDayDeduction}
                onChange={(e) => setShortDayDeduction(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
          </div>
        </div>
      )}

      {/* Tab 5: Compliance */}
      {activeTab === 'compliance' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">ضوابط الامتثال والالتزام بالشفتات</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              مؤشرات الحضور ونسبة الالتزام بالشفتات التشغيلية المعتمدة للمنصة.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">الحد الأدنى لنسبة الحضور (0.00–1.00):</label>
              <input
                type="number"
                step="0.01"
                value={attendanceMinRatio}
                onChange={(e) => setAttendanceMinRatio(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">الحد الأدنى لالتزام الشفتات %:</label>
              <input
                type="number"
                value={shiftComplianceMin}
                onChange={(e) => setShiftComplianceMin(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
              />
            </div>
          </div>
        </div>
      )}

      {/* Tab 6: Exceptions */}
      {activeTab === 'exceptions' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="flex items-center justify-between border-b border-[var(--line)] pb-3">
            <div>
              <h4 className="font-bold text-sm text-[var(--ink)]">استثناءات الحسابات والمناديب</h4>
              <p className="text-[11px] text-[var(--muted)] mt-0.5">
                تخصيص قواعد فردية لمندوب أو حساب معين مع الشركة والمنصة.
              </p>
            </div>

            <button
              type="button"
              onClick={onOpenExceptionModal}
              className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <Plus size={14} />
              <span>إضافة استثناء جديد</span>
            </button>
          </div>

          {localExceptions.length === 0 ? (
            <div className="p-6 text-center text-[var(--muted)]">لا توجد استثناءات فردية مسجلة لهذه القاعدة.</div>
          ) : (
            <DataTable
              tableId="rule-exceptions"
              data={localExceptions}
              getRowId={(ex) => ex.id}
              pageSize={10}
              columns={[
                {
                  key: 'rider',
                  header: 'المندوب',
                  type: 'text',
                  getValue: (ex) => ex.rider || '',
                  render: (ex) => <span className="font-mono font-bold text-[var(--ink)]">{ex.rider}</span>,
                },
                {
                  key: 'ruleType',
                  header: 'نوع القاعدة',
                  type: 'list',
                  getValue: (ex) => ex.ruleType || '',
                  render: (ex) => <span>{ex.ruleType}</span>,
                },
                {
                  key: 'dailyAmount',
                  header: 'المبلغ اليومي',
                  type: 'number',
                  numeric: true,
                  getValue: (ex) => Number(ex.dailyAmount ?? 0),
                  render: (ex) => <span className="num font-bold text-[var(--accent)]">{formatSar(ex.dailyAmount)}</span>,
                },
                {
                  key: 'vehicleRent',
                  header: 'إيجار المركبة',
                  type: 'number',
                  numeric: true,
                  getValue: (ex) => Number(ex.vehicleRent ?? 0),
                  render: (ex) => <span className="num">{formatSar(ex.vehicleRent)}</span>,
                },
                {
                  key: 'validFrom',
                  header: 'سريان من',
                  type: 'date',
                  getValue: (ex) => ex.validFrom || '',
                  render: (ex) => <span className="font-mono text-[var(--muted)]">{ex.validFrom}</span>,
                },
              ]}
            />
          )}
        </div>
      )}

      {/* Tab 7: Monthly Performance Record */}
      {activeTab === 'performance' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">سجل الأداء الشهري لكل حساب</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              مراجعة وتدقيق سجل أداء الحسابات الشهرية مقارنة بالتارجت والامتثال.
            </p>
          </div>

          <div className="p-6 text-center bg-[var(--raised)]/40 rounded-xl border border-[var(--line)] space-y-2">
            <Clock size={28} className="mx-auto text-[var(--accent)] opacity-80" />
            <div className="font-bold text-sm text-[var(--ink)]">قيد التطوير</div>
            <p className="text-[11px] text-[var(--muted)] max-w-md mx-auto">
              سجل الأداء الشهري التفصيلي لكل حساب (طلبات، ورديات، ساعات، ونسب الامتثال) سيظهر هنا عند اكتمال نقطة النهاية في خادم الرواتب.
            </p>
          </div>
        </div>
      )}

      {/* Tab 8: Notes */}
      {activeTab === 'notes' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">ملاحظات وتوجيهات تشغيلية ومحاسبية</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              توثيق القرارات والتفاصيل الإدارية الخاصة بهذه اللائحة.
            </p>
          </div>

          <textarea
            rows={5}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className="w-full p-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs leading-relaxed focus:outline-hidden"
            placeholder="اكتب التوجيهات التشغيلية والملاحظات الخاصة بهذه اللائحة..."
          />
        </div>
      )}
    </div>
  );
}
