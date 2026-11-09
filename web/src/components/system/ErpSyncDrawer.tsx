'use client';

import React, { useState, useEffect, useRef } from 'react';
import {
  RefreshCw,
  Eye,
  Play,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Clock,
  Database,
  ArrowUpDown,
  Loader2,
  ShieldAlert,
} from 'lucide-react';
import { Drawer } from '../ui/Drawer';
import { api, ApiError } from '../../lib/api';
import type { ErpSyncJobStatus, ErpSyncRun, ErpSyncCount } from '../../lib/types';

export interface ErpSyncDrawerProps {
  open: boolean;
  onClose: () => void;
}

const REFUSAL_MESSAGES: Record<string, string> = {
  cooldown: 'فترة التهدئة نشطة؛ يرجى الانتظار قبل إعادة المحاولة.',
  job_already_running: 'توجد مهمة مزامنة أخرى قيد التشغيل حالياً.',
  erp_sync_paused_or_disabled: 'المزامنة من ERP متوقفة مؤقتاً أو غير مفعلة في إعدادات النظام.',
  legacy_schedule_disabled: 'الجدولة القديمة غير مفعلة.',
  job_timeout: 'انتهت مهلة التنفيذ المحددة للمهمة (30 دقيقة).',
  job_cancelled: 'تم إلغاء المهمة قبل اكتمالها.',
  enqueue_failed: 'تعذر إدراج المهمة في طابور المعالجة.',
  interrupted_job: 'تمت مقاطعة المهمة قبل اكتمالها.',
  operation_failed: 'فشلت العملية؛ راجع سجل أخطاء الخادم.',
  unapproved_target: 'الهدف مقفل أو بانتظار الاعتماد المالي.',
};

// The guard refuses with `code:kind`; the kind is what an administrator can accept for one run.
function refusalParts(code: string): { message: string; kind: string; field: 'acceptNewKinds' | 'acceptLargeChanges' | '' } {
  const [head, kind = ''] = code.split(':');
  if (head === 'new_kind_requires_acceptance') return { message: `نوع بيانات جديد يحتاج قبولك أول مرة: ${kind}`, kind, field: 'acceptNewKinds' };
  if (head === 'kind_changes_over_threshold') return { message: `التغييرات في ${kind} أكبر من حد الأمان 10%.`, kind, field: 'acceptLargeChanges' };
  if (head === 'source_count_drop_over_threshold') return { message: `عدد سجلات ${kind} في ERP نقص عن الموجود عندنا أكثر من الحد.`, kind, field: 'acceptLargeChanges' };
  return { message: REFUSAL_MESSAGES[code] || code, kind: '', field: '' };
}

const KIND_LABELS: Record<string, string> = {
  riders: 'المناديب',
  rider: 'المناديب',
  companies: 'الشركات والكفلاء',
  company: 'الشركات والكفلاء',
  accounts: 'الحسابات المالية',
  account: 'الحسابات المالية',
  shifts: 'المناوبات وساعات العمل',
  shift: 'المناوبات وساعات العمل',
  contracts: 'العقود والاتفاقيات',
  contract: 'العقود والاتفاقيات',
};

