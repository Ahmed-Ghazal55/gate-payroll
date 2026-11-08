'use client';

import React, { useState, useMemo } from 'react';
import {
  Copy,
  CheckCircle2,
  AlertTriangle,
  Lock,
  Edit2,
  Check,
  X,
  ChevronDown,
  ChevronUp,
  FileText,
  Layers,
  ShieldAlert,
  Clock,
  Sparkles,
} from 'lucide-react';
import type { PayConfigDoc } from '../../lib/types';
import { diffPaySettings } from './PaySettingsValidation';
import type { PaySettingDiffItem } from './PaySettingsValidation';

export interface PaySettingsCycleReviewProps {
  period: string;
  previousPeriod: string;
  appSlug: string;
  appNameAr: string;
  currentUser?: string;
  termsList: PayConfigDoc[];
  schemesList: PayConfigDoc[];
  eligibilityList: PayConfigDoc[];
  prevTermsList?: PayConfigDoc[];
  prevSchemesList?: PayConfigDoc[];
  prevEligibilityList?: PayConfigDoc[];
  onCopyPreviousMonth: () => void;
  copying: boolean;
  onEditSetting: (doc: PayConfigDoc, kind: 'terms' | 'scheme' | 'eligibility') => void;
  onApproveSetting: (doc: PayConfigDoc, kind: 'terms' | 'scheme' | 'eligibility') => void;
  onRejectSetting: (doc: PayConfigDoc, kind: 'terms' | 'scheme' | 'eligibility') => void;
}

