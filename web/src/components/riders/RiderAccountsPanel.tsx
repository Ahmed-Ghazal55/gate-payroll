'use client';

import React, { useState, useEffect, useCallback } from 'react';
import {
  Smartphone,
  RefreshCw,
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  Layers,
  Clock,
} from 'lucide-react';
import {
  api,
  RiderAccountsResponse,
  RiderAccountRow,
  RiderAccountAssignment,
  ApiError,
} from '../../lib/api';
import { SourceBadge } from '../ui/SourceBadge';

export interface RiderAccountsPanelProps {
  iqama: string;
  className?: string;
}

const PAGE_SIZES = [25, 50, 100, 200];

export function RiderAccountsPanel({ iqama, className = '' }: RiderAccountsPanelProps) {
  const [items, setItems] = useState<RiderAccountRow[]>([]);
  const [total, setTotal] = useState<number>(0);
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(25);
  const [source, setSource] = useState<string>('');
  const [asOf, setAsOf] = useState<string>('');
  const [allowedActions, setAllowedActions] = useState<string[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [is503, setIs503] = useState<boolean>(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const fetchAccounts = useCallback(
    async (targetPage = page, targetSize = pageSize) => {
      const trimmedIqama = iqama?.trim();
      if (!trimmedIqama) {
        setItems([]);
        setTotal(0);
        setLoading(false);
        return;
      }

      setLoading(true);
      setErrorMsg(null);
      setIs503(false);

      try {
        const res: RiderAccountsResponse = await api.getRiderAccounts(trimmedIqama, {
          page: targetPage,
          pageSize: targetSize,
        });

        const rows = res?.items || (Array.isArray(res) ? res : []);
        setItems(rows);
        setTotal(res?.total ?? rows.length);
        setSource(res?.source || '');
        setAsOf(res?.asOf || '');
        setAllowedActions(res?.allowedActions || []);
        setPage(res?.page ?? targetPage);
        setPageSize(res?.pageSize ?? targetSize);
      } catch (err: unknown) {
        const status =
          err instanceof ApiError
            ? err.status
            : typeof err === 'object' && err !== null && 'status' in err && typeof err.status === 'number'
            ? err.status
            : undefined;
        const msg = err instanceof Error && err.message ? err.message : 'تعذر استرجاع حسابات المنصات للمندوب';

        if (status === 503 || msg.includes('503') || msg.includes('مصدر التشغيل')) {
          setIs503(true);
          setErrorMsg(msg);
          // Keep the last page shown if items already exist; do not clear items
        } else {
          setErrorMsg(msg);
          setItems([]);
          setTotal(0);
        }
      } finally {
        setLoading(false);
      }
    },
    [iqama, page, pageSize]
  );

  useEffect(() => {
    setPage(1);
    fetchAccounts(1, pageSize);
  }, [iqama]);

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    fetchAccounts(newPage, pageSize);
  };

  const handlePageSizeChange = (newSize: number) => {
    setPageSize(newSize);
    setPage(1);
    fetchAccounts(1, newSize);
  };

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <div className={`rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs space-y-0 ${className}`}>
      {/* Header with Title and Source Badge */}
      <div className="p-4 border-b border-[var(--line)] flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <Smartphone size={16} className="text-[var(--accent)] shrink-0" />
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              حسابات وتكليفات المنصات الميدانية
            </h3>
          </div>
          <p className="text-xs text-[var(--muted)] mt-0.5">
            مطابقة معرفات العمل وتكليفات التشغيل الفعلي مع تواريخ الاستلام والتسليم للقراءة فقط.
          </p>
        </div>

        <div className="flex items-center gap-2">
          {source && <SourceBadge source={source} asOf={asOf} />}

          <button
            type="button"
            onClick={() => fetchAccounts(page, pageSize)}
            disabled={loading}
            aria-label="تحديث حسابات المنصات"
            className="p-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] disabled:opacity-40 transition cursor-pointer"
            title="تحديث البيانات"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
          </button>
        </div>
      </div>

      {/* 503 Warning Banner when keeping last shown page */}
      {is503 && items.length > 0 && (
        <div role="alert" className="m-4 p-4 rounded-xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2 text-xs text-[var(--warning)] font-bold">
            <AlertTriangle size={16} className="shrink-0" />
            <span>مصدر التشغيل غير متاح الآن — تم الإبقاء على آخر قراءة متوفرة أدناه ({errorMsg})</span>
          </div>
          <button
            type="button"
            onClick={() => fetchAccounts(page, pageSize)}
            className="px-3 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition inline-flex items-center gap-1 cursor-pointer"
          >
            <RefreshCw size={13} />
            <span>إعادة المحاولة</span>
          </button>
        </div>
      )}

      {/* 503 Full Card when no data is available */}
      {is503 && items.length === 0 ? (
        <div role="alert" className="p-8 text-center space-y-3">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] text-xs font-bold">
            <AlertTriangle size={15} />
            <span>مصدر التشغيل غير متاح الآن</span>
          </div>
          <h4 className="text-base font-bold text-[var(--ink)]">
            تعذر الاتصال بمصدر التشغيل اللوجستي
          </h4>
          <p className="text-xs text-[var(--muted)] max-w-md mx-auto">
            {errorMsg || 'نظام الموارد الميداني أو اتصال قاعدة بيانات التشغيل غير متاح حالياً.'}
          </p>
          <div className="pt-2">
            <button
              type="button"
              onClick={() => fetchAccounts(page, pageSize)}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 text-xs font-bold transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <RefreshCw size={14} />
              <span>إعادة المحاولة</span>
            </button>
          </div>
        </div>
      ) : errorMsg && !is503 ? (
        <div role="alert" className="p-6 text-center space-y-2">
          <p className="text-xs text-[var(--danger)] font-bold">{errorMsg}</p>
          <button
            type="button"
            onClick={() => fetchAccounts(page, pageSize)}
            className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold hover:bg-[var(--line)] transition cursor-pointer"
          >
            إعادة المحاولة
          </button>
        </div>
      ) : (
        <>
          {/* Table Container */}
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs sm:text-sm">
              <thead className="border-b border-[var(--line)] bg-[var(--raised)]/60 text-[var(--muted)] font-semibold">
                <tr>
                  <th className="p-3 text-start">كود الحساب</th>
                  <th className="p-3 text-start">معرف المنصة الخارجي</th>
                  <th className="p-3 text-start">المنصة</th>
                  <th className="p-3 text-start">المدينة</th>
                  <th className="p-3 text-start">العلاقة</th>
                  <th className="p-3 text-start">فترات التكليف الفعلي</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]">
                {loading ? (
                  Array.from({ length: 4 }).map((_, idx) => (
                    <tr key={`skel-${idx}`} className="animate-pulse">
                      <td className="p-3"><div className="h-4 w-20 bg-[var(--raised)] rounded" /></td>
                      <td className="p-3"><div className="h-4 w-28 bg-[var(--raised)] rounded" /></td>
                      <td className="p-3"><div className="h-4 w-16 bg-[var(--raised)] rounded" /></td>
                      <td className="p-3"><div className="h-4 w-14 bg-[var(--raised)] rounded" /></td>
                      <td className="p-3"><div className="h-4 w-20 bg-[var(--raised)] rounded" /></td>
                      <td className="p-3"><div className="h-4 w-36 bg-[var(--raised)] rounded" /></td>
                    </tr>
                  ))
                ) : items.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="p-8 text-center text-xs text-[var(--muted)]">
                      لا توجد حسابات منصات مرتبطة بهذا المندوب في نظام التشغيل.
                    </td>
                  </tr>
                ) : (
                  items.map((acc: RiderAccountRow, i: number) => (
                    <tr key={acc.id || acc.code || i} className="hover:bg-[var(--row-hover)] transition">
                      <td className="p-3 font-mono font-bold text-[var(--accent)]">{acc.code || acc.id}</td>
                      <td className="p-3 font-mono font-bold">{acc.externalAccountId || '-'}</td>
                      <td className="p-3 font-bold">{acc.platformCode || '-'}</td>
                      <td className="p-3 text-xs">{acc.cityId || '-'}</td>
                      <td className="p-3">
                        <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                          {acc.relationship === 'actual_operator' ? 'مشغل فعلي' : acc.relationship || 'مشغل'}
                        </span>
                      </td>
                      <td className="p-3 text-xs font-mono text-[var(--muted)]">
                        {acc.assignments?.length ? (
                          acc.assignments.map((a: RiderAccountAssignment, j: number) => (
                            <div key={j}>
                              {a.RawEffectiveFrom?.slice(0, 10) || a.EffectiveFrom?.slice(0, 10) || 'بداية'} ➔ {a.RawEffectiveTo?.slice(0, 10) || a.EffectiveTo?.slice(0, 10) || 'مستمر'}
                            </div>
                          ))
                        ) : (
                          <span>-</span>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          {/* Server Pagination Footer */}
          <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-t border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
            <div className="flex items-center gap-2">
              {loading ? (
                <span>جاري تحميل الحسابات...</span>
              ) : (
                <span>
                  عرض{' '}
                  <strong className="num text-[var(--ink)] font-semibold">
                    {total === 0 ? 0 : (page - 1) * pageSize + 1}
                  </strong>{' '}
                  إلى{' '}
                  <strong className="num text-[var(--ink)] font-semibold">
                    {Math.min(page * pageSize, total)}
                  </strong>{' '}
                  من إجمالي:{' '}
                  <strong className="num text-[var(--ink)] font-semibold">{total}</strong>{' '}
                  حساب
                </span>
              )}

              <div className="flex items-center gap-1.5 ms-4">
                <span>لكل صفحة:</span>
                <select
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                  aria-label="عدد الحسابات لكل صفحة"
                  className="py-1 px-2 rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
                >
                  {PAGE_SIZES.map((size) => (
                    <option key={size} value={size}>
                      {size}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {/* Navigation buttons */}
            {totalPages > 1 && (
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => handlePageChange(page - 1)}
                  aria-label="الصفحة السابقة"
                  className="p-1.5 rounded-md border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:pointer-events-none transition cursor-pointer"
                >
                  <ChevronRight size={15} />
                </button>

                <span className="px-2 num font-semibold text-[var(--ink)]">
                  {page} / {totalPages}
                </span>

                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => handlePageChange(page + 1)}
                  aria-label="الصفحة التالية"
                  className="p-1.5 rounded-md border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:pointer-events-none transition cursor-pointer"
                >
                  <ChevronLeft size={15} />
                </button>
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
