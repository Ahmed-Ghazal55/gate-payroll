'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Download, Grid3X3, LayoutGrid, Star, Users } from 'lucide-react';
import {
  api,
  type EvaluationAppScorecard,
  type EvaluationCityAppScorecard,
  type EvaluationHeatmapScorecard,
  type EvaluationScorecardFilters,
  type EvaluationSupervisorScorecard,
} from '../../lib/api';
import {
  FACTOR_LABELS_AR,
  STAFF_DIMENSION_LABELS_AR,
  platformDisplayName,
} from './evaluation-helpers';

export type ScorecardDrillDown = {
  target: string;
  city?: string;
  app?: string;
  department?: string;
  supervisorId?: string;
  nationalityId?: string;
};

type Props = {
  roundId: string;
  canExport: boolean;
  filters: EvaluationScorecardFilters;
  onDrillDown: (next: ScorecardDrillDown) => void;
  onToast: (message: string, tone?: 'success' | 'danger' | 'info') => void;
};

function fmt(n: number | null | undefined, digits = 2): string {
  if (n == null || Number.isNaN(Number(n))) return '—';
  return Number(n).toFixed(digits);
}

function pctBar(value: number | null | undefined, max: number): number {
  if (value == null || max <= 0) return 0;
  return Math.max(0, Math.min(100, Math.round((Number(value) / max) * 100)));
}