export function PaySettingsCycleReview({
  period,
  previousPeriod,
  appSlug,
  appNameAr,
  currentUser,
  termsList,
  schemesList,
  eligibilityList,
  prevTermsList = [],
  prevSchemesList = [],
  prevEligibilityList = [],
  onCopyPreviousMonth,
  copying,
  onEditSetting,
  onApproveSetting,
  onRejectSetting,
}: PaySettingsCycleReviewProps) {
  const [expandedDiffId, setExpandedDiffId] = useState<string | null>(null);

  // Combine items for the cycle
  const cycleItems = useMemo(() => {
    const items: Array<{
      id: string;
      title: string;
      kind: 'terms' | 'scheme' | 'eligibility';
      kindLabel: string;
      doc: PayConfigDoc;
      prevDoc?: PayConfigDoc;
      diffs: PaySettingDiffItem[];
    }> = [];

    // Terms
    for (const t of termsList) {
      const prev = prevTermsList.find((p) => p.code === t.code || p.id === t.copiedFromId);
      const diffs = prev ? diffPaySettings(prev.data, t.data) : [];
      items.push({
        id: t.id,
        title: t.data?.name || `عقد المنصة (${t.code})`,
        kind: 'terms',
        kindLabel: 'عقد المنصة',
        doc: t,
        prevDoc: prev,
        diffs,
      });
    }

    // Schemes
    for (const s of schemesList) {
      const prev = prevSchemesList.find((p) => p.code === s.code || p.id === s.copiedFromId);
      const diffs = prev ? diffPaySettings(prev.data, s.data) : [];
      items.push({
        id: s.id,
        title: s.data?.name || `النظام الداخلي (${s.code})`,
        kind: 'scheme',
        kindLabel: 'نظام داخلي',
        doc: s,
        prevDoc: prev,
        diffs,
      });
    }

    // Eligibility
    for (const el of eligibilityList) {
      const prev = prevEligibilityList.find((p) => p.code === el.code || p.id === el.copiedFromId);
      const diffs = prev ? diffPaySettings(prev.data, el.data) : [];
      items.push({
        id: el.id,
        title: el.data?.name || `شروط الأهلية (${el.code})`,
        kind: 'eligibility',
        kindLabel: 'شروط الأهلية',
        doc: el,
        prevDoc: prev,
        diffs,
      });
    }

    return items;
  }, [termsList, schemesList, eligibilityList, prevTermsList, prevSchemesList, prevEligibilityList]);

  const approvedCount = cycleItems.filter((i) => i.doc.status === 'approved').length;
  const pendingCount = cycleItems.filter((i) => i.doc.status === 'draft').length;

  return (
    <div className="space-y-4">
      {/* Header with cycle overview and Reuse button */}
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              إعدادات دورة شهر {period} · منصة {appNameAr}
            </h3>
            <span
              className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                pendingCount === 0 && cycleItems.length > 0
                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
              }`}
            >
              {pendingCount === 0 && cycleItems.length > 0
                ? 'كافة الإعدادات معتمدة'
                : `${pendingCount} إعدادات بانتظار الاعتماد`}
            </span>
          </div>
          <p className="text-xs text-[var(--muted)] mt-1">
            مراجعة حالة إعدادات الدورة (العقد، الأنظمة، الأهلية). فحص الفروقات عن شهر {previousPeriod} مع قاعدة Maker ≠ Checker.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            disabled={copying}
            onClick={onCopyPreviousMonth}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50 shadow-xs"
          >
            <Copy size={14} />
            <span>اعتماد نفس إعدادات الشهر السابق ({previousPeriod})</span>
          </button>
        </div>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs">
        <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
          <span className="text-[var(--muted)] block">إجمالي إعدادات الدورة:</span>
          <span className="text-base font-bold text-[var(--ink)] num">{cycleItems.length}</span>
        </div>
        <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
          <span className="text-[var(--muted)] block">إعدادات معتمدة:</span>
          <span className="text-base font-bold text-[var(--money-pos)] num">{approvedCount}</span>
        </div>
        <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
          <span className="text-[var(--muted)] block">بانتظار اعتماد المدقق:</span>
          <span className="text-base font-bold text-[var(--warning)] num">{pendingCount}</span>
        </div>
        <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
          <span className="text-[var(--muted)] block">نسخة المقارنة:</span>
          <span className="text-base font-bold text-[var(--muted)] font-mono">{previousPeriod}</span>
        </div>
      </div>

      {/* Settings list with state and diff */}
      {cycleItems.length === 0 ? (
        <div className="p-8 text-center bg-[var(--surface)] rounded-2xl border border-[var(--line)] text-xs text-[var(--muted)] space-y-3">
          <FileText size={32} className="mx-auto text-[var(--muted)] opacity-60" />
          <p className="font-semibold text-sm text-[var(--ink)]">لا توجد إعدادات مسجلة لدورة شهر {period}</p>
          <p>يمكنك بنقرة واحدة نسخ إعدادات شهر {previousPeriod} أو إضافة إعداد جديد من التبويبات بالأعلى.</p>
          <button
            type="button"
            disabled={copying}
            onClick={onCopyPreviousMonth}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition cursor-pointer"
          >
            اعتماد نفس إعدادات الشهر السابق
          </button>
        </div>
      ) : (
        <div className="space-y-3">
          {cycleItems.map((item) => {
            const isApproved = item.doc.status === 'approved';
            const isMaker = Boolean(
              currentUser &&
                item.doc.createdBy &&
                item.doc.createdBy.toLowerCase() === currentUser.toLowerCase()
            );
            const hasDiffs = item.diffs.length > 0;
            const isDiffExpanded = expandedDiffId === item.id;

            return (
              <div
                key={item.id}
                className={`p-4 rounded-2xl border transition shadow-xs space-y-3 ${
                  isApproved
                    ? 'border-[var(--line)] bg-[var(--surface)]'
                    : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                }`}
              >
                {/* Item Header */}
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex items-center gap-2">
                    <span className="text-xs px-2 py-0.5 rounded-md bg-[var(--raised)] text-[var(--muted)] font-semibold">
                      {item.kindLabel}
                    </span>
                    <h4 className="font-bold text-sm text-[var(--ink)]">{item.title}</h4>
                    <span className="text-xs text-[var(--muted)] font-mono">#{item.doc.version}</span>
                    <span
                      className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                        isApproved
                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                          : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                      }`}
                    >
                      {isApproved ? 'معتمدة' : 'بانتظار الاعتماد'}
                    </span>
                  </div>

                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => onEditSetting(item.doc, item.kind)}
                      className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--raised)] transition inline-flex items-center gap-1 cursor-pointer"
                    >
                      <Edit2 size={13} />
                      <span>{isApproved ? 'إنشاء إصدار معدّل' : 'تعديل الإعداد'}</span>
                    </button>

                    {!isApproved && (
                      <>
                        <button
                          type="button"
                          disabled={Boolean(isMaker)}
                          title={isMaker ? 'قاعدة Maker ≠ Checker: لا يمكن لمنشئ المسودة اعتمادها بنفسه' : 'اعتماد الإعداد'}
                          onClick={() => onApproveSetting(item.doc, item.kind)}
                          className="px-3.5 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer inline-flex items-center gap-1 shadow-xs"
                        >
                          <Check size={13} />
                          <span>اعتماد الإعداد</span>
                        </button>

                        <button
                          type="button"
                          onClick={() => onRejectSetting(item.doc, item.kind)}
                          className="px-2.5 py-1.5 rounded-xl border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                        >
                          رفض
                        </button>
                      </>
                    )}
                  </div>
                </div>

                {/* Metadata row */}
                <div className="flex flex-wrap items-center gap-4 text-[11px] text-[var(--muted)] border-t border-[var(--line)]/60 pt-2">
                  <div>
                    أنشئت بواسطة: <span className="font-semibold text-[var(--ink)]">@{item.doc.createdBy || 'system'}</span>
                    {item.doc.createdAt && <span className="font-mono mr-1">({item.doc.createdAt.slice(0, 10)})</span>}
                  </div>
                  {isApproved && item.doc.approvedBy && (
                    <div>
                      اعتمدت بواسطة: <span className="font-semibold text-[var(--money-pos)]">@{item.doc.approvedBy}</span>
                      {item.doc.approvedAt && <span className="font-mono mr-1">({item.doc.approvedAt.slice(0, 10)})</span>}
                    </div>
                  )}
                  {item.doc.copiedFromId && (
                    <div className="flex items-center gap-1">
                      <Sparkles size={11} className="text-[var(--accent)]" />
                      <span>مستنسخة من الشهر السابق</span>
                    </div>
                  )}
                </div>

                {/* Diff Viewer Card */}
                {item.prevDoc && (
                  <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)]/30 overflow-hidden text-xs">
                    <button
                      type="button"
                      onClick={() => setExpandedDiffId(isDiffExpanded ? null : item.id)}
                      className="w-full p-2.5 flex items-center justify-between text-start hover:bg-[var(--raised)]/60 transition cursor-pointer"
                    >
                      <div className="flex items-center gap-2">
                        {hasDiffs ? (
                          <span className="px-2 py-0.5 rounded-md bg-[var(--warning)]/15 text-[var(--warning)] font-bold text-[10px]">
                            فروقات عن شهر {previousPeriod} ({item.diffs.length} بنود مختلفة)
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 rounded-md bg-[var(--money-pos)]/15 text-[var(--money-pos)] font-bold text-[10px]">
                            مطابق تماماً لنسخة الشهر السابق دون أي تغييرات
                          </span>
                        )}
                        <span className="text-[11px] text-[var(--muted)]">
                          مقارنة مع الإصدار #{item.prevDoc.version} ({previousPeriod})
                        </span>
                      </div>
                      {isDiffExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                    </button>

                    {isDiffExpanded && hasDiffs && (
                      <div className="p-3 border-t border-[var(--line)] bg-[var(--surface)] overflow-x-auto">
                        <table className="w-full text-start text-xs">
                          <thead className="border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                            <tr>
                              <th className="p-2 text-start">البند / المعيار</th>
                              <th className="p-2 text-start">القيمة في {previousPeriod}</th>
                              <th className="p-2 text-start">القيمة في {period}</th>
                              <th className="p-2 text-start">الحالة</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-[var(--line)]">
                            {item.diffs.map((d, dIdx) => (
                              <tr key={dIdx}>
                                <td className="p-2 font-semibold text-[var(--ink)]">{d.labelAr}</td>
                                <td className="p-2 font-mono text-[var(--muted)]">{d.prevValue}</td>
                                <td className="p-2 font-mono font-bold text-[var(--accent)]">{d.curValue}</td>
                                <td className="p-2">
                                  <span
                                    className={`px-1.5 py-0.5 rounded text-[10px] font-bold ${
                                      d.kind === 'changed'
                                        ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                        : d.kind === 'added'
                                        ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                        : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                    }`}
                                  >
                                    {d.kind === 'changed' ? 'تعديل' : d.kind === 'added' ? 'إضافة' : 'حذف'}
                                  </span>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
