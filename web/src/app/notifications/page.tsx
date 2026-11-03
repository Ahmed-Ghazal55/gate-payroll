'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import {
  Bell,
  Check,
  CheckCheck,
  ExternalLink,
  ClipboardCheck,
  Settings,
  User,
  Search,
  Filter,
  Trash2,
  Clock,
  RotateCcw,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { NotificationItem, NotificationCategory } from '../../lib/types';
import { PageHeader } from '../../components/ui/PageHeader';
import { Toolbar } from '../../components/ui/Toolbar';
import { Tabs } from '../../components/ui/Tabs';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { inApp } from '@/lib/paths';

export default function NotificationsPage() {
  const { toast } = useToast();
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [activeTab, setActiveTab] = useState<'all' | NotificationCategory>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [unreadOnly, setUnreadOnly] = useState(false);

  const fetchNotifications = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.getNotifications();
      setItems(data);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل الإشعارات');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchNotifications();
  }, []);

  const handleMarkRead = async (id: string) => {
    try {
      await api.markNotificationRead(id);
      setItems((prev) =>
        prev.map((item) => (item.id === id ? { ...item, unread: false } : item))
      );
      toast('تم تحديد الإشعار كمقروء', 'success');
    } catch (err: any) {
      toast(err?.message || 'تعذر تحديث حالة الإشعار', 'danger');
    }
  };

  const handleMarkAllRead = async () => {
    try {
      await api.markAllNotificationsRead();
      setItems((prev) => prev.map((item) => ({ ...item, unread: false })));
      toast('تم تحديد جميع الإشعارات كمقروءة', 'success');
    } catch (err: any) {
      toast(err?.message || 'تعذر تحديد الكل كمقروء', 'danger');
    }
  };

  const tabs = [
    { id: 'all', label: 'الكل' },
    { id: 'general', label: 'عامة' },
    { id: 'personal', label: 'مخصصة لي' },
    { id: 'system', label: 'النظام' },
    { id: 'approvals', label: 'الاعتمادات' },
  ];

  const filteredItems = items.filter((item) => {
    if (activeTab !== 'all' && item.category !== activeTab) return false;
    if (unreadOnly && !item.unread) return false;
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      return (
        item.title.toLowerCase().includes(q) ||
        item.message.toLowerCase().includes(q)
      );
    }
    return true;
  });

  const unreadTotal = items.filter((i) => i.unread).length;

  const categoryIcon = (cat: NotificationCategory) => {
    switch (cat) {
      case 'approvals':
        return <ClipboardCheck size={18} className="text-[var(--money-pos)]" />;
      case 'system':
        return <Settings size={18} className="text-[var(--info)]" />;
      case 'personal':
        return <User size={18} className="text-[var(--accent)]" />;
      case 'general':
      default:
        return <Bell size={18} className="text-[var(--warning)]" />;
    }
  };

  const categoryLabel = (cat: NotificationCategory) => {
    switch (cat) {
      case 'approvals':
        return 'اعتماد';
      case 'system':
        return 'النظام';
      case 'personal':
        return 'شخصي';
      case 'general':
      default:
        return 'عام';
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        title="مركز الإشعارات والتنبيهات"
        eyebrow="مساحة الإدارة 1.0"
        description="متابعة فورية لطلبات الاعتماد المالي، مراجعات الآيبان ونماذج المناديب، وتنبيهات جودة البيانات."
        primaryAction={
          unreadTotal > 0
            ? {
                label: 'تحديد الكل كمقروء',
                onClick: handleMarkAllRead,
                icon: <CheckCheck size={16} />,
              }
            : undefined
        }
      />

      {/* Tabs */}
      <Tabs
        tabs={tabs.map((t) => ({
          ...t,
          badge:
            items.filter((i) =>
              t.id === 'all' ? i.unread : i.category === t.id && i.unread
            ).length || undefined,
        }))}
        activeTab={activeTab}
        onChange={(id) => setActiveTab(id as any)}
      />

      {/* Toolbar filters */}
      <Toolbar
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        searchPlaceholder="بحث في عنوان الإشعار أو التفاصيل..."
        filterChips={[
          {
            id: 'unread',
            label: 'غير المقروءة فقط',
            active: unreadOnly,
            count: unreadTotal,
            onToggle: () => setUnreadOnly(!unreadOnly),
          },
        ]}
      />

      {/* Notifications Content */}
      {error ? (
        <ErrorState
          title="تعذر تحميل الإشعارات"
          message={error}
          onRetry={fetchNotifications}
        />
      ) : loading ? (
        <div className="space-y-3">
          <Skeleton className="h-20 rounded-xl" rows={4} />
        </div>
      ) : filteredItems.length === 0 ? (
        <EmptyState
          title="لا توجد إشعارات مطابقة"
          description={
            unreadOnly
              ? 'تمت قراءة جميع الإشعارات ضمن هذا القسم.'
              : 'لم تتلق أي إشعارات جديدة في هذا التبويب.'
          }
        />
      ) : (
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] divide-y divide-[var(--line)] overflow-hidden shadow-xs">
          {filteredItems.map((item) => (
            <div
              key={item.id}
              className={`p-4 sm:p-5 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 transition ${
                item.unread
                  ? 'bg-[var(--row-selected)]'
                  : 'hover:bg-[var(--row-hover)]'
              }`}
            >
              <div className="flex items-start gap-3.5 min-w-0">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--raised)] shrink-0 mt-0.5">
                  {categoryIcon(item.category)}
                </div>

                <div className="min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <h3 className="text-sm font-bold text-[var(--ink)]">
                      {item.title}
                    </h3>
                    <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-[var(--raised)] text-[var(--muted)]">
                      {categoryLabel(item.category)}
                    </span>
                    {item.unread && (
                      <span className="h-2 w-2 rounded-full bg-[var(--accent)]" />
                    )}
                  </div>

                  <p className="text-xs sm:text-sm text-[var(--muted)] mt-1 leading-relaxed">
                    {item.message}
                  </p>

                  <div className="flex items-center gap-2 mt-2 text-[11px] text-[var(--muted)]">
                    <Clock size={12} />
                    <span className="num">
                      {new Date(item.createdAt).toLocaleString('ar-SA')}
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 shrink-0 self-end sm:self-center">
                {item.link && (
                  <Link
                    href={inApp(item.link)}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 transition shadow-2xs"
                  >
                    <span>{item.actionText || 'عرض التفاصيل'}</span>
                    <ExternalLink size={13} />
                  </Link>
                )}

                {item.unread && (
                  <button
                    type="button"
                    onClick={() => handleMarkRead(item.id)}
                    className="inline-flex items-center gap-1 px-3 py-1.5 text-xs font-semibold rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] transition"
                  >
                    <Check size={14} />
                    <span>تحديد كمقروء</span>
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
