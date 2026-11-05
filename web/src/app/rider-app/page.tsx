'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  Smartphone,
  Bell,
  Settings,
  FileText,
  Calendar,
  Send,
  RefreshCw,
  Search,
  Filter,
  CheckCircle2,
  AlertCircle,
  Clock,
  ShieldCheck,
  Eye,
  KeyRound,
  Users,
  Layers,
  ArrowRight,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';

function RiderAppAdminPageInner() {
  const { user } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'broadcasts';
  const resolvedTab = tabFromUrl === 'statement' ? 'statement-preview' : tabFromUrl;
  const [activeTab, setActiveTab] = useState<string>(resolvedTab);

  useEffect(() => {
    if (resolvedTab && resolvedTab !== activeTab) {
      setActiveTab(resolvedTab);
    }
  }, [resolvedTab]);

  // Broadcasts state
  const [broadcasts, setBroadcasts] = useState<any[]>([]);
  const [loadingBroadcasts, setLoadingBroadcasts] = useState<boolean>(true);
  const [broadcastsError, setBroadcastsError] = useState<string | null>(null);

  // Send Broadcast Dialog state
  const [sendDialogOpen, setSendDialogOpen] = useState<boolean>(false);
  const [targetType, setTargetType] = useState<'ALL' | 'specific'>('ALL');
  const [targetIqama, setTargetIqama] = useState<string>('');
  const [broadcastCategory, setBroadcastCategory] = useState<string>('operational');
  const [broadcastTitle, setBroadcastTitle] = useState<string>('');
  const [broadcastBody, setBroadcastBody] = useState<string>('');
  const [broadcastActionUrl, setBroadcastActionUrl] = useState<string>('');
  const [requiresAck, setRequiresAck] = useState<boolean>(false);
  const [sendingBroadcast, setSendingBroadcast] = useState<boolean>(false);

  // Firebase Config state
  const [fbConfig, setFbConfig] = useState<any>(null);
  const [loadingFbConfig, setLoadingFbConfig] = useState<boolean>(true);

  // Statement Preview state (F3 - R4.8)
  const [statementIqama, setStatementIqama] = useState<string>('2000000789');
  const [statementPeriod, setStatementPeriod] = useState<string>('2026-10');
  const [monthlyStatement, setMonthlyStatement] = useState<any | null>(null);
  const [loadingStatement, setLoadingStatement] = useState<boolean>(false);
  const [statementError, setStatementError] = useState<string | null>(null);

  // Load Statement Preview
  const loadStatementPreview = async () => {
    if (!statementIqama.trim()) return;
    setLoadingStatement(true);
    setStatementError(null);
    try {
      const res = await api.getRiderMonthlyStatement(statementIqama.trim(), statementPeriod);
      setMonthlyStatement(res);
    } catch (err: any) {
      setStatementError(err.message || 'تعذر تحميل كشف حساب المندوب الشهري');
      setMonthlyStatement(null);
    } finally {
      setLoadingStatement(false);
    }
  };

  // Load Broadcasts
  const loadBroadcasts = async () => {
    setLoadingBroadcasts(true);
    setBroadcastsError(null);
    try {
      const res = await api.getRiderBroadcasts();
      setBroadcasts(Array.isArray(res) ? res : []);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل قائمة الإشعارات';
      setBroadcastsError(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingBroadcasts(false);
    }
  };

  // Load Firebase Config
  const loadFbConfig = async () => {
    setLoadingFbConfig(true);
    try {
      const res = await api.getFirebaseConfig();
      setFbConfig(res);
    } catch (_) {
      setFbConfig(null);
    } finally {
      setLoadingFbConfig(false);
    }
  };

  useEffect(() => {
    loadBroadcasts();
    loadFbConfig();
  }, []);

  useEffect(() => {
    if (activeTab === 'statement-preview') {
      loadStatementPreview();
    }
  }, [activeTab, statementPeriod]);

  // Send Broadcast
  const handleSendBroadcast = async () => {
    if (!broadcastTitle.trim() || !broadcastBody.trim()) {
      toast('عنوان الإشعار ونصه مطلوبان', 'warning');
      return;
    }
    if (targetType === 'specific' && !targetIqama.trim()) {
      toast('يرجى إدخال رقم إقامة المندوب المستهدف', 'warning');
      return;
    }

    setSendingBroadcast(true);
    try {
      await api.sendRiderBroadcast({
        category: broadcastCategory,
        titleAr: broadcastTitle.trim(),
        bodyAr: broadcastBody.trim(),
        iqama: targetType === 'specific' ? targetIqama.trim() : 'ALL',
        actionUrl: broadcastActionUrl.trim() || undefined,
        requiresAck,
      });

      toast('تم بث وتخزين الإشعار في تطبيق المناديب بنجاح', 'success');
      setSendDialogOpen(false);
      setBroadcastTitle('');
      setBroadcastBody('');
      setBroadcastActionUrl('');
      setTargetIqama('');
      setRequiresAck(false);
      loadBroadcasts();
    } catch (err: any) {
      toast(err.message || 'فشل إرسال الإشعار', 'danger');
    } finally {
      setSendingBroadcast(false);
    }
  };

  const sectionTabs: SectionTabItem[] = [
    { id: 'broadcasts', label: 'الإشعارات والإرسال', icon: <Bell size={16} />, badge: broadcasts.length },
    { id: 'accounts', label: 'الحسابات والأجهزة', icon: <Smartphone size={16} /> },
    { id: 'settings', label: 'إعدادات Firebase والربط', icon: <Settings size={16} /> },
    { id: 'statement-preview', label: 'كشف الحساب في التطبيق', icon: <FileText size={16} /> },
    { id: 'requests', label: 'الطلبات والمواعيد', icon: <Calendar size={16} /> },
  ];

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="إدارة تطبيق المناديب (Track M)"
        eyebrow="الخدمات الرقمية للمناديب"
        description="إدارة بوابة وتطبيق الجوال للمناديب: بث التوجيهات والإشعارات الميدانية، إعدادات الربط السحابي (Firebase)، ومعاينة كشوف الحسابات الرقمية."
        primaryAction={{
          label: 'إرسال إشعار للمناديب',
          icon: <Send size={15} />,
          onClick: () => setSendDialogOpen(true),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي الإشعارات المبثوثة"
          value={broadcasts.length}
          hint="رسائل موجهة وتعاميم عامة"
          icon={Bell}
        />
        <StatCard
          label="حالة قناة الإرسال"
          value="مخزّن داخل التطبيق"
          hint="الإرسال الفوري (FCM) لاحقاً"
          icon={Clock}
        />
        <StatCard
          label="حزمة تطبيق أندرويد"
          value="com.albawaba.gate.rider"
          hint="معرف الحزمة الرسمي"
          icon={Smartphone}
        />
        <StatCard
          label="حسابات المناديب المسجلة"
          value="نشطة ومحمية"
          hint="مصادقة برقم الإقامة والهاتف"
          icon={ShieldCheck}
        />
      </div>

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(tabId) => setActiveTab(tabId)}
      />

      {/* Tab 1: Broadcasts */}
      {activeTab === 'broadcasts' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center justify-between gap-3 shadow-xs">
            <div className="space-y-0.5">
              <div className="flex items-center gap-2">
                <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  سجل التوجيهات والإشعارات الإدارية للمناديب
                </h3>
                <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/15 text-[var(--accent)] border border-[var(--accent)]/30">
                  مخزّن داخل التطبيق — الإرسال الفوري (FCM) لاحقًا
                </span>
              </div>
              <p className="text-xs text-[var(--muted)]">
                تظهر الإشعارات للمندوب فور دخوله لتطبيق أو بوابة المناديب مع توثيق قراءة واستلام التوجيه.
              </p>
            </div>

            <button
              type="button"
              onClick={() => setSendDialogOpen(true)}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <Send size={14} />
              <span>بث إشعار جديد</span>
            </button>
          </div>

          {broadcastsError ? (
            <ErrorState title="تعذر تحميل الإشعارات" message={broadcastsError} onRetry={loadBroadcasts} />
          ) : loadingBroadcasts ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : broadcasts.length === 0 ? (
            <EmptyState
              title="لا توجد إشعارات مبثوثة حالياً"
              description="استخدم زر «بث إشعار جديد» لإرسال توجيه تشغيلي أو رابط نموذج استبيان للمناديب."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">عنوان الإشعار</th>
                      <th className="p-3 text-start">نص الإشعار</th>
                      <th className="p-3 text-start">المستهدف</th>
                      <th className="p-3 text-start">التصنيف</th>
                      <th className="p-3 text-start">قناة الإرسال</th>
                      <th className="p-3 text-start">تأكيد القراءة (Ack)</th>
                      <th className="p-3 text-start">تاريخ الإرسال</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {broadcasts.map((b) => (
                      <tr key={b.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-bold text-[var(--ink)]">{b.titleAr}</td>
                        <td className="p-3 text-xs text-[var(--muted)] max-w-sm truncate">{b.bodyAr}</td>
                        <td className="p-3 font-mono text-xs">
                          {b.targetIqama === 'ALL' ? (
                            <span className="px-2 py-0.5 rounded-full bg-[var(--raised)] text-[var(--ink)] font-bold">
                              كافة المناديب (ALL)
                            </span>
                          ) : (
                            <span className="font-bold text-[var(--accent)]">{b.targetIqama}</span>
                          )}
                        </td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                            {b.category === 'financial'
                              ? 'مالي'
                              : b.category === 'form_link'
                              ? 'نموذج'
                              : b.category === 'administrative'
                              ? 'إداري'
                              : 'تشغيلي'}
                          </span>
                        </td>
                        <td className="p-3 text-xs text-[var(--muted)]">
                          {b.deliveryChannel === 'in_app' ? 'داخل التطبيق' : b.deliveryChannel}
                        </td>
                        <td className="p-3 num">
                          {Array.isArray(b.acknowledgedBy) ? b.acknowledgedBy.length : 0} مندوب
                        </td>
                        <td className="p-3 num text-xs text-[var(--muted)] whitespace-nowrap">
                          {b.createdAt || '-'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Accounts & Devices */}
      {activeTab === 'accounts' && (
        <div className="space-y-4">
          <div className="p-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-4 shadow-xs">
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] text-xs font-bold">
              <Clock size={15} />
              <span>غير متاح بعد — بانتظار اكتمال واجهات خادم الجلسات الميدانية (Codex)</span>
            </div>
            <h4 className="text-base font-bold text-[var(--ink)]">
              سجل جلسات الدخول وأجهزة المناديب
            </h4>
            <p className="text-xs sm:text-sm text-[var(--muted)] max-w-md mx-auto leading-relaxed">
              ستتيح هذه الشاشة مراقبة الأجهزة المسجلة (Device Fingerprint)، جلسات تسجيل الدخول، وفصل الجلسات المشبوهة لحماية بيانات المناديب.
            </p>
          </div>
        </div>
      )}

      {/* Tab 3: Firebase Config */}
      {activeTab === 'settings' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  إعدادات الربط مع Firebase Cloud Messaging
                </h3>
                <p className="text-xs text-[var(--muted)] mt-0.5">
                  بيانات الربط مع مشروع Firebase لتطبيق الأندرويد.
                </p>
              </div>
              <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                محمي ومشفر
              </span>
            </div>

            <div className="mt-4 pt-4 border-t border-[var(--line)] space-y-4 max-w-xl text-xs">
              <div className="space-y-1">
                <span className="font-bold text-[var(--ink)]">معرف مشروع Firebase (Project ID):</span>
                <div className="p-2.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono text-[var(--ink)]">
                  {fbConfig?.projectId || 'albawaba-gate-rider'}
                </div>
              </div>

              <div className="space-y-1">
                <span className="font-bold text-[var(--ink)]">معرف الحزمة (Android Package Name):</span>
                <div className="p-2.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono text-[var(--ink)]">
                  {fbConfig?.androidPackageName || 'com.albawaba.gate.rider'}
                </div>
              </div>

              <div className="space-y-1">
                <span className="font-bold text-[var(--ink)]">حساب الخدمة (Service Account):</span>
                <div className="p-2.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--muted)] font-mono flex items-center justify-between">
                  <span>••••••••••••••••••••••••••••••••</span>
                  <span className="text-[10px] text-[var(--accent)] font-sans font-bold">
                    {fbConfig?.hasServiceAccountJson ? 'مفتاح معتمد ومخزن' : 'غير مهيأ'}
                  </span>
                </div>
              </div>

              <div className="p-3.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] text-[11px] text-[var(--muted)] flex items-center gap-2">
                <ShieldCheck size={16} className="text-[var(--accent)] shrink-0" />
                <span>
                  قاعدة الأمان الصارمة: لا يتم عرض نصوص ملف JSON أو المفاتيح السرية إطلاقاً في واجهة المستخدم.
                </span>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Tab 4: App Statement Preview (F3 - R4.8) */}
      {activeTab === 'statement-preview' && (
        <div className="space-y-4">
          <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  كشف الحساب في تطبيق المندوب — معاينة الحساب الرقمي (R4.8)
                </h3>
                <p className="text-xs text-[var(--muted)] mt-0.5">
                  محاكاة واجهة كشف الحساب المبسطة للمندوب باستخدام بيانات GET /api/finance/statement/rider/{'{iqama}'}/monthly.
                </p>
              </div>

              {/* Selector toolbar */}
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  placeholder="رقم الإقامة..."
                  value={statementIqama}
                  onChange={(e) => setStatementIqama(e.target.value)}
                  className="p-1.5 rounded-xl border border-[var(--line)] text-xs font-mono w-32 focus:outline-hidden"
                />
                <input
                  type="month"
                  value={statementPeriod}
                  onChange={(e) => setStatementPeriod(e.target.value)}
                  className="p-1.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
                />
                <button
                  type="button"
                  onClick={loadStatementPreview}
                  className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold hover:bg-[var(--line)] transition cursor-pointer"
                >
                  تحديث
                </button>
                <Link
                  href={`/riders/statement/?iqama=${encodeURIComponent(statementIqama)}`}
                  className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5"
                >
                  <Eye size={14} />
                  <span>فتح كشف الحساب المعتمد</span>
                </Link>
              </div>
            </div>

            {loadingStatement ? (
              <div className="p-8 max-w-xs mx-auto space-y-3">
                <Skeleton className="h-40 w-full rounded-2xl" />
                <Skeleton className="h-20 w-full rounded-2xl" />
              </div>
            ) : (
              /* Mobile frame preview */
              <div className="max-w-xs mx-auto border-4 border-black rounded-3xl p-4 bg-[var(--surface)] shadow-xl space-y-4 my-4">
                <div className="flex justify-between items-center text-[11px] text-[var(--muted)] pb-2 border-b border-[var(--line)]">
                  <span>تطبيق بوابة المندوب</span>
                  <span className="font-mono">12:30 PM</span>
                </div>

                <div className="p-3 rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] text-center space-y-1">
                  <div className="text-[10px] font-medium opacity-90">
                    {monthlyStatement?.header?.name || 'كشف الحساب الشهري'}
                  </div>
                  <div className="text-xl font-black num">
                    {(monthlyStatement?.net ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                  </div>
                  <div className="text-[9px] font-bold bg-white/20 rounded-full px-2 py-0.5 inline-block">
                    {monthlyStatement?.status === 'approved' ? 'معتمد' : 'معاينة مسودة'}
                  </div>
                </div>

                {/* App Cards in mobile */}
                {monthlyStatement?.cards && monthlyStatement.cards.length > 0 && (
                  <div className="space-y-1.5">
                    <span className="text-[10px] text-[var(--muted)] font-bold block">مستحقات المنصات:</span>
                    {monthlyStatement.cards.map((c: any, i: number) => (
                      <div key={i} className="p-2 rounded-xl bg-[var(--raised)]/60 text-xs flex justify-between items-center">
                        <span className="font-semibold">{c.platform || c.app}</span>
                        <span className="num font-bold">
                          {(c.gross || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </span>
                      </div>
                    ))}
                  </div>
                )}

                {/* Deductions in mobile */}
                {monthlyStatement?.deductions && monthlyStatement.deductions.length > 0 && (
                  <div className="space-y-1.5">
                    <span className="text-[10px] text-[var(--danger)] font-bold block">الاستقطاعات والتحملات:</span>
                    {monthlyStatement.deductions.map((d: any, i: number) => (
                      <div key={i} className="p-2 rounded-xl bg-[var(--danger)]/5 border border-[var(--danger)]/20 text-xs flex justify-between items-center">
                        <span className="text-[var(--ink)]">{d.description || d.category}</span>
                        <span className="num font-bold text-[var(--danger)]">
                          -{(d.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </span>
                      </div>
                    ))}
                  </div>
                )}

                <div className="space-y-2 text-xs pt-2 border-t border-[var(--line)]">
                  <div className="flex justify-between py-1 border-b border-[var(--line)]">
                    <span className="text-[var(--muted)]">الرصيد المرحل:</span>
                    <span className="num font-medium">
                      {(monthlyStatement?.carried ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                    </span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-[var(--line)]">
                    <span className="text-[var(--muted)]">حركة الشهر (الصافي):</span>
                    <span className="num font-bold text-[var(--accent)]">
                      {(monthlyStatement?.net ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                    </span>
                  </div>
                  <div className="flex justify-between py-1">
                    <span className="text-[var(--muted)]">الرصيد الختامي:</span>
                    <span className="num font-bold">
                      {(monthlyStatement?.closing ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                    </span>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Tab 5: Requests & Appointments */}
      {activeTab === 'requests' && (
        <div className="space-y-4">
          <div className="p-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-3 shadow-xs">
            <span className="px-3 py-1 rounded-full bg-[var(--raised)] text-xs font-bold text-[var(--muted)]">
              لاحقاً
            </span>
            <h4 className="text-base font-bold text-[var(--ink)]">
              بوابة طلبات المناديب والمواعيد الميدانية
            </h4>
            <p className="text-xs sm:text-sm text-[var(--muted)] max-w-md mx-auto leading-relaxed">
              وفق ترتيب الأولويات المعتمد من غزال (كشف الحساب ➔ الإشعارات ➔ الطلبات والمواعيد)، سيتم إطلاق موديول الطلبات والمواعيد في المرحلة اللاحقة Track M.
            </p>
          </div>
        </div>
      )}

      {/* Send Broadcast Dialog */}
      <Dialog
        open={sendDialogOpen}
        onClose={() => setSendDialogOpen(false)}
        title="بث إشعار جديد لتطبيق المناديب"
      >
        <div className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">الفئة المستهدفة:</label>
            <div className="grid grid-cols-2 gap-2">
              <label className="flex items-center gap-2 p-2 rounded-xl border border-[var(--line)] text-xs cursor-pointer hover:bg-[var(--raised)]">
                <input
                  type="radio"
                  name="targetType"
                  checked={targetType === 'ALL'}
                  onChange={() => setTargetType('ALL')}
                />
                <span className="font-semibold">كافة المناديب (ALL)</span>
              </label>
              <label className="flex items-center gap-2 p-2 rounded-xl border border-[var(--line)] text-xs cursor-pointer hover:bg-[var(--raised)]">
                <input
                  type="radio"
                  name="targetType"
                  checked={targetType === 'specific'}
                  onChange={() => setTargetType('specific')}
                />
                <span className="font-semibold">مندوب محدد برقم الإقامة</span>
              </label>
            </div>
          </div>

          {targetType === 'specific' && (
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">رقم إقامة المندوب المستهدف:</label>
              <input
                type="text"
                placeholder="2000000789"
                value={targetIqama}
                onChange={(e) => setTargetIqama(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">تصنيف الإشعار:</label>
              <select
                value={broadcastCategory}
                onChange={(e) => setBroadcastCategory(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              >
                <option value="operational">تشغيلي وميداني</option>
                <option value="financial">مالي ومستحقات</option>
                <option value="form_link">رابط استبيان / نموذج</option>
                <option value="administrative">إداري وتنبيه عام</option>
              </select>
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">تأكيد القراءة والاستلام:</label>
              <label className="flex items-center gap-2 p-2.5 rounded-xl border border-[var(--line)] text-xs cursor-pointer hover:bg-[var(--raised)]">
                <input
                  type="checkbox"
                  checked={requiresAck}
                  onChange={(e) => setRequiresAck(e.target.checked)}
                />
                <span>يتطلب توقيع المندوب</span>
              </label>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">عنوان الإشعار:</label>
            <input
              type="text"
              placeholder="مثال: تعميم هام بشأن استلام العهد الشهرية"
              value={broadcastTitle}
              onChange={(e) => setBroadcastTitle(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">نص وتفاصيل الإشعار:</label>
            <textarea
              rows={3}
              placeholder="اكتب التوجيه أو التعليمات الموجهة للمندوب..."
              value={broadcastBody}
              onChange={(e) => setBroadcastBody(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">رابط الإجراء التفاعلي (اختياري):</label>
            <input
              type="text"
              placeholder="مثال: /app/forms/?tab=campaigns"
              value={broadcastActionUrl}
              onChange={(e) => setBroadcastActionUrl(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setSendDialogOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={sendingBroadcast}
              onClick={handleSendBroadcast}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
            >
              {sendingBroadcast ? 'جاري الإرسال...' : 'بث الإشعار الآن'}
            </button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

export default function RiderAppAdminPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <RiderAppAdminPageInner />
    </Suspense>
  );
}
