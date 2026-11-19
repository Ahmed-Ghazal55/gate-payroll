'use client';

// App accounts and devices (existing sessions panel) + X12: a one-time login code for a rider,
// issued by staff after checking the rider's identity. The code is shown once and never logged.
import React, { useState } from 'react';
import { KeyRound, Copy } from 'lucide-react';
import { api, type InquiryAccessResult } from '../../lib/api';
import { RiderSessionsPanel } from '../../components/riders/RiderSessionsPanel';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { Dialog } from '../../components/ui/Dialog';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';

function AccessCodeCard() {
  const { toast } = useToast();
  const [iqama, setIqama] = useState('');
  const [checked, setChecked] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [issuing, setIssuing] = useState(false);
  const [grant, setGrant] = useState<InquiryAccessResult | null>(null);

  const issue = async () => {
    setIssuing(true);
    try {
      setGrant(await api.issueMobileAccessCode(iqama));
      setConfirm(false);
      setChecked(false);
    } catch (err: any) {
      toast(err?.message || 'تعذر إصدار رمز الدخول', 'danger');
    } finally {
      setIssuing(false);
    }
  };

  const copy = async (text: string, label: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast(`تم نسخ ${label}`, 'success');
    } catch {
      toast('تعذر النسخ؛ انسخ يدويًا', 'warning');
    }
  };

  return (
    <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
      <div className="flex items-center gap-2">
        <KeyRound size={16} className="text-[var(--accent)]" />
        <h3 className="font-bold text-sm">رمز دخول لمرة واحدة للتطبيق</h3>
      </div>
      <p className="text-xs text-[var(--muted)]">
        يُسلَّم الرابط للمندوب بعد التحقق من هويته. صالح لمرة واحدة ولمدة 10 دقائق، ويطلب آخر 4 أرقام من جوال المندوب المسجل.
      </p>
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-64 flex-1">
          <RiderPicker value={iqama} onChange={(r) => setIqama(r ? r.iqama ?? r.id : '')} />
        </div>
        <button
          type="button"
          disabled={!iqama}
          onClick={() => {
            setGrant(null);
            setConfirm(true);
          }}
          className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
        >
          إصدار رمز دخول
        </button>
      </div>
      {grant ? (
        <div className="p-3 rounded-xl bg-[var(--raised)] space-y-2 text-xs" data-testid="mobile-access-grant">
          <p>
            ينتهي في: <span className="num">{new Date(grant.expiresAt).toLocaleTimeString('ar-SA')}</span> — يظهر مرة واحدة فقط.
          </p>
          <div className="flex items-center gap-2">
            <span className="font-bold shrink-0">الرمز:</span>
            <code className="font-mono text-[11px]" dir="ltr">{grant.code}</code>
            <button type="button" onClick={() => copy(grant.code, 'الرمز')} className="px-2 py-1 rounded-lg border border-[var(--line)] inline-flex items-center gap-1">
              <Copy size={12} /> نسخ الرمز
            </button>
          </div>
          <div className="flex items-center gap-2">
            <code className="flex-1 truncate font-mono text-[11px]" dir="ltr">{grant.url}</code>
            <button type="button" onClick={() => copy(grant.url, 'رابط الدخول')} className="px-2 py-1 rounded-lg border border-[var(--line)] inline-flex items-center gap-1">
              <Copy size={12} /> نسخ الرابط
            </button>
          </div>
        </div>
      ) : null}
      <Dialog
        open={confirm}
        onClose={() => setConfirm(false)}
        title="تأكيد هوية المندوب"
        description="لا يُسلَّم الرمز إلا لصاحب الإقامة بعد التحقق من هويته بنفسك."
        actions={
          <button type="button" disabled={!checked || issuing} onClick={issue} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            {issuing ? 'جاري الإصدار…' : 'إصدار'}
          </button>
        }
      >
        <label className="flex items-center gap-2 text-xs">
          <input type="checkbox" checked={checked} onChange={(e) => setChecked(e.target.checked)} />
          <span>تحققت من هوية المندوب صاحب الإقامة <span className="font-mono" dir="ltr">{iqama}</span></span>
        </label>
      </Dialog>
    </div>
  );
}

export function AccountsPanel({ canIssueCode }: { canIssueCode: boolean }) {
  const { can, isAdmin } = useAuth();
  // InquiryApi requires payroll.view for mobile access codes; hide the card without it.
  const showIssue = canIssueCode && (isAdmin || can('payroll.view'));
  return (
    <div className="space-y-4">
      {showIssue ? <AccessCodeCard /> : null}
      <RiderSessionsPanel />
    </div>
  );
}