export function EvaluationScorecardsPanel({
  roundId,
  canExport,
  filters,
  onDrillDown,
  onToast,
}: Props) {
  const [supervisors, setSupervisors] = useState<EvaluationSupervisorScorecard | null>(null);
  const [apps, setApps] = useState<EvaluationAppScorecard | null>(null);
  const [heatmap, setHeatmap] = useState<EvaluationHeatmapScorecard | null>(null);
  const [cityApp, setCityApp] = useState<EvaluationCityAppScorecard | null>(null);
  const [loading, setLoading] = useState(false);
  const [exportingKind, setExportingKind] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!roundId) return;
    setLoading(true);
    try {
      const [s, a, h, m] = await Promise.all([
        api.getEvaluationSupervisorScorecard(roundId, filters),
        api.getEvaluationAppScorecard(roundId, filters),
        api.getEvaluationHeatmapScorecard(roundId, filters),
        api.getEvaluationCityAppScorecard(roundId, filters),
      ]);
      setSupervisors(s);
      setApps(a);
      setHeatmap(h);
      setCityApp(m);
    } catch (err: any) {
      onToast(err?.message || 'تعذر تحميل بطاقات المؤشرات', 'danger');
    } finally {
      setLoading(false);
    }
  }, [roundId, filters, onToast]);

  useEffect(() => {
    void load();
  }, [load]);

  const confidenceNote = useMemo(() => {
    return (
      supervisors?.confidenceNoteAr ||
      apps?.confidenceNoteAr ||
      heatmap?.confidenceNoteAr ||
      cityApp?.confidenceNoteAr ||
      ''
    );
  }, [supervisors, apps, heatmap, cityApp]);

  const exportKind = async (kind: 'supervisors' | 'apps' | 'heatmap' | 'city-app') => {
    if (!canExport) return;
    setExportingKind(kind);
    try {
      const blob = await api.exportEvaluationScorecard(roundId, kind, filters);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `evaluation-${kind}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      onToast('تم تنزيل بطاقة المؤشرات', 'success');
    } catch (err: any) {
      onToast(err?.message || 'تعذر تصدير بطاقة المؤشرات', 'danger');
    } finally {
      setExportingKind(null);
    }
  };

  const applyDrill = (raw: Record<string, unknown> | undefined) => {
    if (!raw) return;
    onDrillDown({
      target: String(raw.target || 'replies'),
      city: raw.city ? String(raw.city) : undefined,
      app: raw.app ? String(raw.app) : undefined,
      department: raw.department ? String(raw.department) : undefined,
      supervisorId: raw.supervisorId ? String(raw.supervisorId) : undefined,
      nationalityId: raw.nationalityId ? String(raw.nationalityId) : undefined,
    });
  };

  const heatmapStaff = useMemo(() => {
    const map = new Map<string, { name: string; dims: Record<string, number | null>; low: boolean }>();
    for (const cell of heatmap?.cells || []) {
      const row = map.get(cell.staffId) || {
        name: cell.name || cell.staffId,
        dims: {},
        low: false,
      };
      row.dims[cell.dimension] = cell.average ?? null;
      if (cell.lowSample) row.low = true;
      map.set(cell.staffId, row);
    }
    return [...map.entries()];
  }, [heatmap]);

  const cityAppGrid = useMemo(() => {
    const cities = cityApp?.cities || [];
    const platforms = cityApp?.apps || [];
    type Cell = NonNullable<EvaluationCityAppScorecard['cells']>[number];
    const lookup = new Map<string, Cell>();
    for (const cell of cityApp?.cells || []) {
      lookup.set(`${cell.city}||${cell.app}`, cell);
    }
    return { cities, platforms, lookup };
  }, [cityApp]);

  return (
    <section className="space-y-4" data-testid="eval-scorecards-panel">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-base font-bold text-[var(--ink)]">بطاقات المؤشرات التحليلية</h2>
          <p className="text-xs text-[var(--muted)]">
            تُحسب على الخادم من الردود بعد تطبيق الفلاتر الحالية. اضغط أي بطاقة للانتقال لجدول الردود المصفّى.
          </p>
        </div>
        {loading && <span className="text-xs text-[var(--muted)]">جاري تحديث المؤشرات…</span>}
      </div>

      {confidenceNote ? (
        <div
          className="rounded-xl border border-[var(--warning,#b45309)]/40 bg-[var(--warning,#b45309)]/10 px-3 py-2 text-xs text-[var(--ink)]"
          data-testid="eval-scorecard-confidence-note"
        >
          {confidenceNote}
        </div>
      ) : null}

      {/* Supervisor scorecards */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-bold text-[var(--ink)] flex items-center gap-1.5">
            <Users size={14} /> بطاقة أداء المشرف / الموظف
          </h3>
          {canExport && (
            <button
              type="button"
              disabled={exportingKind === 'supervisors'}
              onClick={() => void exportKind('supervisors')}
              data-testid="eval-export-scorecard-supervisors"
              className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2 py-1 text-[11px] font-bold"
            >
              <Download size={12} />
              {exportingKind === 'supervisors' ? 'جاري التصدير…' : 'تصدير إكسل'}
            </button>
          )}
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {(supervisors?.items || []).map((item) => (
            <button
              key={item.staffId}
              type="button"
              data-testid={`eval-supervisor-scorecard-${item.staffId}`}
              onClick={() => applyDrill(item.drillDown as Record<string, unknown>)}
              className="text-start rounded-xl border border-[var(--line)] bg-[var(--bg)] p-3 space-y-2 hover:border-[var(--accent)] transition"
            >
              <div className="flex items-start justify-between gap-2">
                <div>
                  <div className="text-sm font-bold text-[var(--ink)]">{item.name}</div>
                  <div className="text-[11px] text-[var(--muted)]">{item.department || '—'}</div>
                </div>
                <div className="text-end">
                  <div className="text-lg font-black num text-[var(--accent)]">
                    {fmt(item.average)}
                  </div>
                  <div className="text-[10px] text-[var(--muted)]">/ 10</div>
                </div>
              </div>
              <div className="flex flex-wrap gap-1 text-[10px]">
                <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                  ن={item.replyCount}
                </span>
                <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                  شكاوى {fmt(item.complaintRate, 2)}
                </span>
                {item.rankInDepartment != null && (
                  <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                    ترتيب {item.rankInDepartment}
                  </span>
                )}
                {item.changeVsPrevious != null && (
                  <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                    Δ {Number(item.changeVsPrevious) > 0 ? '+' : ''}
                    {fmt(item.changeVsPrevious)}
                  </span>
                )}
                {item.lowSample && (
                  <span className="rounded-full bg-[var(--danger)]/15 text-[var(--danger)] px-2 py-0.5 font-bold">
                    عينة صغيرة
                  </span>
                )}
              </div>
              <div className="space-y-1">
                {(item.dimensions || []).map((d) => (
                  <div key={d.key} className="space-y-0.5">
                    <div className="flex justify-between text-[10px]">
                      <span className="text-[var(--muted)]">
                        {STAFF_DIMENSION_LABELS_AR[d.key] || d.key}
                      </span>
                      <span className="num font-bold">{fmt(d.average)}</span>
                    </div>
                    <div className="h-1.5 rounded-full bg-[var(--raised)] overflow-hidden">
                      <div
                        className="h-full bg-[var(--accent)]"
                        style={{ width: `${pctBar(d.average, 10)}%` }}
                      />
                    </div>
                  </div>
                ))}
              </div>
            </button>
          ))}
          {(supervisors?.items || []).length === 0 && (
            <p className="text-xs text-[var(--muted)] col-span-full">لا توجد بطاقات مشرفين ضمن الفلتر الحالي.</p>
          )}
        </div>
      </div>

      {/* App scorecards */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-bold text-[var(--ink)] flex items-center gap-1.5">
            <Star size={14} /> بطاقة أداء المنصة
          </h3>
          {canExport && (
            <button
              type="button"
              disabled={exportingKind === 'apps'}
              onClick={() => void exportKind('apps')}
              data-testid="eval-export-scorecard-apps"
              className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2 py-1 text-[11px] font-bold"
            >
              <Download size={12} />
              {exportingKind === 'apps' ? 'جاري التصدير…' : 'تصدير إكسل'}
            </button>
          )}
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {(apps?.items || [])
            .filter((item) => Number(item.replyCount) > 0)
            .map((item) => (
              <button
                key={item.app}
                type="button"
                data-testid={`eval-app-scorecard-${item.app}`}
                onClick={() => applyDrill(item.drillDown as Record<string, unknown>)}
                className="text-start rounded-xl border border-[var(--line)] bg-[var(--bg)] p-3 space-y-2 hover:border-[var(--accent)] transition"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="text-sm font-bold">{platformDisplayName(item.app, 'ar')}</div>
                  <div className="text-end">
                    <div className="text-lg font-black num text-[var(--money-pos)]">
                      {fmt(item.average)}
                    </div>
                    <div className="text-[10px] text-[var(--muted)]">/ 5 ★</div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-1 text-[10px]">
                  <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                    ن={item.replyCount}
                  </span>
                  <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 num">
                    صافي {fmt(item.netScore)}
                  </span>
                  {item.lowSample && (
                    <span className="rounded-full bg-[var(--danger)]/15 text-[var(--danger)] px-2 py-0.5 font-bold">
                      عينة صغيرة
                    </span>
                  )}
                </div>
                <div className="grid grid-cols-5 gap-1">
                  {(item.distribution || []).map((d) => (
                    <div key={d.stars} className="text-center">
                      <div className="h-10 flex items-end justify-center">
                        <div
                          className="w-3 rounded-t bg-[var(--accent)]"
                          style={{
                            height: `${pctBar(d.count, Math.max(1, ...((item.distribution || []).map((x) => Number(x.count) || 0))) )}%`,
                            minHeight: Number(d.count) > 0 ? 4 : 0,
                          }}
                        />
                      </div>
                      <div className="text-[9px] num text-[var(--muted)]">{d.stars}★</div>
                      <div className="text-[9px] num font-bold">{d.count}</div>
                    </div>
                  ))}
                </div>
                <div className="grid grid-cols-2 gap-2 text-[10px]">
                  <div>
                    <div className="text-[var(--muted)] mb-0.5">أعلى العوامل</div>
                    {(item.topFactors || []).slice(0, 2).map((f) => (
                      <div key={f.key} className="flex justify-between gap-1">
                        <span>{FACTOR_LABELS_AR[f.key] || f.key}</span>
                        <span className="num font-bold">{fmt(f.average)}</span>
                      </div>
                    ))}
                  </div>
                  <div>
                    <div className="text-[var(--muted)] mb-0.5">أدنى العوامل</div>
                    {(item.bottomFactors || []).slice(0, 2).map((f) => (
                      <div key={f.key} className="flex justify-between gap-1">
                        <span>{FACTOR_LABELS_AR[f.key] || f.key}</span>
                        <span className="num font-bold">{fmt(f.average)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </button>
            ))}
        </div>
      </div>

      {/* Heatmap */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 overflow-x-auto">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-bold text-[var(--ink)] flex items-center gap-1.5">
            <Grid3X3 size={14} /> خريطة حرارية: مشرف × بُعد
          </h3>
          {canExport && (
            <button
              type="button"
              disabled={exportingKind === 'heatmap'}
              onClick={() => void exportKind('heatmap')}
              data-testid="eval-export-scorecard-heatmap"
              className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2 py-1 text-[11px] font-bold"
            >
              <Download size={12} />
              {exportingKind === 'heatmap' ? 'جاري التصدير…' : 'تصدير إكسل'}
            </button>
          )}
        </div>
        {heatmapStaff.length === 0 ? (
          <p className="text-xs text-[var(--muted)]">لا بيانات للخريطة الحرارية.</p>
        ) : (
          <div className="min-w-[36rem] text-[11px]" data-testid="eval-heatmap-table" role="grid">
            <div
              className="grid gap-1 text-[var(--muted)] font-semibold"
              style={{
                gridTemplateColumns: `minmax(8rem,1.4fr) repeat(${(heatmap?.dimensions || Object.keys(STAFF_DIMENSION_LABELS_AR)).length}, minmax(4.5rem,1fr))`,
              }}
              role="row"
            >
              <div className="p-2" role="columnheader">
                الموظف
              </div>
              {(heatmap?.dimensions || Object.keys(STAFF_DIMENSION_LABELS_AR)).map((d) => (
                <div key={d} className="p-2 text-center" role="columnheader">
                  {STAFF_DIMENSION_LABELS_AR[d] || d}
                </div>
              ))}
            </div>
            {heatmapStaff.map(([staffId, row]) => (
              <div
                key={staffId}
                className="grid gap-1 border-t border-[var(--line)] items-center"
                style={{
                  gridTemplateColumns: `minmax(8rem,1.4fr) repeat(${(heatmap?.dimensions || Object.keys(STAFF_DIMENSION_LABELS_AR)).length}, minmax(4.5rem,1fr))`,
                }}
                role="row"
              >
                <div className="p-2" role="rowheader">
                  <button
                    type="button"
                    className="font-bold text-[var(--ink)] hover:text-[var(--accent)]"
                    onClick={() => onDrillDown({ target: 'replies', supervisorId: staffId })}
                  >
                    {row.name}
                    {row.low ? (
                      <span className="ms-1 text-[var(--danger)] text-[9px]">عينة صغيرة</span>
                    ) : null}
                  </button>
                </div>
                {(heatmap?.dimensions || Object.keys(STAFF_DIMENSION_LABELS_AR)).map((d) => {
                  const avg = row.dims[d];
                  const intensity = pctBar(avg, 10);
                  return (
                    <div key={d} className="p-1" role="gridcell">
                      <button
                        type="button"
                        onClick={() => onDrillDown({ target: 'replies', supervisorId: staffId })}
                        className="w-full rounded-md px-1 py-2 num font-bold"
                        style={{
                          background: `color-mix(in srgb, var(--accent) ${intensity}%, var(--raised))`,
                        }}
                      >
                        {fmt(avg)}
                      </button>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* City × app matrix */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 overflow-x-auto">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-bold text-[var(--ink)] flex items-center gap-1.5">
            <LayoutGrid size={14} /> مصفوفة المدينة × المنصة
          </h3>
          {canExport && (
            <button
              type="button"
              disabled={exportingKind === 'city-app'}
              onClick={() => void exportKind('city-app')}
              data-testid="eval-export-scorecard-city-app"
              className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2 py-1 text-[11px] font-bold"
            >
              <Download size={12} />
              {exportingKind === 'city-app' ? 'جاري التصدير…' : 'تصدير إكسل'}
            </button>
          )}
        </div>
        {cityAppGrid.cities.length === 0 ? (
          <p className="text-xs text-[var(--muted)]">لا بيانات للمصفوفة.</p>
        ) : (
          <div className="min-w-[36rem] text-[11px]" data-testid="eval-city-app-matrix" role="grid">
            <div
              className="grid gap-1 text-[var(--muted)] font-semibold"
              style={{
                gridTemplateColumns: `minmax(7rem,1.2fr) repeat(${cityAppGrid.platforms.length}, minmax(5rem,1fr))`,
              }}
              role="row"
            >
              <div className="p-2" role="columnheader">
                المدينة
              </div>
              {cityAppGrid.platforms.map((p) => (
                <div key={p} className="p-2 text-center" role="columnheader">
                  {platformDisplayName(p, 'ar')}
                </div>
              ))}
            </div>
            {cityAppGrid.cities.map((city) => (
              <div
                key={city}
                className="grid gap-1 border-t border-[var(--line)] items-center"
                style={{
                  gridTemplateColumns: `minmax(7rem,1.2fr) repeat(${cityAppGrid.platforms.length}, minmax(5rem,1fr))`,
                }}
                role="row"
              >
                <div className="p-2 font-bold" role="rowheader">
                  {city}
                </div>
                {cityAppGrid.platforms.map((platform) => {
                  const cell = cityAppGrid.lookup.get(`${city}||${platform}`);
                  return (
                    <div key={platform} className="p-1" role="gridcell">
                      <button
                        type="button"
                        data-testid={`eval-city-app-cell-${city}-${platform}`}
                        onClick={() =>
                          applyDrill(
                            (cell?.drillDown as Record<string, unknown>) || {
                              target: 'replies',
                              city,
                              app: platform,
                            }
                          )
                        }
                        className="w-full rounded-md border border-[var(--line)] bg-[var(--bg)] px-1 py-2 hover:border-[var(--accent)]"
                      >
                        <div className="num font-black">{fmt(cell?.average)}</div>
                        <div className="text-[9px] text-[var(--muted)] num">
                          ن={cell?.replyCount ?? 0}
                          {cell?.lowSample ? ' · عينة صغيرة' : ''}
                        </div>
                      </button>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
