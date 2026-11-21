'use client';

import React, { useCallback, useState, useEffect, useMemo, Suspense } from 'react';
import Link from 'next/link';
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
  ShieldAlert,
} from 'lucide-react';
import { api } from '../../lib/api';
import {
  auditActionLabelAr,
  auditActorLabelAr,
  formatAuditActorDisplay,
  formatAuditAt,
  shortAuditId,
} from '../../lib/audit-labels';
import { orgOperationLabelAr } from '../../lib/payroll-labels';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { ConfigListsPanel } from '../../features/admin/ConfigListsPanel';
import { ApprovalBypassPanel } from '../../features/admin/ApprovalBypassPanel';
import { RolePermissionsEditor, UserAccessDialog, type AccessCatalog, type AccessUser } from '../../features/admin/UserAccessDialog';
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

  const [accessCatalog, setAccessCatalog] = useState<AccessCatalog | null>(null);
  const [editingUser, setEditingUser] = useState<AccessUser | null>(null);
  const loadAccessCatalog = useCallback(() => {
    api.getRolesCatalog().then(setAccessCatalog).catch(() => setAccessCatalog(null));
  }, []);
  useEffect(() => { loadAccessCatalog(); }, [loadAccessCatalog]);

  // Add User Dialog State
  const [addUserOpen, setAddUserOpen] = useState<boolean>(false);
  const [newUsername, setNewUsername] = useState<string>('');
  const [newPassword, setNewPassword] = useState<string>('');
  const [newRole, setNewRole] = useState<string>('Accountant');
  const [newDept, setNewDept] = useState<string>('FIN');
  const [creatingUser, setCreatingUser] = useState<boolean>(false);

  // Audit State (server-paged; never load the whole table)
  const [auditLogs, setAuditLogs] = useState<any[]>([]);
  const [loadingAudit, setLoadingAudit] = useState<boolean>(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditSearch, setAuditSearch] = useState<string>('');
  const [auditSearchDebounced, setAuditSearchDebounced] = useState<string>('');
  const [auditActionFilter, setAuditActionFilter] = useState<string>('all');
  const [auditPage, setAuditPage] = useState(1);
  const [auditPageSize] = useState(50);
  const [auditTotal, setAuditTotal] = useState(0);

  // Firebase Config State
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
  const canViewAudit = can('audit.view') || isAdmin;

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

  // Load Audit (server page + filters; SQL-backed on the API)
  const loadAudit = useCallback(async (page = auditPage) => {
    setLoadingAudit(true);
    setAuditError(null);
    try {
      const res = await api.getAuditLogs({
        page,
        pageSize: auditPageSize,
        q: auditSearchDebounced.trim() || undefined,
        action: auditActionFilter,
      });
      setAuditLogs(Array.isArray(res?.items) ? res.items : []);
      setAuditTotal(typeof res?.total === 'number' ? res.total : 0);
      setAuditPage(typeof res?.page === 'number' ? res.page : page);
    } catch (err: any) {
      setAuditError(err.message || 'تعذر تحميل سجل التدقيق');
    } finally {
      setLoadingAudit(false);
    }
  }, [auditPage, auditPageSize, auditSearchDebounced, auditActionFilter]);

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
    if (canManageOrg) {
      void loadOrgData();
      void loadResponsible(queryOperation);
    }
    if (canViewAudit) {
      void loadAudit(1);
    }
  }, []);

  useEffect(() => {
    const t = setTimeout(() => setAuditSearchDebounced(auditSearch), 300);
    return () => clearTimeout(t);
  }, [auditSearch]);

  useEffect(() => {
    if (activeTab === 'audit') {
      void loadAudit(1);
    } else if (activeTab === 'org') {
      loadOrgData();
      loadResponsible(queryOperation);
    }
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'audit') return;
    void loadAudit(1);
  }, [auditSearchDebounced, auditActionFilter]);

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

  const auditTotalPages = Math.max(1, Math.ceil(auditTotal / auditPageSize));

  const usersFilterCols = useMemo(
    () => [
      { key: 'username', header: 'اسم المستخدم', type: 'text' as const, getValue: (u: any) => u.username || '' },
      { key: 'role', header: 'الدور الوظيفي', type: 'list' as const, getValue: (u: any) => u.roleNameAr || u.role || '' },
      { key: 'department', header: 'القسم', type: 'list' as const, getValue: (u: any) => u.department || 'ALL' },
      {
        key: 'customPermissions',
        header: 'الصلاحيات المخصصة',
        type: 'text' as const,
        getValue: (u: any) => (u.customPermissions?.length ? u.customPermissions.join(', ') : 'حسب الدور الأساسي'),
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (u: any) => (u.status === 'suspended' ? 'suspended' : 'active'),
        labelOf: (v: unknown) => (v === 'suspended' ? 'موقوف' : 'نشط'),
        toneOf: (v: unknown) => (v === 'suspended' ? 'danger' : 'success'),
      },
    ],
    []
  );
  const usersFilter = useTableAutoFilter(filteredUsers, usersFilterCols);

  const membershipsFilterCols = useMemo(
    () => [
      { key: 'userId', header: 'المستخدم', type: 'list' as const, getValue: (m: any) => m.userId || '' },
      { key: 'department', header: 'القسم', type: 'list' as const, getValue: (m: any) => m.department || '' },
      { key: 'rank', header: 'الرتبة', type: 'list' as const, getValue: (m: any) => m.rank || '' },
      { key: 'validFrom', header: 'سارٍ من', type: 'date' as const, getValue: (m: any) => m.validFrom?.split('T')[0] || '' },
      { key: 'validTo', header: 'سارٍ حتى', type: 'date' as const, getValue: (m: any) => m.validTo?.split('T')[0] || '' },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (m: any) => (m.validTo ? 'ended' : 'active'),
        labelOf: (v: unknown) => (v === 'ended' ? 'منتهية' : 'نشطة'),
        toneOf: (v: unknown) => (v === 'ended' ? 'neutral' : 'success'),
      },
    ],
    []
  );
  const membershipsFilter = useTableAutoFilter(memberships, membershipsFilterCols);

  const delegationsFilterCols = useMemo(
    () => [
      { key: 'fromUser', header: 'المفوض (من)', type: 'list' as const, getValue: (d: any) => d.fromUser || '' },
      { key: 'toUser', header: 'المفوض إليه (إلى)', type: 'list' as const, getValue: (d: any) => d.toUser || '' },
      { key: 'operation', header: 'العملية المفوضة', type: 'list' as const, getValue: (d: any) => d.operation || '' },
      { key: 'validFrom', header: 'سارٍ من', type: 'date' as const, getValue: (d: any) => d.validFrom?.split('T')[0] || '' },
      { key: 'until', header: 'حتى تاريخ', type: 'date' as const, getValue: (d: any) => d.until?.split('T')[0] || '' },
      { key: 'reason', header: 'السبب', type: 'text' as const, getValue: (d: any) => d.reason || '' },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (d: any) => (d.until && new Date(d.until).getTime() < Date.now() ? 'expired' : 'active'),
        labelOf: (v: unknown) => (v === 'expired' ? 'منتهٍ' : 'نشط'),
        toneOf: (v: unknown) => (v === 'expired' ? 'neutral' : 'success'),
      },
    ],
    []
  );
  const delegationsFilter = useTableAutoFilter(delegations, delegationsFilterCols);

  const supervisionsFilterCols = useMemo(
    () => [
      { key: 'supervisorUserId', header: 'المشرف المسؤول', type: 'list' as const, getValue: (s: any) => s.supervisorUserId || '' },
      { key: 'riderName', header: 'اسم المندوب', type: 'text' as const, getValue: (s: any) => s.riderName || 'مندوب مسجل' },
      { key: 'riderIqama', header: 'رقم الإقامة', type: 'text' as const, getValue: (s: any) => s.riderIqama || '' },
      { key: 'validFrom', header: 'سارٍ من', type: 'date' as const, getValue: (s: any) => s.validFrom?.split('T')[0] || '' },
      { key: 'validTo', header: 'سارٍ حتى', type: 'date' as const, getValue: (s: any) => s.validTo?.split('T')[0] || '' },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (s: any) => (s.validTo ? 'ended' : 'active'),
        labelOf: (v: unknown) => (v === 'ended' ? 'منتهٍ' : 'نشط'),
        toneOf: (v: unknown) => (v === 'ended' ? 'neutral' : 'success'),
      },
    ],
    []
  );
  const supervisionsFilter = useTableAutoFilter(supervisions, supervisionsFilterCols);

  const routingsFilterCols = useMemo(
    () => [
      { key: 'operation', header: 'العملية الموجهة', type: 'list' as const, getValue: (r: any) => r.operation || '' },
      { key: 'department', header: 'القسم', type: 'list' as const, getValue: (r: any) => r.department || '' },
      { key: 'minRank', header: 'الحد الأدنى للرتبة', type: 'list' as const, getValue: (r: any) => r.minRank || '' },
      { key: 'users', header: 'المستخدمون المكلفون', type: 'text' as const, getValue: (r: any) => (r.users || []).join(', ') },
      { key: 'validFrom', header: 'سارٍ من', type: 'date' as const, getValue: (r: any) => r.validFrom?.split('T')[0] || '' },
      { key: 'validTo', header: 'سارٍ حتى', type: 'date' as const, getValue: (r: any) => r.validTo?.split('T')[0] || '' },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (r: any) => (r.validTo ? 'ended' : 'active'),
        labelOf: (v: unknown) => (v === 'ended' ? 'منتهٍ' : 'نشط'),
        toneOf: (v: unknown) => (v === 'ended' ? 'neutral' : 'success'),
      },
    ],
    []
  );
  const routingsFilter = useTableAutoFilter(routings, routingsFilterCols);

  const auditFilterCols = useMemo(
    () => [
      { key: 'id', header: 'المعرف', type: 'text' as const, getValue: (l: any) => String(l.id ?? '') },
      {
        key: 'action',
        header: 'نوع العملية',
        type: 'list' as const,
        getValue: (l: any) => l.action || '',
        labelOf: (v: unknown) => auditActionLabelAr(String(v ?? '')),
      },
      {
        key: 'actor',
        header: 'المنفذ',
        type: 'list' as const,
        getValue: (l: any) => l.actor || 'system',
        labelOf: (v: unknown) => auditActorLabelAr(String(v ?? '')),
      },
      { key: 'at', header: 'التاريخ والوقت', type: 'date' as const, getValue: (l: any) => l.at || l.createdAt || '' },
    ],
    []
  );
  const auditFilter = useTableAutoFilter(auditLogs, auditFilterCols);

  const sectionTabs: SectionTabItem[] = [
    { id: 'users', label: 'المستخدمون والصلاحيات', icon: <Users size={16} /> },
    { id: 'org', label: 'الأقسام والرتب والتفويضات', icon: <Building2 size={16} /> },
    { id: 'lists', label: 'القوائم والإعدادات', icon: <SlidersHorizontal size={16} /> },
    { id: 'bypass', label: 'تجاوز الاعتماد', icon: <ShieldAlert size={16} /> },
    { id: 'audit', label: 'سجل التدقيق الشامل', icon: <History size={16} />, badge: auditTotal || undefined },
    { id: 'settings', label: 'إعدادات المنظومة', icon: <Settings size={16} /> },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الإدارة والتنظيم المؤسسي"
        eyebrow="التحكم والأمن ونظام الرتب"
        description="إدارة حسابات المستخدمين، سجل الأقسام والرتب والتفويضات، تعيين المشرفين وتوجيه مسؤولي مراجعة وصرف السلف، وفحص سجل التدقيق."
        primaryAction={
          activeTab === 'users'
            ? {
                label: 'إضافة مستخدم',
                icon: <UserPlus size={15} />,
                onClick: () => setAddUserOpen(true),
              }
            : activeTab === 'org'
              ? {
                  label: 'تحديث سجل التنظيم',
                  icon: <RefreshCw size={15} />,
                  onClick: loadOrgData,
                }
              : activeTab === 'audit'
                ? {
                    label: 'تحديث السجل',
                    icon: <RefreshCw size={15} />,
                    onClick: () => void loadAudit(auditPage),
                  }
              : activeTab === 'lists'
                ? undefined
                : undefined
        }
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
          hint="أعضاء مثبتون بالرتب التنظيمية"
          icon={Building2}
        />
        <StatCard
          label="المسؤولين المكلفين بالسلف"
          value={responsibleUsers.length}
          hint="مكلفون بمراجعة وصرف السلف"
          icon={UserCheck}
        />
        <StatCard
          label="عمليات سجل التدقيق"
          value={auditTotal}
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
                onClick={() => {
                  void (async () => {
                    try {
                      const headers = ['اسم المستخدم', 'الدور', 'القسم', 'الحالة'];
                      const rows = filteredUsers.map((u) => [
                        u.username,
                        u.roleNameAr || u.role,
                        u.department || '',
                        u.disabled ? 'معطل' : 'نشط',
                      ]);
                      const blob = await api.exportExcel({
                        headers,
                        rows,
                        numericColumns: [],
                        filename: 'admin-users.xlsx',
                      });
                      const url = URL.createObjectURL(blob);
                      const a = document.createElement('a');
                      a.href = url;
                      a.download = 'admin-users.xlsx';
                      a.click();
                      URL.revokeObjectURL(url);
                    } catch {
                      toast('تعذر تصدير المستخدمين', 'danger');
                    }
                  })();
                }}
                className="px-3.5 py-1.5 rounded-xl border border-[var(--line)] font-bold text-xs inline-flex items-center gap-1.5 hover:bg-[var(--raised)] transition cursor-pointer"
              >
                <Download size={14} />
                <span>تصدير إكسل</span>
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
              <AutoFilterBar controller={usersFilter} totalCount={filteredUsers.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={usersFilter} colKey="username" className="p-3 text-start" />
                      <AutoFilterTh controller={usersFilter} colKey="role" className="p-3 text-start" />
                      <AutoFilterTh controller={usersFilter} colKey="department" className="p-3 text-start" />
                      <AutoFilterTh controller={usersFilter} colKey="customPermissions" className="p-3 text-start" />
                      <AutoFilterTh controller={usersFilter} colKey="status" className="p-3 text-start" />
                      <th className="p-3 text-start">إجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {usersFilter.rows.map((u) => (
                      <tr key={u.id || u.username} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--ink)]">
                          <bdi dir="ltr">@{u.username}</bdi>
                        </td>
                        <td className="p-3 font-medium">{u.roleNameAr || u.role}</td>
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
                          <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${u.status === 'suspended' ? 'bg-[var(--danger)]/15 text-[var(--danger)]' : 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'}`}>
                            <CheckCircle2 size={12} />
                            <span>{u.status === 'suspended' ? 'موقوف' : 'نشط'}</span>
                          </span>
                        </td>
                        <td className="p-3">
                          <div className="flex gap-1.5">
                            <button type="button" data-user-edit={u.username} disabled={!accessCatalog} onClick={() => setEditingUser(u)} className="rounded-md border border-[var(--line)] px-2 py-1 text-xs font-bold disabled:opacity-50">تعديل</button>
                            <button
                              type="button"
                              onClick={async () => {
                                try {
                                  await api.updateUser(u.id, { status: u.status === 'suspended' ? 'active' : 'suspended' });
                                  loadUsers();
                                } catch (err: any) {
                                  toast(err.message || 'تعذر تغيير الحالة', 'danger');
                                }
                              }}
                              className="rounded-md border border-[var(--line)] px-2 py-1 text-xs font-bold"
                            >
                              {u.status === 'suspended' ? 'تفعيل' : 'إيقاف'}
                            </button>
                          </div>
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

      {activeTab === 'users' && accessCatalog && isAdmin && (
        <RolePermissionsEditor catalog={accessCatalog} onSaved={loadAccessCatalog} />
      )}
      {editingUser && accessCatalog && (
        <UserAccessDialog
          user={editingUser}
          users={users}
          catalog={accessCatalog}
          isAdmin={isAdmin}
          onClose={() => setEditingUser(null)}
          onSaved={() => { setEditingUser(null); loadUsers(); }}
        />
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
                  <AutoFilterBar controller={membershipsFilter} totalCount={memberships.length} />
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <AutoFilterTh controller={membershipsFilter} colKey="userId" className="p-3 text-start" />
                          <AutoFilterTh controller={membershipsFilter} colKey="department" className="p-3 text-start" />
                          <AutoFilterTh controller={membershipsFilter} colKey="rank" className="p-3 text-start" />
                          <AutoFilterTh controller={membershipsFilter} colKey="validFrom" className="p-3 text-start" />
                          <AutoFilterTh controller={membershipsFilter} colKey="validTo" className="p-3 text-start" />
                          <AutoFilterTh controller={membershipsFilter} colKey="status" className="p-3 text-start" />
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {membershipsFilter.rows.map((m) => {
                          const isEnded = Boolean(m.validTo);
                          return (
                            <tr
                              key={m.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">
                                <bdi dir="ltr">@{m.userId}</bdi>
                              </td>
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
                  <AutoFilterBar controller={delegationsFilter} totalCount={delegations.length} />
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <AutoFilterTh controller={delegationsFilter} colKey="fromUser" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="toUser" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="operation" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="validFrom" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="until" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="reason" className="p-3 text-start" />
                          <AutoFilterTh controller={delegationsFilter} colKey="status" className="p-3 text-start" />
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {delegationsFilter.rows.map((d) => {
                          const isExpired = d.until && new Date(d.until).getTime() < Date.now();
                          return (
                            <tr
                              key={d.id}
                              className={`transition ${
                                isExpired ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">
                                <bdi dir="ltr">@{d.fromUser}</bdi>
                              </td>
                              <td className="p-3 font-mono font-bold text-[var(--accent)]">
                                <bdi dir="ltr">@{d.toUser}</bdi>
                              </td>
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
                  <AutoFilterBar controller={supervisionsFilter} totalCount={supervisions.length} />
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <AutoFilterTh controller={supervisionsFilter} colKey="supervisorUserId" className="p-3 text-start" />
                          <AutoFilterTh controller={supervisionsFilter} colKey="riderName" className="p-3 text-start" />
                          <AutoFilterTh controller={supervisionsFilter} colKey="riderIqama" className="p-3 text-start" />
                          <AutoFilterTh controller={supervisionsFilter} colKey="validFrom" className="p-3 text-start" />
                          <AutoFilterTh controller={supervisionsFilter} colKey="validTo" className="p-3 text-start" />
                          <AutoFilterTh controller={supervisionsFilter} colKey="status" className="p-3 text-start" />
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {supervisionsFilter.rows.map((s) => {
                          const isEnded = Boolean(s.validTo);
                          return (
                            <tr
                              key={s.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-mono font-bold">
                                <bdi dir="ltr">@{s.supervisorUserId}</bdi>
                              </td>
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
                    جداول التوجيه وتكليف المسؤولين بالعمليات
                  </h4>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">
                    توجيه مهام مراجعة السلف وصرف الخزن والبنوك حسب الخزينة المحددة، للمستخدمين المؤهلين رتبوياً.
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
                    <SearchableSelect
                      id="admin-responsible-op"
                      value={queryOperation}
                      onChange={(v) => {
                        setQueryOperation(v);
                        loadResponsible(v);
                      }}
                      ariaLabel="عملية التوجيه"
                      placeholder="اختر العملية..."
                      size="sm"
                      className="w-72"
                      options={[
                        { value: 'advance.review', label: 'مراجعة السلف' },
                        { value: 'advance.payout:MAIN.BW.DEBT', label: 'صرف كفيل البوابة نقدية (MAIN.BW.DEBT)' },
                        { value: 'advance.payout:BANK.BW', label: 'صرف كفيل البوابة بنك (BANK.BW)' },
                      ]}
                    />
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
                        <bdi dir="ltr">@{ru.userId}</bdi>
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
                  <AutoFilterBar controller={routingsFilter} totalCount={routings.length} />
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <AutoFilterTh controller={routingsFilter} colKey="operation" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="department" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="minRank" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="users" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="validFrom" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="validTo" className="p-3 text-start" />
                          <AutoFilterTh controller={routingsFilter} colKey="status" className="p-3 text-start" />
                          <th className="p-3 text-start">الإجراءات</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {routingsFilter.rows.map((r) => {
                          const isEnded = Boolean(r.validTo);
                          return (
                            <tr
                              key={r.id}
                              className={`transition ${
                                isEnded ? 'opacity-60 bg-[var(--raised)]/30 text-[var(--muted)]' : 'hover:bg-[var(--row-hover)]'
                              }`}
                            >
                              <td className="p-3 font-bold text-[var(--accent)]" title={r.operation}>
                                {orgOperationLabelAr(r.operation)}
                              </td>
                              <td className="p-3 font-mono text-xs">{r.department}</td>
                              <td className="p-3 font-bold">{r.minRank}</td>
                              <td className="p-3">
                                <div className="flex items-center gap-1 flex-wrap">
                                  {r.users?.map((u, i) => (
                                    <span key={i} className="px-2 py-0.5 rounded-md bg-[var(--raised)] text-xs font-mono">
                                      <bdi dir="ltr">@{u}</bdi>
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
      {/* Tab: القوائم والإعدادات (BR §27)                           */}
      {/* ======================================================== */}
      {activeTab === 'lists' && (
        <ConfigListsPanel />
      )}

      {activeTab === 'bypass' && (
        <ApprovalBypassPanel />
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
              <SearchableSelect
                id="admin-audit-filter"
                value={auditActionFilter}
                onChange={setAuditActionFilter}
                ariaLabel="تصنيف التدقيق"
                placeholder="كل التصنيفات"
                size="sm"
                className="w-52"
                options={[
                  { value: 'all', label: 'كل التصنيفات' },
                  { value: 'advance', label: 'السلف والرتب' },
                  { value: 'platform.hs', label: 'فواتير وتسويات هنقرستيشن' },
                  { value: 'finance', label: 'الخزينة والمالية' },
                  { value: 'rider', label: 'المناديب والدليل' },
                  { value: 'admin', label: 'إدارة المستخدمين' },
                ]}
              />
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => {
                  void (async () => {
                    try {
                      const headers = ['المعرف', 'نوع العملية', 'المنفذ', 'التاريخ والوقت'];
                      const rows = auditFilter.rows.map((log, idx) => [
                        log.id || idx + 1,
                        auditActionLabelAr(log.action),
                        auditActorLabelAr(log.actor || 'system'),
                        formatAuditAt(log.at || log.createdAt),
                      ]);
                      const blob = await api.exportExcel({
                        headers,
                        rows,
                        numericColumns: [],
                        filename: 'admin-audit.xlsx',
                      });
                      const url = URL.createObjectURL(blob);
                      const a = document.createElement('a');
                      a.href = url;
                      a.download = 'admin-audit.xlsx';
                      a.click();
                      URL.revokeObjectURL(url);
                    } catch {
                      toast('تعذر تصدير سجل التدقيق', 'danger');
                    }
                  })();
                }}
                className="px-3.5 py-1.5 rounded-xl border border-[var(--line)] font-bold text-xs inline-flex items-center gap-1.5 hover:bg-[var(--raised)] transition cursor-pointer"
              >
                <Download size={14} />
                <span>تصدير إكسل</span>
              </button>
              <span className="text-xs text-[var(--muted)]">
                {auditTotal} عملية مسجلة
              </span>
            </div>
          </div>

          {auditError ? (
            <ErrorState title="تعذر تحميل سجل التدقيق" message={auditError} onRetry={() => void loadAudit(auditPage)} />
          ) : loadingAudit ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : auditLogs.length === 0 ? (
            <EmptyState
              title="لا توجد عمليات تدقيق مطابقة"
              description="سجل العمليات يسجل تلقائياً كافة إجراءات الإنشاء والاعتماد والتعديل."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={auditFilter} totalCount={auditLogs.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={auditFilter} colKey="id" className="p-3 text-start" />
                      <AutoFilterTh controller={auditFilter} colKey="action" className="p-3 text-start" />
                      <AutoFilterTh controller={auditFilter} colKey="actor" className="p-3 text-start" />
                      <AutoFilterTh controller={auditFilter} colKey="at" className="p-3 text-start" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {auditFilter.rows.map((log, idx) => (
                      <tr key={log.id || idx} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs text-[var(--muted)]" title={String(log.id || '')}>
                          <span className="sm:hidden">{shortAuditId(log.id)}</span>
                          <span className="hidden sm:inline">{log.id || idx + 1}</span>
                        </td>
                        <td className="p-3">
                          <span
                            className="text-xs font-bold text-[var(--accent)] bg-[var(--accent)]/10 px-2 py-0.5 rounded-md"
                            title={String(log.action || '')}
                          >
                            {auditActionLabelAr(log.action)}
                          </span>
                        </td>
                        <td className="p-3 font-semibold text-[var(--ink)]">
                          <bdi dir="ltr">{formatAuditActorDisplay(log.actor || 'system')}</bdi>
                        </td>
                        <td className="p-3 num text-xs text-[var(--muted)] whitespace-nowrap">
                          {formatAuditAt(log.at || log.createdAt)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2 border-t border-[var(--line)] px-3 py-2 text-xs text-[var(--muted)]">
                <span>
                  صفحة {auditPage} من {auditTotalPages}
                </span>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    data-testid="audit-page-prev"
                    disabled={auditPage <= 1 || loadingAudit}
                    onClick={() => void loadAudit(auditPage - 1)}
                    className="px-2.5 py-1 rounded-lg border border-[var(--line)] disabled:opacity-40"
                  >
                    السابق
                  </button>
                  <button
                    type="button"
                    data-testid="audit-page-next"
                    disabled={auditPage >= auditTotalPages || loadingAudit}
                    onClick={() => void loadAudit(auditPage + 1)}
                    className="px-2.5 py-1 rounded-lg border border-[var(--line)] disabled:opacity-40"
                  >
                    التالي
                  </button>
                </div>
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
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              إعدادات المنظومة
            </h3>
            <p className="text-xs text-[var(--muted)]">
              إعدادات إشعارات تطبيق المناديب تُدار من شاشة «إعدادات التطبيق» داخل قسم تطبيق المناديب والنماذج — وليست نموذجًا وهميًا هنا.
            </p>
            <Link
              href="/rider-app/?tab=settings"
              className="inline-flex items-center gap-2 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
            >
              فتح إعدادات التطبيق
            </Link>
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
            <label className="text-xs font-bold text-[var(--ink)]">اسم المستخدم:</label>
            <input
              type="text"
              placeholder="مثال: ahmed.ali"
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
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
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
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
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
            <SearchableSelect
              id="input-mem-userid"
              value={memUserId}
              onChange={setMemUserId}
              ariaLabel="المستخدم"
              placeholder="ابحث باسم المستخدم..."
              required
              options={(users as Array<{ username: string; displayNameAr?: string }>).map((u) => ({
                value: u.username,
                label: u.displayNameAr ? `${u.displayNameAr} (@${u.username})` : u.username,
              }))}
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">القسم:</label>
              <select
                id="select-mem-dept"
                value={memDept}
                onChange={(e) => setMemDept(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
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
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-bold focus:outline-hidden"
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
              <SearchableSelect
                value={delFromUser}
                onChange={setDelFromUser}
                ariaLabel="المفوض"
                placeholder="اختر المستخدم المفوض..."
                required
                options={(users as Array<{ username: string; displayNameAr?: string }>).map((u) => ({
                  value: u.username,
                  label: u.displayNameAr ? `${u.displayNameAr} (@${u.username})` : u.username,
                }))}
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">المفوض إليه (إلى):</label>
              <SearchableSelect
                value={delToUser}
                onChange={setDelToUser}
                ariaLabel="المفوض إليه"
                placeholder="اختر المستخدم المستلم..."
                required
                options={(users as Array<{ username: string; displayNameAr?: string }>).map((u) => ({
                  value: u.username,
                  label: u.displayNameAr ? `${u.displayNameAr} (@${u.username})` : u.username,
                }))}
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">العملية المفوضة:</label>
            <input
              type="text"
              placeholder="مثال: مراجعة السلف"
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
            <SearchableSelect
              id="input-sup-supervisor"
              value={supSupervisor}
              onChange={setSupSupervisor}
              ariaLabel="المشرف المسؤول"
              placeholder="اختر المشرف المسؤول..."
              required
              options={(users as Array<{ username: string; displayNameAr?: string }>).map((u) => ({
                value: u.username,
                label: u.displayNameAr ? `${u.displayNameAr} (@${u.username})` : u.username,
              }))}
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">المندوب (البحث بالاسم أو رقم الإقامة):</label>
            <RiderPicker
              value={supIqama}
              onChange={(r) => setSupIqama(r?.iqama || '')}
              placeholder="ابحث باسم المندوب أو رقم الإقامة..."
              required
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
                placeholder="رمز العملية الداخلي (يُملأ من النماذج السريعة)"
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
                مراجعة السلف
              </button>
              <button
                type="button"
                onClick={() => setRouteOperation('advance.payout:MAIN.BW.DEBT')}
                className="px-2 py-0.5 rounded-md border text-[10px] hover:bg-[var(--raised)]"
              >
                صرف نقدي كفيل البوابة
              </button>
              <button
                type="button"
                onClick={() => setRouteOperation('advance.payout:BANK.BW')}
                className="px-2 py-0.5 rounded-md border text-[10px] hover:bg-[var(--raised)]"
              >
                صرف بنكي كفيل البوابة
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
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
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
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-bold focus:outline-hidden"
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
              placeholder="أسماء المستخدمين مفصولة بفواصل"
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
