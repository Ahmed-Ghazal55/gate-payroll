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
  Building2,
  Check,
  X,
  Plus,
  UserCheck,
  Briefcase,
  SlidersHorizontal,
  Send,
  AlertTriangle,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import type {
  OrgCatalog,
  OrgMembership,
  OrgDelegation,
  OrgSupervision,
  OrgRouting,
  OrgResponsibleUser,
} from '../../lib/types';

function AdminPageInner() {
  const { user, can, isAdmin } = useAuth();
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

  // Audit State
  const [auditLogs, setAuditLogs] = useState<any[]>([]);
  const [loadingAudit, setLoadingAudit] = useState<boolean>(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditSearch, setAuditSearch] = useState<string>('');
  const [auditActionFilter, setAuditActionFilter] = useState<string>('all');

  // Firebase Config State
  const [fbProjectId, setFbProjectId] = useState<string>('gate-payroll-app');
  const [fbClientEmail, setFbClientEmail] = useState<string>('firebase-adminsdk@gate-payroll.iam.gserviceaccount.com');

  // ========================================================
  // R5-3 ORG DIRECTORY STATES (BR §25.1)
  // ========================================================
  const [orgSubTab, setOrgSubTab] = useState<'memberships' | 'delegations' | 'supervision' | 'routing'>('memberships');
  const [orgCatalog, setOrgCatalog] = useState<OrgCatalog | null>(null);
  const [memberships, setMemberships] = useState<OrgMembership[]>([]);
  const [delegations, setDelegations] = useState<OrgDelegation[]>([]);
  const [supervisions, setSupervisions] = useState<OrgSupervision[]>([]);
  const [routings, setRoutings] = useState<OrgRouting[]>([]);
  const [loadingOrg, setLoadingOrg] = useState<boolean>(false);
  const [orgError, setOrgError] = useState<string | null>(null);

  // Modals for Org creation
  const [addMembershipOpen, setAddMembershipOpen] = useState<boolean>(false);
  const [memUserId, setMemUserId] = useState<string>('');
  const [memDept, setMemDept] = useState<string>('FIN');
  const [memRank, setMemRank] = useState<string>('A');
  const [memValidFrom, setMemValidFrom] = useState<string>('2026-01-01');
  const [submittingMem, setSubmittingMem] = useState<boolean>(false);

  const [addDelegationOpen, setAddDelegationOpen] = useState<boolean>(false);
  const [delFromUser, setDelFromUser] = useState<string>('');
  const [delToUser, setDelToUser] = useState<string>('');
  const [delOperation, setDelOperation] = useState<string>('advance.review');
  const [delValidFrom, setDelValidFrom] = useState<string>('2026-01-01');
  const [delUntil, setDelUntil] = useState<string>('2026-12-31');
  const [delReason, setDelReason] = useState<string>('');
  const [submittingDel, setSubmittingDel] = useState<boolean>(false);

  const [addSupervisionOpen, setAddSupervisionOpen] = useState<boolean>(false);
  const [supSupervisor, setSupSupervisor] = useState<string>('');
  const [supIqama, setSupIqama] = useState<string>('');
  const [supValidFrom, setSupValidFrom] = useState<string>('2026-01-01');
  const [submittingSup, setSubmittingSup] = useState<boolean>(false);

  const [addRoutingOpen, setAddRoutingOpen] = useState<boolean>(false);
  const [routeOperation, setRouteOperation] = useState<string>('advance.review');
  const [routeDept, setRouteDept] = useState<string>('FIN');
  const [routeMinRank, setRouteMinRank] = useState<string>('B');
  const [routeUsers, setRouteUsers] = useState<string>('');
  const [routeValidFrom, setRouteValidFrom] = useState<string>('2026-01-01');
  const [submittingRoute, setSubmittingRoute] = useState<boolean>(false);

  // Responsible Users Query State
  const [queryOperation, setQueryOperation] = useState<string>('advance.review');
  const [responsibleUsers, setResponsibleUsers] = useState<OrgResponsibleUser[]>([]);
  const [loadingResponsible, setLoadingResponsible] = useState<boolean>(false);

  const canManageOrg = can('org.manage') || isAdmin;

  // Load Users
  const loadUsers = async () => {
    setLoadingUsers(true);
    setUsersError(null);
    try {
      const res = await api.getUsers();
      setUsers(Array.isArray(res) ? res : []);
    } catch (err: any) {
      setUsersError(err.message || 'تعذر تحميل قائمة المستخدمين');
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
      setAuditError(err.message || 'تعذر تحميل سجل التدقيق');
    } finally {
      setLoadingAudit(false);
    }
  };

  // Load Org Directory Data
  const loadOrgData = async () => {
    setLoadingOrg(true);
    setOrgError(null);
    try {
      const [catRes, memRes, delRes, supRes, routeRes] = await Promise.all([
        api.getOrgCatalog(),
        api.getOrgMemberships(),
        api.getOrgDelegations(),
        api.getOrgSupervision(),
        api.getOrgRouting(),
      ]);
      setOrgCatalog(catRes);
      setMemberships(Array.isArray(memRes) ? memRes : []);
      setDelegations(Array.isArray(delRes) ? delRes : []);
      setSupervisions(Array.isArray(supRes) ? supRes : []);
      setRoutings(Array.isArray(routeRes) ? routeRes : []);
    } catch (err: any) {
      setOrgError(err.message || 'تعذر تحميل بيانات سجل التنظيم والرتب');
    } finally {
      setLoadingOrg(false);
    }
  };

  const loadResponsible = async (op: string) => {
    if (!op.trim()) return;
    setLoadingResponsible(true);
    try {
      const res = await api.getOrgResponsible(op.trim());
      setResponsibleUsers(Array.isArray(res) ? res : []);
    } catch (err: any) {
      toast(err.message || 'تعذر استعلام المسؤولين عن العملية', 'warning');
      setResponsibleUsers([]);
    } finally {
      setLoadingResponsible(false);
    }
  };

  useEffect(() => {
    loadUsers();
  }, []);

  useEffect(() => {
    if (activeTab === 'audit') {
      loadAudit();
    } else if (activeTab === 'org') {
      loadOrgData();
      loadResponsible(queryOperation);
    }
  }, [activeTab]);

  // Create User
  const handleCreateUser = async () => {
    if (!newUsername.trim() || !newPassword.trim()) {
      toast('اسم المستخدم وكلمة المرور مطلوبان', 'warning');
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
      toast(`تم إنشاء المستخدم @${newUsername} بنجاح`, 'success');
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

  // Create Membership
  const handleCreateMembership = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!memUserId.trim()) {
      toast('يرجى اختيار المستخدم', 'warning');
      return;
    }
    setSubmittingMem(true);
    try {
      await api.createOrgMembership({
        userId: memUserId.trim(),
        department: memDept,
        rank: memRank,
        validFrom: memValidFrom || undefined,
      });
      toast('تم تسجيل عضوية القسم والرتبة بنجاح', 'success');
      setAddMembershipOpen(false);
      setMemUserId('');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل إضافة العضوية', 'danger');
    } finally {
      setSubmittingMem(false);
    }
  };

  const handleEndMembership = async (id: string) => {
    try {
      await api.endOrgMembership(id);
      toast('تم إنهاء العضوية وتحديث السجل الزمني', 'success');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل إنهاء العضوية', 'danger');
    }
  };

  // Create Delegation
  const handleCreateDelegation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!delFromUser.trim() || !delToUser.trim() || !delUntil.trim()) {
      toast('يرجى تحديد أطراف التفويض وتاريخ الانتهاء الإلزامي', 'warning');
      return;
    }
    setSubmittingDel(true);
    try {
      await api.createOrgDelegation({
        fromUser: delFromUser.trim(),
        toUser: delToUser.trim(),
        operation: delOperation.trim(),
        validFrom: delValidFrom || undefined,
        until: delUntil.trim(),
        reason: delReason.trim() || undefined,
      });
      toast('تم تسجيل التفويض الإداري بنجاح', 'success');
      setAddDelegationOpen(false);
      setDelFromUser('');
      setDelToUser('');
      setDelReason('');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل تسجيل التفويض', 'danger');
    } finally {
      setSubmittingDel(false);
    }
  };

  const handleEndDelegation = async (id: string) => {
    try {
      await api.endOrgDelegation(id);
      toast('تم إنهاء التفويض بنجاح', 'success');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل إنهاء التفويض', 'danger');
    }
  };

  // Create Supervision
  const handleCreateSupervision = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!supSupervisor.trim() || !supIqama.trim()) {
      toast('يرجى اختيار المشرف وإدخال رقم إقامة المندوب', 'warning');
      return;
    }
    setSubmittingSup(true);
    try {
      await api.createOrgSupervision({
        supervisorUserId: supSupervisor.trim(),
        riderIqama: supIqama.trim(),
        validFrom: supValidFrom || undefined,
      });
      toast('تم تعيين إشراف المشرف على المندوب بنجاح', 'success');
      setAddSupervisionOpen(false);
      setSupSupervisor('');
      setSupIqama('');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل تسجيل الإشراف', 'danger');
    } finally {
      setSubmittingSup(false);
    }
  };

  const handleEndSupervision = async (id: string) => {
    try {
      await api.endOrgSupervision(id);
      toast('تم إنهاء إشراف المشرف على المندوب', 'success');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل إنهاء الإشراف', 'danger');
    }
  };

  // Create Routing
  const handleCreateRouting = async (e: React.FormEvent) => {
    e.preventDefault();
    const parsedUsers = routeUsers
      .split(',')
      .map((u) => u.trim())
      .filter(Boolean);
    if (!routeOperation.trim() || parsedUsers.length === 0) {
      toast('العملية وتحديد المستخدمين على الأقل مطلوبان', 'warning');
      return;
    }
    setSubmittingRoute(true);
    try {
      await api.createOrgRouting({
        operation: routeOperation.trim(),
        department: routeDept,
        minRank: routeMinRank,
        users: parsedUsers,
        validFrom: routeValidFrom || undefined,
      });
      toast('تم تحديث جدول التوجيه وتكليف المسؤولين بنجاح', 'success');
      setAddRoutingOpen(false);
      setRouteUsers('');
      loadOrgData();
      loadResponsible(routeOperation.trim());
    } catch (err: any) {
      toast(err.message || 'فشل تسجيل التوجيه', 'danger');
    } finally {
      setSubmittingRoute(false);
    }
  };

  const handleEndRouting = async (id: string) => {
    try {
      await api.endOrgRouting(id);
      toast('تم إنهاء سجل التوجيه', 'success');
      loadOrgData();
    } catch (err: any) {
      toast(err.message || 'فشل إنهاء التوجيه', 'danger');
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
    { id: 'org', label: 'الأقسام والرتب والتفويضات', icon: <Building2 size={16} /> },
    { id: 'audit', label: 'سجل التدقيق الشامل', icon: <History size={16} />, badge: auditLogs.length },
    { id: 'settings', label: 'إعدادات المنظومة', icon: <Settings size={16} /> },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الإدارة والتنظيم المؤسسي"
        eyebrow="التحكم والأمن ونظام الرتب"
        description="إدارة حسابات المستخدمين، سجل الأقسام والرتب والتفويضات (BR §25.1)، تعيين المشرفين وتوجيه مسؤولي مراجعة وصرف السلف، وفحص سجل التدقيق."
        primaryAction={{
          label: activeTab === 'org' ? 'تحديث سجل التنظيم' : 'إضافة مستخدم جديد',
          icon: activeTab === 'org' ? <RefreshCw size={15} /> : <UserPlus size={15} />,
          onClick: activeTab === 'org' ? loadOrgData : () => setAddUserOpen(true),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي المستخدمين المسجلين"
          value={users.length}
          hint="حسابات نشطة في المنظومة"
          icon={Users}
        />
        <StatCard
          label="العضويات النشطة بالأقسام"
          value={memberships.filter((m) => !m.validTo).length}
          hint="أعضاء مثبتون بالرتب A+/A/B/C"
          icon={Building2}
        />
        <StatCard
          label="المسؤولين المكلفين بالسلف"
          value={responsibleUsers.length}
          hint={`مكلفون بالعملية: ${queryOperation}`}
          icon={UserCheck}
        />
        <StatCard
          label="عمليات سجل التدقيق"
          value={auditLogs.length}
          hint="سجل الحركات والأمان المالي"
          icon={History}
        />
      </div>

      {/* Section Tabs */}
      <SectionTabs tabs={sectionTabs} activeTab={activeTab} onChange={setActiveTab} />

      {/* ======================================================== */}
      {/* Tab 1: المستخدمون والصلاحيات                              */}
      {/* ======================================================== */}
      {activeTab === 'users' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                placeholder="بحث باسم المستخدم أو الدور أو القسم..."
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
                className="w-full bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setAddUserOpen(true)}
                className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
              >
                <UserPlus size={14} />
                <span>إضافة مستخدم</span>
              </button>
            </div>
          </div>

          {usersError ? (
            <ErrorState title="تعذر تحميل المستخدمين" message={usersError} onRetry={loadUsers} />
          ) : loadingUsers ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : filteredUsers.length === 0 ? (
            <EmptyState
              title="لم يتم العثور على مستخدمين"
              description="لا يوجد مستخدمون يطابقون معايير البحث المدخلة."
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
                      <th className="p-3 text-start">الصلاحيات المخصصة</th>
                      <th className="p-3 text-start">الحالة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {filteredUsers.map((u) => (
                      <tr key={u.id || u.username} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--ink)]">
                          @{u.username}
                        </td>
                        <td className="p-3 font-medium">{u.role}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-md text-xs font-mono bg-[var(--raised)]">
                            {u.department || 'ALL'}
                          </span>
                        </td>
                        <td className="p-3 text-xs text-[var(--muted)] max-w-xs truncate">
                          {u.customPermissions && u.customPermissions.length > 0
                            ? u.customPermissions.join(', ')
                            : 'حسب الدور الأساسي'}
                        </td>
                        <td className="p-3">
                          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                            <CheckCircle2 size={12} />
                            <span>نشط</span>
                          </span>
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

      {/* ======================================================== */}
      {/* Tab 2: الأقسام والرتب والتفويضات (BR §25.1)                */}
      {/* ======================================================== */}
      {activeTab === 'org' && (
        <div className="space-y-5">
          {/* Sub-tab pills */}
          <div className="flex items-center gap-2 border-b border-[var(--line)] overflow-x-auto no-scrollbar">
            {[
              { id: 'memberships' as const, label: 'العضويات والرتب', count: memberships.length },
              { id: 'delegations' as const, label: 'التفويضات الإدارية', count: delegations.length },
              { id: 'supervision' as const, label: 'إشراف المناديب', count: supervisions.length },
              { id: 'routing' as const, label: 'التوجيه وتكليف المسؤولين', count: routings.length },
            ].map((tab) => (
              <button
                key={tab.id}
                id={`org-tab-${tab.id}`}
                type="button"
                onClick={() => setOrgSubTab(tab.id)}
                className={`py-2 px-3 text-xs sm:text-sm font-semibold border-b-2 transition whitespace-nowrap cursor-pointer ${
                  orgSubTab === tab.id
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <span>{tab.label}</span>
                {tab.count !== undefined && tab.count > 0 && (
                  <span className="ms-1.5 px-1.5 py-0.2 rounded-full text-[10px] bg-[var(--raised)] text-[var(--ink)] font-bold">
                    {tab.count}
                  </span>
                )}
              </button>
            ))}
          </div>

          {orgError && (
            <ErrorState title="تعذر تحميل سجل التنظيم" message={orgError} onRetry={loadOrgData} />
          )}

          {/* Sub-tab 1: Memberships */}
          {orgSubTab === 'memberships' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div>
                  <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                    عضويات الأقسام ومستويات الرتب (A+ / A / B / C)
                  </h4>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">
                    السجلات تاريخية ومؤرخة؛ إنهاء العضوية يحدد تاريخ الانتهاء دون حذف السجل لحفظ التدقيق.
                  </p>
                </div>
                {canManageOrg && (
                  <button
                    type="button"
                    id="btn-add-membership"
                    onClick={() => setAddMembershipOpen(true)}
                    className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
                  >
                    <Plus size={14} />
                    <span>إضافة عضوية ورتبة</span>
                  </button>
                )}
              </div>

              {loadingOrg ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : memberships.length === 0 ? (
                <EmptyState
                  title="لا توجد عضويات مسجلة"
                  description="استخدم زر الإضافة بالأعلى لتعيين المستخدمين في الأقسام والرتب."
                />
              ) : (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <th className="p-3 text-start">المستخدم</th>
                          <th className="p-3 text-start">القسم</th>
                          <th className="p-3 text-start">الرتبة</th>
                          <th className="p-3 text-start">سارٍ من</th>
                          <th className="p-3 text-start">سارٍ حتى</th>
                          <th className="p-3 text-start">الحالة</th>
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {memberships.map((m) => {
                          const isEnded = Boolean(m.validTo);
                          return (
                            <tr
                              key={m.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">@{m.userId}</td>
                              <td className="p-3">
                                <span className="px-2 py-0.5 rounded-md text-xs font-mono bg-[var(--raised)]">
                                  {m.department}
                                </span>
                              </td>
                              <td className="p-3 font-bold text-[var(--accent)]">{m.rank}</td>
                              <td className="p-3 num text-xs">{m.validFrom?.split('T')[0] || '—'}</td>
                              <td className="p-3 num text-xs">{m.validTo?.split('T')[0] || 'مستمر'}</td>
                              <td className="p-3">
                                <span
                                  className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                                    isEnded ? 'bg-[var(--muted)]/20 text-[var(--muted)]' : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                  }`}
                                >
                                  {isEnded ? 'منتهية' : 'نشطة'}
                                </span>
                              </td>
                              <td className="p-3">
                                {!isEnded && canManageOrg && (
                                  <button
                                    type="button"
                                    onClick={() => handleEndMembership(m.id)}
                                    className="px-2.5 py-1 rounded-lg border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                  >
                                    إنهاء العضوية
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Sub-tab 2: Delegations */}
          {orgSubTab === 'delegations' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div>
                  <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                    التفويضات الإدارية وتمرير الصلاحيات
                  </h4>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">
                    تفويض الصلاحية يشترط تاريخ انتهاء إلزامي (until)؛ التفويضات المنتهية تُلغى تلقائياً.
                  </p>
                </div>
                {canManageOrg && (
                  <button
                    type="button"
                    id="btn-add-delegation"
                    onClick={() => setAddDelegationOpen(true)}
                    className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
                  >
                    <Plus size={14} />
                    <span>إضافة تفويض جديد</span>
                  </button>
                )}
              </div>

              {loadingOrg ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : delegations.length === 0 ? (
                <EmptyState
                  title="لا توجد تفويضات مسجلة"
                  description="يمكنك إنشاء تفويض مؤقت بين مستخدمين لأداء عمليات محددة كالمراجعة أو الصرف."
                />
              ) : (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <th className="p-3 text-start">المفوض (من)</th>
                          <th className="p-3 text-start">المفوض إليه (إلى)</th>
                          <th className="p-3 text-start">العملية المفوضة</th>
                          <th className="p-3 text-start">سارٍ من</th>
                          <th className="p-3 text-start">حتى تاريخ</th>
                          <th className="p-3 text-start">السبب</th>
                          <th className="p-3 text-start">الحالة</th>
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {delegations.map((d) => {
                          const isExpired = d.until && new Date(d.until).getTime() < Date.now();
                          return (
                            <tr
                              key={d.id}
                              className={`transition ${
                                isExpired ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">@{d.fromUser}</td>
                              <td className="p-3 font-mono font-bold text-[var(--accent)]">@{d.toUser}</td>
                              <td className="p-3 font-mono text-xs">{d.operation}</td>
                              <td className="p-3 num text-xs">{d.validFrom?.split('T')[0] || '—'}</td>
                              <td className="p-3 num text-xs font-semibold">{d.until?.split('T')[0]}</td>
                              <td className="p-3 text-xs text-[var(--muted)]">{d.reason || '—'}</td>
                              <td className="p-3">
                                <span
                                  className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                                    isExpired ? 'bg-[var(--muted)]/20 text-[var(--muted)]' : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                  }`}
                                >
                                  {isExpired ? 'منتهٍ' : 'سارٍ'}
                                </span>
                              </td>
                              <td className="p-3">
                                {!isExpired && canManageOrg && (
                                  <button
                                    type="button"
                                    onClick={() => handleEndDelegation(d.id)}
                                    className="px-2.5 py-1 rounded-lg border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                  >
                                    إنهاء التفويض
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Sub-tab 3: Supervision */}
          {orgSubTab === 'supervision' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div>
                  <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                    إشراف المشرفين على المناديب (Supervision)
                  </h4>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">
                    يحدد المناديب المسموح للمشرف الميداني تقديم طلبات السلف نيابة عنهم.
                  </p>
                </div>
                {canManageOrg && (
                  <button
                    type="button"
                    id="btn-add-supervision"
                    onClick={() => setAddSupervisionOpen(true)}
                    className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
                  >
                    <Plus size={14} />
                    <span>تعيين إشراف جديد</span>
                  </button>
                )}
              </div>

              {loadingOrg ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : supervisions.length === 0 ? (
                <EmptyState
                  title="لا توجد سجلات إشراف مسجلة"
                  description="قم بتعيين المشرفين والمناديب التابعين لهم لتمكينهم من رفع طلبات السلف."
                />
              ) : (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <th className="p-3 text-start">المشرف المسؤول</th>
                          <th className="p-3 text-start">اسم المندوب</th>
                          <th className="p-3 text-start">رقم الإقامة</th>
                          <th className="p-3 text-start">سارٍ من</th>
                          <th className="p-3 text-start">سارٍ حتى</th>
                          <th className="p-3 text-start">الحالة</th>
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {supervisions.map((s) => {
                          const isEnded = Boolean(s.validTo);
                          return (
                            <tr
                              key={s.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">@{s.supervisorUserId}</td>
                              <td className="p-3 font-semibold">{s.riderName || 'مندوب مسجل'}</td>
                              <td className="p-3 font-mono text-[var(--accent)]">{s.riderIqama}</td>
                              <td className="p-3 num text-xs">{s.validFrom?.split('T')[0] || '—'}</td>
                              <td className="p-3 num text-xs">{s.validTo?.split('T')[0] || 'مستمر'}</td>
                              <td className="p-3">
                                <span
                                  className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                                    isEnded ? 'bg-[var(--muted)]/20 text-[var(--muted)]' : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                  }`}
                                >
                                  {isEnded ? 'منتهٍ' : 'نشط'}
                                </span>
                              </td>
                              <td className="p-3">
                                {!isEnded && canManageOrg && (
                                  <button
                                    type="button"
                                    onClick={() => handleEndSupervision(s.id)}
                                    className="px-2.5 py-1 rounded-lg border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                  >
                                    إنهاء الإشراف
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Sub-tab 4: Routing */}
          {orgSubTab === 'routing' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div>
                  <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                    جداول التوجيه وتكليف المسؤولين بالعمليات (Routing)
                  </h4>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">
                    توجيه مهام مراجعة السلف (advance.review) وصرف الخزن والبنوك (advance.payout:&lt;vault&gt;) للمستخدمين المؤهلين رتبوياً.
                  </p>
                </div>
                {canManageOrg && (
                  <button
                    type="button"
                    id="btn-add-routing"
                    onClick={() => setAddRoutingOpen(true)}
                    className="px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
                  >
                    <Plus size={14} />
                    <span>إضافة سجل توجيه</span>
                  </button>
                )}
              </div>

              {/* Responsible Users Inspection Bar */}
              <div className="p-4 rounded-xl border border-[var(--accent)]/30 bg-[var(--surface)] space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-bold text-[var(--ink)]">فحص المسؤولين المكلفين حالياً:</span>
                    <select
                      value={queryOperation}
                      onChange={(e) => {
                        setQueryOperation(e.target.value);
                        loadResponsible(e.target.value);
                      }}
                      className="p-1.5 rounded-xl border border-[var(--line)] text-xs font-mono font-bold focus:outline-hidden"
                    >
                      <option value="advance.review">advance.review (مراجعة السلف)</option>
                      <option value="advance.payout:MAIN.BW.DEBT">advance.payout:MAIN.BW.DEBT (صرف كفيل BW نقدية)</option>
                      <option value="advance.payout:BANK.BW">advance.payout:BANK.BW (صرف كفيل BW بنك)</option>
                    </select>
                  </div>
                  <button
                    type="button"
                    onClick={() => loadResponsible(queryOperation)}
                    className="px-3 py-1 rounded-lg border border-[var(--line)] text-xs font-bold hover:bg-[var(--raised)] transition cursor-pointer"
                  >
                    {loadingResponsible ? 'جاري الفحص...' : 'تحديث القائمة'}
                  </button>
                </div>

                <div className="flex items-center gap-2 flex-wrap">
                  {responsibleUsers.length > 0 ? (
                    responsibleUsers.map((ru, idx) => (
                      <span
                        key={idx}
                        className="inline-flex items-center gap-1.5 px-3 py-1 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs font-bold"
                      >
                        <UserCheck size={13} className="text-[var(--accent)]" />
                        <span>@{ru.userId}</span>
                        <span className="text-[10px] text-[var(--muted)] font-mono">
                          ({ru.department}/{ru.rank} — {ru.via === 'delegation' ? 'بتفويض' : 'تعيين مباشر'})
                        </span>
                      </span>
                    ))
                  ) : (
                    <span className="text-xs text-[var(--danger)]">
                      لا يوجد أي مسؤول مستوفٍ لشروط الرتبة والتوجيه لهذه العملية حالياً!
                    </span>
                  )}
                </div>
              </div>

              {loadingOrg ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : routings.length === 0 ? (
                <EmptyState
                  title="لا توجد سجلات توجيه مسجلة"
                  description="قم بتحديد المستخدمين الموجهة لهم مهام المراجعة والصرف لكل عملية وخزينة."
                />
              ) : (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <th className="p-3 text-start">العملية الموجهة</th>
                          <th className="p-3 text-start">القسم</th>
                          <th className="p-3 text-start">الحد الأدنى للرتبة</th>
                          <th className="p-3 text-start">المستخدمون المكلفون</th>
                          <th className="p-3 text-start">سارٍ من</th>
                          <th className="p-3 text-start">سارٍ حتى</th>
                          <th className="p-3 text-start">الحالة</th>
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {routings.map((r) => {
                          const isEnded = Boolean(r.validTo);
                          return (
                            <tr
                              key={r.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold text-[var(--accent)]">{r.operation}</td>
                              <td className="p-3 font-mono text-xs">{r.department}</td>
                              <td className="p-3 font-bold">{r.minRank}</td>
                              <td className="p-3">
                                <div className="flex items-center gap-1 flex-wrap">
                                  {r.users?.map((u, i) => (
                                    <span key={i} className="px-2 py-0.5 rounded-md bg-[var(--raised)] text-xs font-mono">
                                      @{u}
                                    </span>
                                  ))}
                                </div>
                              </td>
                              <td className="p-3 num text-xs">{r.validFrom?.split('T')[0] || '—'}</td>
                              <td className="p-3 num text-xs">{r.validTo?.split('T')[0] || 'مستمر'}</td>
                              <td className="p-3">
                                <span
                                  className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                                    isEnded ? 'bg-[var(--muted)]/20 text-[var(--muted)]' : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                  }`}
                                >
                                  {isEnded ? 'منتهٍ' : 'نشط'}
                                </span>
                              </td>
                              <td className="p-3">
                                {!isEnded && canManageOrg && (
                                  <button
                                    type="button"
                                    onClick={() => handleEndRouting(r.id)}
                                    className="px-2.5 py-1 rounded-lg border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                  >
                                    إنهاء التوجيه
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Tab 3: سجل التدقيق الشامل                                  */}
      {/* ======================================================== */}
      {activeTab === 'audit' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                placeholder="بحث برمز العملية، المعرف، أو المنفذ..."
                value={auditSearch}
                onChange={(e) => setAuditSearch(e.target.value)}
                className="w-full bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2">
              <select
                value={auditActionFilter}
                onChange={(e) => setAuditActionFilter(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-xl px-3 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل التصنيفات</option>
                <option value="advance">السلف والرتب</option>
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

      {/* ======================================================== */}
      {/* Tab 4: إعدادات المنظومة                                    */}
      {/* ======================================================== */}
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
                <option value="Reviewer">مراجع ومدقق (Reviewer)</option>
                <option value="Manager">مدير تشغيل (Manager)</option>
                <option value="Admin">مدير نظام (Admin)</option>
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">القسم الأساسي (Dept):</label>
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

      {/* Add Membership Dialog */}
      <Dialog
        open={addMembershipOpen}
        onClose={() => setAddMembershipOpen(false)}
        title="إضافة عضوية ورتبة مستخدم في قسم"
      >
        <form onSubmit={handleCreateMembership} className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">المستخدم:</label>
            <input
              type="text"
              id="input-mem-userid"
              placeholder="اسم المستخدم (مثل syn-accountant)"
              value={memUserId}
              onChange={(e) => setMemUserId(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">القسم:</label>
              <select
                id="select-mem-dept"
                value={memDept}
                onChange={(e) => setMemDept(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              >
                <option value="FIN">المالية والحسابات (FIN)</option>
                <option value="OPS">التشغيل الميداني (OPS)</option>
                <option value="HR">الموارد البشرية (HR)</option>
                <option value="FLT">الأسطول والمركبات (FLT)</option>
                <option value="MGT">الإدارة العامة (MGT)</option>
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">الرتبة:</label>
              <select
                id="select-mem-rank"
                value={memRank}
                onChange={(e) => setMemRank(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
              >
                <option value="A+">رتبة A+ (قيادي)</option>
                <option value="A">رتبة A (أول)</option>
                <option value="B">رتبة B (متوسط)</option>
                <option value="C">رتبة C (تشغيلي)</option>
              </select>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">تاريخ بداية السريان:</label>
            <input
              type="date"
              id="input-mem-validfrom"
              value={memValidFrom}
              onChange={(e) => setMemValidFrom(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setAddMembershipOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              id="btn-submit-membership"
              disabled={submittingMem}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {submittingMem ? 'جاري الحفظ...' : 'تأكيد العضوية'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Add Delegation Dialog */}
      <Dialog
        open={addDelegationOpen}
        onClose={() => setAddDelegationOpen(false)}
        title="إنشاء تفويض إداري مؤقت"
      >
        <form onSubmit={handleCreateDelegation} className="space-y-4 pt-2">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">المفوض (من):</label>
              <input
                type="text"
                placeholder="اسم المستخدم المفوض"
                value={delFromUser}
                onChange={(e) => setDelFromUser(e.target.value)}
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">المفوض إليه (إلى):</label>
              <input
                type="text"
                placeholder="اسم المستخدم المستلم"
                value={delToUser}
                onChange={(e) => setDelToUser(e.target.value)}
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">العملية المفوضة:</label>
            <input
              type="text"
              placeholder="e.g. advance.review"
              value={delOperation}
              onChange={(e) => setDelOperation(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">سارٍ من:</label>
              <input
                type="date"
                value={delValidFrom}
                onChange={(e) => setDelValidFrom(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">حتى تاريخ (إلزامي):</label>
              <input
                type="date"
                value={delUntil}
                onChange={(e) => setDelUntil(e.target.value)}
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">سبب التفويض:</label>
            <input
              type="text"
              placeholder="مثال: إجازة سنوية للمحاسب المسؤول"
              value={delReason}
              onChange={(e) => setDelReason(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setAddDelegationOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submittingDel}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {submittingDel ? 'جاري الحفظ...' : 'حفظ التفويض'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Add Supervision Dialog */}
      <Dialog
        open={addSupervisionOpen}
        onClose={() => setAddSupervisionOpen(false)}
        title="تعيين إشراف المشرف على مندوب"
      >
        <form onSubmit={handleCreateSupervision} className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">المشرف المسؤول:</label>
            <input
              type="text"
              id="input-sup-supervisor"
              placeholder="اسم المستخدم للمشرف (مثل syn-supervisor)"
              value={supSupervisor}
              onChange={(e) => setSupSupervisor(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">رقم إقامة المندوب (10 أرقام):</label>
            <input
              type="text"
              id="input-sup-iqama"
              placeholder="مثال: 2000000611"
              value={supIqama}
              onChange={(e) => setSupIqama(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">تاريخ بداية الإشراف:</label>
            <input
              type="date"
              id="input-sup-validfrom"
              value={supValidFrom}
              onChange={(e) => setSupValidFrom(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setAddSupervisionOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              id="btn-submit-supervision"
              disabled={submittingSup}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {submittingSup ? 'جاري الحفظ...' : 'تثبيت الإشراف'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Add Routing Dialog */}
      <Dialog
        open={addRoutingOpen}
        onClose={() => setAddRoutingOpen(false)}
        title="تحديث جدول التوجيه وتكليف المسؤولين"
      >
        <form onSubmit={handleCreateRouting} className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">العملية والوظيفة الموجهة:</label>
            <div className="flex gap-2">
              <input
                type="text"
                id="input-route-operation"
                placeholder="e.g. advance.review or advance.payout:BANK.BW"
                value={routeOperation}
                onChange={(e) => setRouteOperation(e.target.value)}
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
            <div className="flex items-center gap-1.5 flex-wrap pt-1">
              <span className="text-[10px] text-[var(--muted)]">نماذج سريعة:</span>
              <button
                type="button"
                onClick={() => setRouteOperation('advance.review')}
                className="px-2 py-0.5 rounded-md border text-[10px] hover:bg-[var(--raised)]"
              >
                advance.review
              </button>
              <button
                type="button"
                onClick={() => setRouteOperation('advance.payout:MAIN.BW.DEBT')}
                className="px-2 py-0.5 rounded-md border text-[10px] hover:bg-[var(--raised)]"
              >
                advance.payout:MAIN.BW.DEBT
              </button>
              <button
                type="button"
                onClick={() => setRouteOperation('advance.payout:BANK.BW')}
                className="px-2 py-0.5 rounded-md border text-[10px] hover:bg-[var(--raised)]"
              >
                advance.payout:BANK.BW
              </button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">القسم المختص:</label>
              <select
                id="select-route-dept"
                value={routeDept}
                onChange={(e) => setRouteDept(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              >
                <option value="FIN">المالية والحسابات (FIN)</option>
                <option value="OPS">التشغيل الميداني (OPS)</option>
                <option value="HR">الموارد البشرية (HR)</option>
                <option value="MGT">الإدارة العامة (MGT)</option>
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">الحد الأدنى للرتبة المطلوبة:</label>
              <select
                id="select-route-minrank"
                value={routeMinRank}
                onChange={(e) => setRouteMinRank(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
              >
                <option value="A+">رتبة A+ (قيادي فقط)</option>
                <option value="A">رتبة A أو أعلى</option>
                <option value="B">رتبة B أو أعلى</option>
                <option value="C">رتبة C أو أعلى (الكل)</option>
              </select>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">
              المستخدمون المكلفون (مفصولين بفواصل):
            </label>
            <input
              type="text"
              id="input-route-users"
              placeholder="e.g. syn-accountant, ahmed.ali"
              value={routeUsers}
              onChange={(e) => setRouteUsers(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
            <span className="text-[10px] text-[var(--muted)]">
              ملاحظة: يشترط أن يمتلك المستخدم عضوية نشطة في القسم المحدد برتبة لا تقل عن الحد الأدنى.
            </span>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">سارٍ من:</label>
            <input
              type="date"
              id="input-route-validfrom"
              value={routeValidFrom}
              onChange={(e) => setRouteValidFrom(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setAddRoutingOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              id="btn-submit-routing"
              disabled={submittingRoute}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {submittingRoute ? 'جاري الحفظ...' : 'تثبيت التوجيه'}
            </button>
          </div>
        </form>
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
