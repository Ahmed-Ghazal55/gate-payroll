'use client';

import React, { useState } from 'react';
import {
  Plus,
  Copy,
  QrCode,
  XCircle,
  Trash2,
  RefreshCw,
  CalendarClock,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { Campaign } from '../../lib/types';
import { Toolbar } from '../../components/ui/Toolbar';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { ErrorState } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { DeletePreviewDialog, DeleteTarget } from './DeletePreviewDialog';


// The server builds the public link from Payroll:PublicBaseUrl; older payloads only had a path.
function campaignLink(c: Campaign): string {
  if (c.publicUrl) return c.publicUrl;
  return c.path ? `${window.location.origin}${c.path}` : '';
}

// The public link also checks expiry (Program.cs Campaign()), so an "open" campaign past its date is expired.
function campaignState(c: Campaign): 'open' | 'expired' | 'closed' {
  if (c.status !== 'open') return 'closed';
  return new Date(c.expiresAt).getTime() <= Date.now() ? 'expired' : 'open';
}

export interface CampaignsTabProps {
  campaigns: Campaign[];
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
}

export function CampaignsTab({
  campaigns,
  loading,
  error,
  onRefresh,
}: CampaignsTabProps) {
  const { toast } = useToast();
  const { canManageForms, isAdmin } = useAuth();

  const [createOpen, setCreateOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newKind, setNewKind] = useState<'work-choice' | 'iban'>('iban');
  const [newDays, setNewDays] = useState(14);
  const [creating, setCreating] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null);
  const [qrTarget, setQrTarget] = useState<Campaign | null>(null);
  const [extendTarget, setExtendTarget] = useState<Campaign | null>(null);
  const [extendDays, setExtendDays] = useState(14);
  const [extending, setExtending] = useState(false);

  const handleExtend = async () => {
    if (!extendTarget) return;
    setExtending(true);
    try {
      const expiresAt = new Date(Date.now() + extendDays * 86400000).toISOString();
      await api.updateCampaign(extendTarget.id, { expiresAt, status: 'open' });
      toast(`تم فتح الحملة حتى ${new Date(expiresAt).toLocaleDateString('ar-SA')}`, 'success');
      setExtendTarget(null);
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر تمديد الحملة', 'danger');
    } finally {
      setExtending(false);
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim()) return;

    setCreating(true);
    try {
      await api.createCampaign({
        title: newTitle.trim(),
        kind: newKind,
        days: newDays,
      });
      toast('تم إنشاء الحملة بنجاح', 'success');
      setCreateOpen(false);
      setNewTitle('');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر إنشاء الحملة', 'danger');
    } finally {
      setCreating(false);
    }
  };

  const handleCloseCampaign = async (id: string) => {
    try {
      await api.closeCampaign(id);
      toast('تم إغلاق استقبال الردود للحملة', 'warning');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر إغلاق الحملة', 'danger');
    }
  };

  const copyToClipboard = async (text: string, label: string) => {
    if (!text) {
      toast('رابط الحملة غير متاح؛ حدّث الصفحة.', 'danger');
      return;
    }
    try {
      await navigator.clipboard.writeText(text);
      toast(`تم نسخ رابط ${label} إلى الحافظة`, 'success');
    } catch {
      // Clipboard blocked (permissions or non-HTTPS): show the link so it can be copied by hand.
      window.prompt('انسخ الرابط:', text);
    }
  };

  const columns: ColumnDef<Campaign>[] = [
    {
      key: 'title',
      header: 'عنوان الحملة',
      render: (c) => (
        <div className="flex flex-col">
          <strong className="text-[var(--ink)] font-bold">{c.title}</strong>
          <span className="text-[11px] text-[var(--muted)] font-mono">{c.path}</span>
        </div>
      ),
    },
    {
      key: 'kind',
      header: 'النوع',
      render: (c) => (
        <span
          className={`px-2 py-0.5 rounded-full text-[11px] font-semibold ${
            c.kind === 'iban'
              ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
              : 'bg-[var(--info)]/15 text-[var(--info)]'
          }`}
        >
          {c.kind === 'iban' ? 'تحديث الآيبان' : 'اختيار نظام العمل'}
        </span>
      ),
    },
    {
      key: 'status',
      header: 'الحالة',
      render: (c) => (
        <span
          className={`px-2.5 py-0.5 rounded-full text-xs font-semibold ${
            campaignState(c) === 'open'
              ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
              : campaignState(c) === 'expired'
              ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
              : 'bg-[var(--muted)]/20 text-[var(--muted)]'
          }`}
        >
          {campaignState(c) === 'open' ? 'مفتوحة للاستقبال' : campaignState(c) === 'expired' ? 'منتهية الصلاحية' : 'مغلقة'}
        </span>
      ),
    },
    {
      key: 'responseCount',
      header: 'عدد الردود',
      numeric: true,
      render: (c) => (
        <span className="num font-bold text-[var(--ink)]">{c.responseCount}</span>
      ),
    },
    {
      key: 'expiresAt',
      header: 'تاريخ الانتهاء',
      render: (c) => (
        <span className="num text-xs text-[var(--muted)]">
          {new Date(c.expiresAt).toLocaleDateString('ar-SA')}
        </span>
      ),
    },
  ];

  return (
    <div className="space-y-4">
      <Toolbar
        searchPlaceholder="بحث في الحملات والروابط..."
        extraControls={
          <div className="flex items-center gap-2">
            {canManageForms && (
              <button
                type="button"
                onClick={() => setCreateOpen(true)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 shadow-xs"
              >
                <Plus size={15} />
                <span>إنشاء حملة جديدة</span>
              </button>
            )}
            <button
              type="button"
              onClick={onRefresh}
              className="p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)]"
              title="تحديث البيانات"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
            </button>
          </div>
        }
      />

      {error ? (
        <ErrorState
          title="تعذر تحميل الحملات"
          message={error}
          onRetry={onRefresh}
        />
      ) : (
        <DataTable
          columns={columns}
          data={campaigns}
          loading={loading}
          emptyTitle="لا توجد حملات نشطة حاليًا"
          emptyDescription="أنشئ رابط حملة جديد لمشاركته مع المناديب واستقبال البيانات المحدثة."
          rowActions={[
            {
              label: 'نسخ رابط النموذج',
              onClick: (c) =>
                copyToClipboard(campaignLink(c), c.title),
              icon: <Copy size={14} />,
            },
            {
              label: 'عرض رمز QR',
              onClick: (c) => setQrTarget(c),
              icon: <QrCode size={14} />,
            },
            ...(canManageForms
              ? [
                  {
                    label: 'إغلاق الاستقبال',
                    disabled: (c: Campaign) => c.status === 'closed',
                    onClick: (c: Campaign) => handleCloseCampaign(c.id),
                    icon: <XCircle size={14} />,
                  },
                ]
              : []),
            ...(isAdmin
              ? [
                  {
                    label: 'تمديد / إعادة فتح',
                    onClick: (c: Campaign) => setExtendTarget(c),
                    icon: <CalendarClock size={14} />,
                  },
                ]
              : []),
            ...(isAdmin
              ? [
                  {
                    label: 'حذف الحملة',
                    danger: true,
                    onClick: (c: Campaign) =>
                      setDeleteTarget({ id: c.id, title: c.title, kind: 'campaign' }),
                    icon: <Trash2 size={14} />,
                  },
                ]
              : []),
          ]}
        />
      )}

      {/* Create Dialog */}
      <Dialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="إنشاء حملة نماذج واستبيانات جديدة"
        description="أنشئ رابطًا عامًا آمنًا لمشاركته مع المناديب لجمع اختياراتهم أو بيانات الآيبان."
        actions={
          <>
            <button
              type="button"
              onClick={() => setCreateOpen(false)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={creating}
              onClick={handleCreate}
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50"
            >
              إنشاء الحملة وتوليد الرابط
            </button>
          </>
        }
      >
        <form onSubmit={handleCreate} className="space-y-4">
          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              عنوان الحملة
            </label>
            <input
              type="text"
              required
              value={newTitle}
              onChange={(e) => setNewTitle(e.target.value)}
              placeholder="مثال: تحديث الآيبان البنكي — أكتوبر 2026"
              className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
            />
          </div>

          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              نوع النموذج المطلوب
            </label>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => setNewKind('iban')}
                className={`p-3 rounded-xl border text-start transition ${
                  newKind === 'iban'
                    ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <div className="text-xs font-bold">تحديث الآيبان (IBAN)</div>
                <div className="text-[11px] font-normal opacity-80 mt-0.5">
                  جمع الحساب البنكي والتحقق التلقائي
                </div>
              </button>

              <button
                type="button"
                onClick={() => setNewKind('work-choice')}
                className={`p-3 rounded-xl border text-start transition ${
                  newKind === 'work-choice'
                    ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <div className="text-xs font-bold">اختيار نظام العمل</div>
                <div className="text-[11px] font-normal opacity-80 mt-0.5">
                  راتب شهري أو بالطلب وتحديد المدينة
                </div>
              </button>
            </div>
          </div>

          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              فترة صلاحية الرابط (أيام)
            </label>
            <input
              type="number"
              min={1}
              max={90}
              value={newDays}
              onChange={(e) => setNewDays(Number(e.target.value))}
              className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] num"
            />
          </div>
        </form>
      </Dialog>

      {/* Delete Preview Confirmation Dialog */}
      <DeletePreviewDialog
        target={deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onSuccess={onRefresh}
      />

      <Dialog
        open={!!extendTarget}
        onClose={() => (extending ? null : setExtendTarget(null))}
        title={`تمديد / إعادة فتح: ${extendTarget?.title || ''}`}
        description="الرابط الحالي يبقى كما هو؛ يعود لاستقبال الردود حتى التاريخ الجديد."
        actions={
          <>
            <button
              type="button"
              disabled={extending}
              onClick={() => setExtendTarget(null)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] text-[var(--ink)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={extending}
              onClick={handleExtend}
              className="px-4 py-2 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] disabled:opacity-50"
            >
              {extending ? 'جاري الحفظ...' : 'فتح الحملة'}
            </button>
          </>
        }
      >
        <label className="block space-y-1.5 text-xs">
          <span className="font-semibold text-[var(--ink)]">مدة الاستقبال من اليوم (بالأيام، 1 إلى 30)</span>
          <input
            type="number"
            min={1}
            max={30}
            value={extendDays}
            onChange={(e) => setExtendDays(Math.min(30, Math.max(1, Number(e.target.value) || 1)))}
            className="w-full px-3 py-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] num"
          />
          <span className="text-[var(--muted)]">
            ينتهي في: {new Date(Date.now() + extendDays * 86400000).toLocaleDateString('ar-SA')}
          </span>
        </label>
      </Dialog>

      {/* QR Code Dialog */}
      <Dialog
        open={!!qrTarget}
        onClose={() => setQrTarget(null)}
        title={`رمز QR: ${qrTarget?.title || ''}`}
        actions={
          <button
            type="button"
            onClick={() => setQrTarget(null)}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)]"
          >
            إغلاق
          </button>
        }
      >
        <div className="flex flex-col items-center justify-center p-6 text-center space-y-3">
          <div className="h-44 w-44 rounded-2xl border-2 border-[var(--line)] bg-white p-3 flex items-center justify-center shadow-md">
            {qrTarget?.qrCodeUrl ? (
              <img
                src={qrTarget.qrCodeUrl}
                alt={`رمز QR لرابط ${qrTarget.title}`}
                className="w-full h-full object-contain"
                data-testid="campaign-qr"
              />
            ) : (
              <span className="text-xs text-[var(--muted)]">رمز QR غير متاح</span>
            )}
          </div>
          <p className="text-xs text-[var(--muted)]">
            امسح الرمز بكاميرا الجوال للانتقال لصفحة النموذج مباشرة
          </p>
          <button
            type="button"
            onClick={() =>
              qrTarget && copyToClipboard(campaignLink(qrTarget), qrTarget.title)
            }
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--ink)]"
          >
            <Copy size={13} />
            <span>نسخ الرابط المباشر</span>
          </button>
        </div>
      </Dialog>
    </div>
  );
}
