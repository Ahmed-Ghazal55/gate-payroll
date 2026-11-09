'use client';

import React, { useState } from 'react';
import {
  FileText,
  Calendar,
  MapPin,
  Clock,
  Layers,
  Award,
  ShieldCheck,
  AlertTriangle,
  Plus,
  Save,
  CheckCircle2,
  Trash2,
  Lock,
  User,
  DollarSign,
  TrendingUp,
} from 'lucide-react';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { useToast } from '../../components/ui/Toast';
import type { PayConfigDoc } from '../../lib/types';

export interface RuleRecordWorkspaceProps {
  appSlug: string;
  appNameAr: string;
  period: string;
  onOpenExceptionModal: () => void;
  activeEligibilityDoc?: any | null;
  activeTermsDoc?: PayConfigDoc | null;
  activeSchemeDoc?: PayConfigDoc | null;
}

export function RuleRecordWorkspace({
  appSlug,
  appNameAr,
  period,
  onOpenExceptionModal,
  activeEligibilityDoc,
  activeTermsDoc,
  activeSchemeDoc,
}: RuleRecordWorkspaceProps) {
  const { toast } = useToast();

  // Rule Record Header state
  const [ruleName, setRuleName] = useState<string>(`لائحة قواعد وتشغيل ${appNameAr} · ${period}`);
  const [selectedApp, setSelectedApp] = useState<string>(appSlug);
  const [selectedCity, setSelectedCity] = useState<string>('all');
  const [validFrom, setValidFrom] = useState<string>(`${period}-01`);
  const [validTo, setValidTo] = useState<string>('');
  const [isActive, setIsActive] = useState<boolean>(true);
  const [onMissingReport, setOnMissingReport] = useState<string>('flag_decision');

  // Sub-tabs
  type RuleTabKey = 'daily' | 'monthly' | 'levels' | 'incentives' | 'compliance' | 'exceptions' | 'performance' | 'notes';
  const [activeTab, setActiveTab] = useState<RuleTabKey>('daily');

  // Daily rules state
  const [dailyCommission, setDailyCommission] = useState<string>('10.00');
  const [minWorkingHours, setMinWorkingHours] = useState<string>('8');
  const [dailyCarRent, setDailyCarRent] = useState<string>('60.00');
  const [dailyBikeRent, setDailyBikeRent] = useState<string>('26.67');

  // Monthly rules state
  const [targetOrders, setTargetOrders] = useState<string>('450');
  const [mandatoryWorkingDays, setMandatoryWorkingDays] = useState<string>('26');
  const [monthDaysMode, setMonthDaysMode] = useState<string>('calendar');

  // Incentives state
  const [fuelCap, setFuelCap] = useState<string>('400.00');
  const [internetAllowance, setInternetAllowance] = useState<string>('50.00');
  const [shortDayDeduction, setShortDayDeduction] = useState<string>('50');

  // Compliance state
  const [attendanceMinRatio, setAttendanceMinRatio] = useState<string>('0.80');
  const [shiftComplianceMin, setShiftComplianceMin] = useState<string>('85');

  // Notes state
  const [notes, setNotes] = useState<string>(
    'تسري هذه اللائحة على كافة مناديب التشغيل المعتمدين لدى المنصة. تُستبعد الأيام التي يقل فيها العمل عن 4 ساعات وتُخصم بنسبة 50% للأيام بين 4 و8 ساعات وفق المعايير.'
  );

  // Demo Account Exceptions state
  const [localExceptions, setLocalExceptions] = useState<Array<{
    id: string;
    rider: string;
    ruleType: string;
    dailyAmount: number;
    vehicleRent: number;
    validFrom: string;
  }>>([
    {
      id: 'exc-demo-01',
      rider: '2000000001',
      ruleType: 'عمولة يومية مخصصة',
      dailyAmount: 12.5,
      vehicleRent: 50.0,
      validFrom: `${period}-01`,
    },
  ]);

  const handleSaveWorkspace = () => {
    toast(`تم حفظ مسودة سجل القواعد للائحة «${ruleName}» بنجاح.`, 'success');
  };

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
      {/* P6 Notice Banner */}
      <div className="p-3.5 rounded-2xl bg-[var(--accent)]/10 border border-[var(--accent)]/20 text-xs text-[var(--ink)] flex items-start gap-2.5">
        <AlertTriangle size={16} className="text-[var(--accent)] shrink-0 mt-0.5" />
        <div className="leading-relaxed">
          <strong>ملاحظة هيكلة السجلات (Ghazal 2026-11-08):</strong> يتم تخزين محددات الفوترة، الأنظمة، والأهلية
          مباشرة عبر API الرواتب المعتمد. الحقول الموسعة (تحديد المدينة، التواريخ الممتدة، وسجل الأداء الشهري للحسابات)
          مربوطة شكلياً وموثقة تحت «طلبات Codex P6» لحين اكتمال الـ endpoints المقابلة.
        </div>
      </div>

      {/* Main Record Header Card */}
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--line)] pb-4">
          <div className="space-y-1">
            <span className="text-[11px] text-[var(--muted)] font-mono">سجل القواعد والضوابط الموحد (Rule Record)</span>
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
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-semibold"
            >
              <option value="ninja">نينجا (Ninja)</option>
              <option value="keeta">كيتا (Keeta)</option>
              <option value="hungerstation">هنقرستيشن (Hungerstation)</option>
              <option value="jahez">جاهز (Jahez)</option>
              <option value="amazon">أمازون (Amazon)</option>
            </select>
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--muted)] block">نطاق المدن:</label>
            <select
              value={selectedCity}
              onChange={(e) => setSelectedCity(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-semibold"
            >
              <option value="all">كل المدن والمناطق (All Cities)</option>
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
              ضوابط احتساب اليوم التشغيلي، عمولة الشركة اليومية، وإيجارات المركبات.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">العمولة اليومية للشركة (ر.س):</label>
              <input
                type="number"
                step="0.01"
                value={dailyCommission}
                onChange={(e) => setDailyCommission(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] num font-bold"
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
              <label className="font-bold text-[var(--ink)] block">الهدف الشهري للطلبات (Target Orders):</label>
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
              <h4 className="font-bold text-sm text-[var(--ink)]">استثناءات الحسابات والمناديب (Account Exceptions)</h4>
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
            <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
              <table className="w-full text-start text-xs">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)] text-[var(--muted)] font-semibold">
                  <tr>
                    <th className="p-2.5 text-start">المندوب</th>
                    <th className="p-2.5 text-start">نوع القاعدة</th>
                    <th className="p-2.5 text-start">المبلغ اليومي</th>
                    <th className="p-2.5 text-start">إيجار المركبة</th>
                    <th className="p-2.5 text-start">سريان من</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {localExceptions.map((ex) => (
                    <tr key={ex.id}>
                      <td className="p-2.5 font-mono font-bold text-[var(--ink)]">{ex.rider}</td>
                      <td className="p-2.5">{ex.ruleType}</td>
                      <td className="p-2.5 num font-bold text-[var(--accent)]">{ex.dailyAmount} ر.س</td>
                      <td className="p-2.5 num">{ex.vehicleRent} ر.س</td>
                      <td className="p-2.5 font-mono text-[var(--muted)]">{ex.validFrom}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Tab 7: Monthly Performance Record */}
      {activeTab === 'performance' && (
        <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 text-xs">
          <div className="border-b border-[var(--line)] pb-2">
            <h4 className="font-bold text-sm text-[var(--ink)]">سجل الأداء الشهري لكل حساب (Monthly Performance Record)</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              مراجعة وتدقيق سجل أداء الحسابات الشهرية مقارنة بالتارجت والامتثال.
            </p>
          </div>

          <div className="p-6 text-center bg-[var(--raised)]/40 rounded-xl border border-[var(--line)] space-y-2">
            <Clock size={28} className="mx-auto text-[var(--accent)] opacity-80" />
            <div className="font-bold text-sm text-[var(--ink)]">بانتظار تفعيل Endpoint سجل الأداء في Codex P6</div>
            <p className="text-[11px] text-[var(--muted)] max-w-md mx-auto">
              سيتم عرض سجل الأداء الشهري التفصيلي لكل حساب (طلبات، شفتات، ساعات، ونسب الامتثال) فور اكتمال نقطة النهاية المقابلة في خادم الرواتب P6.
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