export function ErpSyncDrawer({ open, onClose }: ErpSyncDrawerProps) {
  const [jobsEnabled, setJobsEnabled] = useState<boolean | null>(null);
  const [loadingHealth, setLoadingHealth] = useState(false);

  const [triggering, setTriggering] = useState<'dry_run' | 'commit' | null>(null);
  const [triggerError, setTriggerError] = useState<string | null>(null);

  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [activeJob, setActiveJob] = useState<ErpSyncJobStatus | null>(null);

  const [recentRuns, setRecentRuns] = useState<ErpSyncRun[]>([]);
  const [loadingRecent, setLoadingRecent] = useState(false);

  const pollIntervalRef = useRef<NodeJS.Timeout | null>(null);

  // Check health on open
  useEffect(() => {
    if (!open) return;
    setLoadingHealth(true);
    setTriggerError(null);

    api
      .getHealth()
      .then((h) => {
        setJobsEnabled(h.jobs?.enabled ?? false);
      })
      .catch(() => {
        setJobsEnabled(false);
      })
      .finally(() => {
        setLoadingHealth(false);
      });

    loadRecentRuns();
  }, [open]);

  // Load recent 5 runs
  const loadRecentRuns = () => {
    setLoadingRecent(true);
    api
      .getRecentErpSyncRuns(5)
      .then((runs) => {
        setRecentRuns(runs || []);
      })
      .catch((err) => {
        // If forbidden or not found, keep empty
        console.warn('Failed to fetch recent erp sync runs:', err);
      })
      .finally(() => {
        setLoadingRecent(false);
      });
  };

  // Poll active job status
  useEffect(() => {
    if (!activeJobId || !open) {
      if (pollIntervalRef.current) {
        clearInterval(pollIntervalRef.current);
        pollIntervalRef.current = null;
      }
      return;
    }

    const poll = async () => {
      try {
        const status = await api.getErpSyncJobStatus(activeJobId);
        setActiveJob(status);

        if (status.status !== 'queued' && status.status !== 'running') {
          // Job finished
          if (pollIntervalRef.current) {
            clearInterval(pollIntervalRef.current);
            pollIntervalRef.current = null;
          }
          loadRecentRuns();
        }
      } catch (err: any) {
        console.error('Failed to poll erp sync status:', err);
      }
    };

    poll();
    pollIntervalRef.current = setInterval(poll, 3000);

    return () => {
      if (pollIntervalRef.current) {
        clearInterval(pollIntervalRef.current);
        pollIntervalRef.current = null;
      }
    };
  }, [activeJobId, open]);

  const [accepted, setAccepted] = useState<{ acceptNewKinds: string[]; acceptLargeChanges: string[] }>({ acceptNewKinds: [], acceptLargeChanges: [] });
  const [overrideReason, setOverrideReason] = useState('');
  const handleTrigger = async (dryRun: boolean) => {
    setTriggerError(null);
    setTriggering(dryRun ? 'dry_run' : 'commit');
    try {
      const hasAccepted = accepted.acceptNewKinds.length + accepted.acceptLargeChanges.length > 0;
      const res = await api.runErpSync(dryRun, hasAccepted ? { ...accepted, reason: overrideReason } : undefined);
      if (res?.jobId) {
        setActiveJobId(res.jobId);
        setActiveJob({
          jobId: res.jobId,
          status: 'queued',
          mode: dryRun ? 'dry_run' : 'commit',
          queuedAt: new Date().toISOString(),
          counts: [],
        });
      }
    } catch (err: any) {
      let msg = err.message || 'تعذر تشغيل المزامنة.';
      if (err instanceof ApiError && err.code && REFUSAL_MESSAGES[err.code]) {
        msg = REFUSAL_MESSAGES[err.code];
      }
      setTriggerError(msg);
    } finally {
      setTriggering(null);
    }
  };

  const isJobBusy = activeJob?.status === 'queued' || activeJob?.status === 'running';

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title="مزامنة بيانات الرواتب من ERP"
      subtitle="تحديث بيانات الرواتب من ERP — قراءة فقط من ERP"
      width="max-w-md sm:max-w-2xl"
    >
      <div className="space-y-6">
        {/* Server Jobs Disabled Notice */}
        {jobsEnabled === false && !loadingHealth && (
          <div className="rounded-xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 p-3.5 flex items-start gap-3 text-xs text-[var(--ink)]">
            <ShieldAlert size={18} className="text-[var(--warning)] shrink-0 mt-0.5" />
            <div>
              <p className="font-bold text-[var(--warning)]">المزامنة غير مفعلة على الخادم بعد</p>
              <p className="text-[var(--muted)] mt-0.5">
                خدمة المهام الخلفية (Hangfire Jobs) معطلة على بيئة الخادم الحالية. لا يمكن جدولة مهام مزامنة جديدة حالياً.
              </p>
            </div>
          </div>
        )}

        {/* Trigger Error Banner */}
        {triggerError && (
          <div className="rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 p-3.5 flex items-start gap-3 text-xs text-[var(--ink)]">
            <AlertTriangle size={18} className="text-[var(--danger)] shrink-0 mt-0.5" />
            <div>
              <p className="font-bold text-[var(--danger)]">تعذر تنفيذ العملية</p>
              <p className="text-[var(--muted)] mt-0.5">{triggerError}</p>
            </div>
          </div>
        )}

        {/* Action Buttons */}
        <div className="flex flex-wrap items-center gap-3 p-4 rounded-xl border border-[var(--line)] bg-[var(--raised)]/50">
          <div className="flex-1 min-w-[200px]">
            <h4 className="text-xs font-bold text-[var(--ink)]">بدء مهمة المزامنة</h4>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              يمكنك المعاينة أولاً لمراجعة الفروقات دون تطبيق أي تغييرات، أو التحديث المباشر.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={jobsEnabled === false || isJobBusy || triggering !== null}
              onClick={() => handleTrigger(true)}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] transition disabled:opacity-50 disabled:cursor-not-allowed shadow-2xs"
            >
              {triggering === 'dry_run' ? (
                <Loader2 size={14} className="animate-spin text-[var(--accent)]" />
              ) : (
                <Eye size={14} className="text-[var(--accent)]" />
              )}
              <span>معاينة (قراءة فقط)</span>
            </button>

            <button
              type="button"
              disabled={jobsEnabled === false || isJobBusy || triggering !== null}
              onClick={() => handleTrigger(false)}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 transition disabled:opacity-50 disabled:cursor-not-allowed shadow-xs"
            >
              {triggering === 'commit' ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <RefreshCw size={14} />
              )}
              <span>تحديث الآن</span>
            </button>
          </div>
        </div>

        {/* Active Job Progress Section */}
        {activeJob && (
          <div className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 shadow-xs">
            <div className="flex items-center justify-between border-b border-[var(--line)] pb-3">
              <div className="flex items-center gap-2">
                {activeJob.status === 'queued' && (
                  <Clock size={16} className="text-[var(--warning)] animate-spin" />
                )}
                {activeJob.status === 'running' && (
                  <RefreshCw size={16} className="text-[var(--info)] animate-spin" />
                )}
                {activeJob.status === 'completed' && (
                  <CheckCircle2 size={16} className="text-[var(--money-pos)]" />
                )}
                {activeJob.status === 'refused' && (
                  <AlertTriangle size={16} className="text-[var(--warning)]" />
                )}
                {activeJob.status === 'failed' && (
                  <XCircle size={16} className="text-[var(--danger)]" />
                )}
                <div>
                  <h4 className="text-xs font-bold text-[var(--ink)]">
                    مهمة رقم: <span className="font-mono">{activeJob.jobId}</span>
                  </h4>
                  <p className="text-[10px] text-[var(--muted)]">
                    النوع: {activeJob.mode === 'dry_run' ? 'معاينة (قراءة فقط)' : 'تحديث وتثبيت'}
                  </p>
                </div>
              </div>

              <span
                className={`text-[11px] font-bold px-2.5 py-1 rounded-full ${
                  activeJob.status === 'completed'
                    ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                    : activeJob.status === 'refused'
                    ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                    : activeJob.status === 'failed'
                    ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                    : 'bg-[var(--info)]/15 text-[var(--info)]'
                }`}
              >
                {activeJob.status === 'queued' && 'قيد الانتظار'}
                {activeJob.status === 'running' && 'جارِ المزامنة...'}
                {activeJob.status === 'completed' && 'اكتملت بنجاح'}
                {activeJob.status === 'refused' && 'تم الرفض'}
                {activeJob.status === 'failed' && 'فشلت'}
              </span>
            </div>

            {/* Refusal / Cooldown Warning */}
            {activeJob.refusalCode && (
              <div className="rounded-lg bg-[var(--warning)]/10 border border-[var(--warning)]/30 p-2.5 text-xs text-[var(--warning)]">
                <span className="font-bold">ملاحظة الخادم: </span>
                <span>{refusalParts(activeJob.refusalCode).message}</span>
                {(() => {
                  const refusal = refusalParts(activeJob.refusalCode!);
                  if (!refusal.field) return null;
                  const field = refusal.field;
                  return (
                    <div className="mt-2 space-y-2">
                      <p>الخطوة التالية: راجع الأعداد، ولو مقبولة اكتب السبب ثم اضغط قبول، وبعدها شغّل المعاينة تاني.</p>
                      <input
                        id="erp-override-reason"
                        value={overrideReason}
                        onChange={(e) => setOverrideReason(e.target.value)}
                        placeholder="سبب القبول (مطلوب)"
                        className="w-full rounded-md border border-[var(--line)] bg-[var(--surface)] p-1.5 text-[var(--ink)]"
                      />
                      <button
                        type="button"
                        id="erp-override-accept"
                        disabled={overrideReason.trim().length < 3}
                        onClick={() => setAccepted((prev) => ({ ...prev, [field]: Array.from(new Set([...prev[field], refusal.kind])) }))}
                        className="rounded-md bg-[var(--accent)] px-2.5 py-1 font-bold text-white disabled:opacity-50"
                      >
                        قبول {refusal.kind} لهذه المرة
                      </button>
                      {accepted.acceptNewKinds.length + accepted.acceptLargeChanges.length > 0 && (
                        <p>مقبول للتشغيل القادم: {[...accepted.acceptNewKinds, ...accepted.acceptLargeChanges].join('، ')}</p>
                      )}
                    </div>
                  );
                })()}
              </div>
            )}

            {/* Counts Table */}
            {activeJob.counts && activeJob.counts.length > 0 ? (
              <div className="space-y-1.5 pt-1">
                <h5 className="text-[11px] font-semibold text-[var(--muted)]">إحصائيات السجلات المتأثرة</h5>
                <div className="overflow-x-auto rounded-lg border border-[var(--line)]">
                  <table className="w-full text-xs text-start border-collapse">
                    <thead>
                      <tr className="bg-[var(--raised)] border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                        <th className="py-1.5 px-3">النوع</th>
                        <th className="py-1.5 px-3">المصدر</th>
                        <th className="py-1.5 px-2 text-end num text-[var(--money-pos)]">جديد</th>
                        <th className="py-1.5 px-2 text-end num text-[var(--info)]">تعديل</th>
                        <th className="py-1.5 px-2 text-end num text-[var(--muted)]">مطابق</th>
                        <th className="py-1.5 px-2 text-end num text-[var(--danger)]">مرفوض</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {activeJob.counts.map((c, i) => (
                        <tr key={i} className="hover:bg-[var(--raised)]/40 transition">
                          <td className="py-1.5 px-3 font-medium">
                            {KIND_LABELS[c.kind] || c.kind}
                          </td>
                          <td className="py-1.5 px-3 text-[var(--muted)] font-mono text-[11px]">
                            {c.source || 'ERP'}
                          </td>
                          <td className="py-1.5 px-2 text-end font-mono num text-[var(--money-pos)] font-bold">
                            {c.new}
                          </td>
                          <td className="py-1.5 px-2 text-end font-mono num text-[var(--info)] font-bold">
                            {c.changed}
                          </td>
                          <td className="py-1.5 px-2 text-end font-mono num text-[var(--muted)]">
                            {c.unchanged}
                          </td>
                          <td className="py-1.5 px-2 text-end font-mono num text-[var(--danger)] font-bold">
                            {c.rejected}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ) : isJobBusy ? (
              <div className="flex items-center justify-center py-4 text-xs text-[var(--muted)] gap-2">
                <Loader2 size={16} className="animate-spin text-[var(--accent)]" />
                <span>جارِ جلب ومعالجة البيانات من ERP...</span>
              </div>
            ) : null}
          </div>
        )}

        {/* Recent Runs List */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-xs font-bold text-[var(--ink)]">آخر 5 عمليات مزامنة</h4>
            <button
              type="button"
              onClick={loadRecentRuns}
              disabled={loadingRecent}
              className="text-[11px] text-[var(--muted)] hover:text-[var(--ink)] flex items-center gap-1"
            >
              <RefreshCw size={12} className={loadingRecent ? 'animate-spin' : ''} />
              <span>تحديث السجل</span>
            </button>
          </div>

          {loadingRecent && recentRuns.length === 0 ? (
            <div className="flex items-center justify-center py-8 text-[var(--muted)]">
              <Loader2 size={20} className="animate-spin text-[var(--accent)]" />
            </div>
          ) : recentRuns.length === 0 ? (
            <div className="py-6 text-center text-xs text-[var(--muted)] rounded-xl border border-dashed border-[var(--line)]">
              لا توجد عمليات مزامنة مسجلة حتى الآن.
            </div>
          ) : (
            <div className="space-y-2">
              {recentRuns.map((run) => (
                <div
                  key={run.id}
                  className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-3 text-xs space-y-2 hover:border-[var(--muted)] transition"
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-semibold text-[var(--ink)] text-[11px]">
                        {run.id}
                      </span>
                      <span className="text-[10px] px-1.5 py-0.5 rounded-md bg-[var(--raised)] text-[var(--muted)]">
                        {run.mode === 'dry_run' ? 'معاينة' : 'تثبيت'}
                      </span>
                    </div>

                    <span
                      className={`text-[10px] font-bold px-2 py-0.5 rounded-full ${
                        run.status === 'completed'
                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                          : run.status === 'refused'
                          ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                          : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                      }`}
                    >
                      {run.status === 'completed' ? 'اكتملت' : run.status === 'refused' ? 'مرفوضة' : 'فاشلة'}
                    </span>
                  </div>

                  <div className="flex items-center justify-between text-[11px] text-[var(--muted)]">
                    <span className="num">
                      {new Date(run.finishedAt || run.startedAt).toLocaleString('ar-SA')}
                    </span>

                    {run.refusalCode ? (
                      <span className="text-[var(--warning)] truncate max-w-[220px]" title={run.refusalCode}>
                        {refusalParts(run.refusalCode).message}
                      </span>
                    ) : (
                      <div className="flex items-center gap-2 text-[10px] font-mono">
                        {run.counts?.map((c, i) => (
                          <span key={i} className="num">
                            {c.kind}: +{c.new} / ~{c.changed}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </Drawer>
  );
}
