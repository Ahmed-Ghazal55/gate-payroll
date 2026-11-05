'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import {
  ShieldCheck,
  Users,
  History,
  Settings,
  UserPlus,
  RefreshCw,
  Search,
  Filter,
  Download,
  Lock,
  Unlock,
  KeyRound,
  CheckCircle2,
  AlertCircle,
  Eye,
  EyeOff,
  Clock,
  Sparkles,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';

function AdminPageInner() {
  const { user, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'users';
  const [activeTab, setActiveTab] = useState<string>(tabFromUrl);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  // Users State
  const [users, setUsers] = useState<any[]>([]);
  const [loadingUsers, setLoadingUsers] = useState<boolean>(true);
  const [usersError, setUsersError] = useState<string | null>(null);
  const [userSearch, setUserSearch] = useState<string>('');

  // Add User Dialog State
  const [addUserOpen, setAddUserOpen] = useState<boolean>(false);
  const [newUsername, setNewUsername] = useState<string>('');
  const [newPassword, setNewPassword] = useState<string>('');
  const [newRole, setNewRole] = useState<string>('Accountant');
  const [newDept, setNewDept] = useState<string>('FIN');
  const [creatingUser, setCreatingUser] = useState<boolean>(false);

  // Audit Logs State
  const [auditLogs, setAuditLogs] = useState<any[]>([]);
  const [loadingAudit, setLoadingAudit] = useState<boolean>(true);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditSearch, setAuditSearch] = useState<string>('');
  const [auditActionFilter, setAuditActionFilter] = useState<string>('all');

  // Firebase / System Settings State
  const [firebaseConfig, setFirebaseConfig] = useState<any>(null);
  const [loadingSettings, setLoadingSettings] = useState<boolean>(true);
  const [savingSettings, setSavingSettings] = useState<boolean>(false);
  const [fbProjectId, setFbProjectId] = useState<string>('');
  const [fbClientEmail, setFbClientEmail] = useState<string>('');

  // Load Users
  const loadUsers = async () => {
    setLoadingUsers(true);
    setUsersError(null);
    try {
      const res = await api.getUsers();
      setUsers(Array.isArray(res) ? res : []);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل قائمة المستخدمين';
      setUsersError(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingUsers(false);
    }
  };

  // Load Audit
  const loadAudit = async () => {
    setLoadingAudit(true);
    setAuditError(null);
    try {
      const res = await api.getAuditLogs();
      setAuditLogs(Array.isArray(res) ? res : []);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل سجل التدقيق';
      setAuditError(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingAudit(false);
    }
  };

  // Load Settings
  const loadSettings = async () => {
    setLoadingSettings(true);
    try {
      const res = await api.getFirebaseConfig();
      setFirebaseConfig(res);
      if (res?.projectId) setFbProjectId(res.projectId);
      if (res?.clientEmail) setFbClientEmail(res.clientEmail);
    } catch (_) {
      // setting not initialized yet
      setFirebaseConfig(null);
    } finally {
      setLoadingSettings(false);
    }
  };

  useEffect(() => {
    loadUsers();
    loadAudit();
    loadSettings();
  }, []);

  // Create User
  const handleCreateUser = async () => {
    if (!newUsername.trim() || !newPassword.trim()) {
      toast('يرجى إدخال اسم المستخدم وكلمة المرور', 'warning');
      return;
    }
    setCreatingUser(true);
    try {
      await api.createUser({
        username: newUsername.trim(),
        password: newPassword,
        role: newRole,
        department: newDept,
      });
      toast(`تم إنشاء حساب المستخدم @${newUsername.trim()} بنجاح`, 'success');
      setAddUserOpen(false);
      setNewUsername('');
      setNewPassword('');
      loadUsers();
    } catch (err: any) {
      toast(err.message || 'فشل إنشاء المستخدم', 'danger');
    } finally {
      setCreatingUser(false);
    }
  };

  // Filtered Users
  const filteredUsers = useMemo(() => {
    return users.filter((u) => {
      if (!userSearch.trim()) return true;
      const q = userSearch.toLowerCase().trim();
      return (
        (u.username && u.username.toLowerCase().includes(q)) ||
        (u.role && u.role.toLowerCase().includes(q)) ||
        (u.department && u.department.toLowerCase().includes(q))
      );
    });
  }, [users, userSearch]);

  // Filtered Audit Logs
  const filteredAudit = useMemo(() => {
    return auditLogs.filter((l) => {
      if (auditActionFilter !== 'all' && !l.action?.includes(auditActionFilter)) return false;
      if (!auditSearch.trim()) return true;
      const q = auditSearch.toLowerCase().trim();
      return (
        (l.action && l.action.toLowerCase().includes(q)) ||
        (l.actor && l.actor.toLowerCase().includes(q)) ||
        (l.id && l.id.toLowerCase().includes(q))
      );
    });
  }, [auditLogs, auditActionFilter, auditSearch]);

  const sectionTabs: SectionTabItem[] = [
    { id: 'users', label: 'المستخدمون والصلاحيات', icon: <Users size={16} /> },
    { id: 'audit', label: 'سجل التدقيق الشامل', icon: <History size={16} />, badge: auditLogs.length },
    { id: 'settings', label: 'إعدادات المنظومة', icon: <Settings size={16} /> },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الإدارة وسجل التدقيق"
        eyebrow="التحكم والأمن"
        description="إدارة حسابات المستخدمين وصلاحيات الوصول، فحص سجل التدقيق الشامل لكافة العمليات المالية، وإعدادات ربط المنظومة."
        primaryAction={{
          label: 'إضافة مستخدم جديد',
          icon: <UserPlus size={15} />,
          onClick: () => setAddUserOpen(true),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي الحسابات المسجلة"
          value={users.length}
          hint={`${users.filter((u) => u.status === 'active').length} حساب نشط`}
          icon={Users}
        />
        <StatCard
          label="المسؤولون والمدققون"
          value={users.filter((u) => u.role === 'Admin' || u.role === 'SuperAdmin').length}
          hint="صلاحيات إدارة كاملة"
          icon={ShieldCheck}
        />
        <StatCard
          label="سجلات التدقيق المسجلة"
          value={auditLogs.length}
          hint="سجل غير قابل للتعديل"
          icon={History}
        />
        <StatCard
          label="حالة الربط والأمان"
          value="آمن ومعتمد"
          hint="تشفير جلسات PBKDF2 و CSP"
          icon={CheckCircle2}
        />
      </div>

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(tabId) => setActiveTab(tabId)}
      />

      {/* Tab 1: Users */}
      {activeTab === 'users' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="relative flex-1 max-w-sm">
              <Search size={15} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
              <input
                type="text"
                placeholder="بحث باسم المستخدم أو الدور أو القسم..."
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
                className="w-full ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm focus:outline-hidden"
              />
            </div>
            <button
              type="button"
              onClick={() => setAddUserOpen(true)}
              className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer"
            >
              <UserPlus size={14} />
              <span>مستخدم جديد</span>
            </button>
          </div>

          {usersError ? (
            <ErrorState title="تعذر تحميل المستخدمين" message={usersError} onRetry={loadUsers} />
          ) : loadingUsers ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : filteredUsers.length === 0 ? (
            <EmptyState
              title="لا يوجد مستخدمون مطابقون"
              description="لم يتم العثور على أي حسابات تطابق خيارات البحث."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">اسم المستخدم</th>
                      <th className="p-3 text-start">الدور الوظيفي</th>
                      <th className="p-3 text-start">القسم</th>
                      <th className="p-3 text-start">رتبة الاعتماد</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">تاريخ الإنشاء</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {filteredUsers.map((u) => (
                      <tr key={u.id || u.username} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--ink)]">
                          @{u.username}
                        </td>
                        <td className="p-3">
                          <span
                            className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                              u.role === 'Admin' || u.role === 'SuperAdmin'
                                ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
                                : u.role === 'Accountant'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : 'bg-[var(--raised)] text-[var(--ink)]'
                            }`}
                          >
                            {u.role}
                          </span>
                        </td>
                        <td className="p-3 font-medium">{u.department || 'FIN'}</td>
                        <td className="p-3 num font-mono">المستوى {u.rankLevel || 1}</td>
                        <td className="p-3">
                          <span
                            className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                              u.status === 'active'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                            }`}
                          >
                            {u.status === 'active' ? 'نشط' : 'معلّق'}
                          </span>
                        </td>
                        <td className="p-3 num text-xs text-[var(--muted)]">{u.createdAt || '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Audit Logs */}
      {activeTab === 'audit' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex flex-wrap items-center gap-2 flex-1 min-w-[240px]">
              <div className="relative flex-1 max-w-sm">
                <Search size={15} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
                <input
                  type="text"
                  placeholder="بحث في العمليات أو المنفذين..."
                  value={auditSearch}
                  onChange={(e) => setAuditSearch(e.target.value)}
                  className="w-full ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm focus:outline-hidden"
                />
              </div>

              <select
                value={auditActionFilter}
                onChange={(e) => setAuditActionFilter(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-xl px-3 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل التصنيفات</option>
                <option value="platform.hs">فواتير وتسويات هنقرستيشن</option>
                <option value="finance">الخزينة والمالية</option>
                <option value="rider">المناديب والدليل</option>
                <option value="admin">إدارة المستخدمين</option>
              </select>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-xs text-[var(--muted)]">
                {filteredAudit.length} عملية مسجلة
              </span>
            </div>
          </div>

          {auditError ? (
            <ErrorState title="تعذر تحميل سجل التدقيق" message={auditError} onRetry={loadAudit} />
          ) : loadingAudit ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : filteredAudit.length === 0 ? (
            <EmptyState
              title="لا توجد عمليات تدقيق مطابقة"
              description="سجل العمليات يسجل تلقائياً كافة إجراءات الإنشاء والاعتماد والتعديل."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">المعرف</th>
                      <th className="p-3 text-start">نوع العملية (Action)</th>
                      <th className="p-3 text-start">المنفذ (Actor)</th>
                      <th className="p-3 text-start">التاريخ والوقت</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {filteredAudit.map((log, idx) => (
                      <tr key={log.id || idx} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs text-[var(--muted)]">{log.id || idx + 1}</td>
                        <td className="p-3">
                          <span className="font-mono text-xs font-bold text-[var(--accent)] bg-[var(--accent)]/10 px-2 py-0.5 rounded-md">
                            {log.action}
                          </span>
                        </td>
                        <td className="p-3 font-mono font-semibold text-[var(--ink)]">
                          @{log.actor || 'system'}
                        </td>
                        <td className="p-3 num text-xs text-[var(--muted)] whitespace-nowrap">
                          {log.at || log.createdAt || '-'}
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

      {/* Tab 3: Settings */}
      {activeTab === 'settings' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              إعدادات الربط والمنظومة الخارجية
            </h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              تهيئة وإدارة اتصالات Firebase Cloud Messaging لإشعارات المناديب ومفاتيح التكامل الرسمية.
            </p>

            <div className="mt-4 pt-4 border-t border-[var(--line)] space-y-4 max-w-xl">
              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--ink)]">معرف مشروع Firebase (Project ID):</label>
                <input
                  type="text"
                  placeholder="gate-payroll-app"
                  value={fbProjectId}
                  onChange={(e) => setFbProjectId(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--ink)]">البريد الإلكتروني للخدمة (Client Email):</label>
                <input
                  type="text"
                  placeholder="firebase-adminsdk@gate-payroll.iam.gserviceaccount.com"
                  value={fbClientEmail}
                  onChange={(e) => setFbClientEmail(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
                />
              </div>

              <div className="p-3.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs text-[var(--muted)] flex items-center gap-2">
                <ShieldCheck size={16} className="text-[var(--accent)] shrink-0" />
                <span>
                  مفاتيح الربط الحساسة (Private Keys) محمية بالكامل ومخزنة مشفرة داخل الخادم، ولا يتم عرضها بنصوص صريحة حفاظاً على الأمان.
                </span>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Add User Dialog */}
      <Dialog
        open={addUserOpen}
        onClose={() => setAddUserOpen(false)}
        title="إنشاء حساب مستخدم جديد"
      >
        <div className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">اسم المستخدم (Username):</label>
            <input
              type="text"
              placeholder="e.g. ahmed.ali"
              value={newUsername}
              onChange={(e) => setNewUsername(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">كلمة المرور المؤقتة:</label>
            <input
              type="password"
              placeholder="••••••••"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">الدور الوظيفي (Role):</label>
              <select
                value={newRole}
                onChange={(e) => setNewRole(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              >
                <option value="Accountant">محاسب (Accountant)</option>
                <option value="Manager">مدير تشغيل (Manager)</option>
                <option value="Admin">مسؤول نظام (Admin)</option>
                <option value="Viewer">قارئ تقارير (Viewer)</option>
              </select>
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">القسم (Department):</label>
              <select
                value={newDept}
                onChange={(e) => setNewDept(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              >
                <option value="FIN">المالية والحسابات (FIN)</option>
                <option value="OPS">التشغيل الميداني (OPS)</option>
                <option value="HR">الموارد البشرية (HR)</option>
                <option value="MGMT">الإدارة العامة (MGMT)</option>
              </select>
            </div>
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setAddUserOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={creatingUser}
              onClick={handleCreateUser}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {creatingUser ? 'جاري الإنشاء...' : 'حفظ وإنشاء الحساب'}
            </button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

export default function AdminPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <AdminPageInner />
    </Suspense>
  );
}
