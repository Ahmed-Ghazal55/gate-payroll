'use client';

import React, { useState, useEffect, useRef } from 'react';
import Link from 'next/link';
import {
  Bell,
  Check,
  CheckCheck,
  Clock,
  ExternalLink,
  ShieldAlert,
  User,
  Settings,
  ClipboardCheck,
  X,
  Loader2,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { NotificationItem, NotificationCategory } from '../../lib/types';
import { inApp } from '@/lib/paths';

export interface NotificationsPanelProps {
  open: boolean;
  onClose: () => void;
  onUnreadCountChange?: (count: number) => void;
}

export function NotificationsPanel({
  open,
  onClose,
  onUnreadCountChange,
}: NotificationsPanelProps) {
  const [activeTab, setActiveTab] = useState<'all' | 'general' | 'personal' | 'tasks'>('all');
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [markingAll, setMarkingAll] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);

  const fetchNotifications = async () => {
    setLoading(true);
    try {
      const data = await api.getNotifications();
      setItems(data);
      const unread = data.filter((n) => n.unread).length;
      if (onUnreadCountChange) onUnreadCountChange(unread);
    } catch (err) {
      console.error('Failed to load notifications:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (open) {
      fetchNotifications();
    }
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function handleClickOutside(e: MouseEvent) {
      if (panelRef.current && !panelRef.current.parentElement?.contains(e.target as Node)) {
        onClose();
      }
    }
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open, onClose]);

  const handleMarkRead = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await api.markNotificationRead(id);
      setItems((prev) =>
        prev.map((item) => (item.id === id ? { ...item, unread: false } : item))
      );
      if (onUnreadCountChange) {
        onUnreadCountChange(Math.max(0, items.filter((n) => n.unread && n.id !== id).length));
      }
    } catch (err) {
      console.error('Failed to mark read:', err);
    }
  };

  const handleMarkAllRead = async () => {
    setMarkingAll(true);
    try {
      await api.markAllNotificationsRead();
      setItems((prev) => prev.map((item) => ({ ...item, unread: false })));
      if (onUnreadCountChange) onUnreadCountChange(0);
    } catch (err) {
      console.error('Failed to mark all read:', err);
    } finally {
      setMarkingAll(false);
    }
  };

  if (!open) return null;

  const tabs = [
    { id: 'all' as const, label: 'الكل' },
    { id: 'general' as const, label: 'عام' },
    { id: 'personal' as const, label: 'مخصص' },
    { id: 'tasks' as const, label: 'طلبات ومهام' },
  ];

  const displayedItems = items.filter((item) => {
    if (activeTab === 'all') return true;
    if (activeTab === 'general') return item.category === 'general';
    if (activeTab === 'personal') return item.category === 'personal';
    if (activeTab === 'tasks') return item.requiresAction || item.taskKind === 'advance_request';
    return true;
  });

  const unreadTotal = items.filter((item) => item.unread).length;

  const categoryIcon = (cat: NotificationCategory) => {
    switch (cat) {
      case 'approvals':
        return <ClipboardCheck size={16} className="text-[var(--money-pos)]" />;
      case 'system':
        return <Settings size={16} className="text-[var(--info)]" />;
      case 'personal':
        return <User size={16} className="text-[var(--accent)]" />;
      case 'general':
      default:
        return <Bell size={16} className="text-[var(--warning)]" />;
    }
  };

  return (
    <div
      ref={panelRef}
      role="dialog"
      aria-label="مركز الإشعارات والتنبيهات"
      className="absolute start-0 sm:start-auto end-0 top-full mt-2 w-full sm:w-[420px] rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-2xl z-50 overflow-hidden animate-in fade-in zoom-in-95 duration-150"
    >
      {/* Panel Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-[var(--line)] bg-[var(--surface)]">
        <div className="flex items-center gap-2">
          <Bell size={18} className="text-[var(--accent)]" />
          <h3 className="text-sm font-bold">الإشعارات</h3>
          {unreadTotal > 0 && (
            <span className="num text-[11px] font-bold px-2 py-0.5 rounded-full bg-[var(--accent)] text-[var(--accent-ink)]">
              {unreadTotal} جديد
            </span>
          )}
        </div>

        <div className="flex items-center gap-2">
          {unreadTotal > 0 && (
            <button
              type="button"
              disabled={markingAll}
              onClick={handleMarkAllRead}
              className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--accent)] hover:underline disabled:opacity-50"
            >
              <CheckCheck size={14} />
              <span>قراءة الكل</span>
            </button>
          )}
          <button
            type="button"
            onClick={onClose}
            className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]"
          >
            <X size={16} />
          </button>
        </div>
      </div>

      {/* Category Tabs */}
      <div className="flex items-center gap-1 px-3 pt-2 pb-1 border-b border-[var(--line)] bg-[var(--raised)]/40 overflow-x-auto no-scrollbar">
        {tabs.map((tab) => {
          const count = items.filter((n) => {
            if (tab.id === 'all') return n.unread;
            if (tab.id === 'general') return n.category === 'general' && n.unread;
            if (tab.id === 'personal') return n.category === 'personal' && n.unread;
            if (tab.id === 'tasks') return n.requiresAction && n.actionStatus === 'open';
            return false;
          }).length;
          const isActive = activeTab === tab.id;

          return (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id)}
              className={`inline-flex items-center gap-1 px-2.5 py-1 text-xs font-semibold rounded-lg transition whitespace-nowrap ${
                isActive
                  ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span>{tab.label}</span>
              {count > 0 && (
                <span className="num text-[10px] px-1.5 py-0.2 rounded-full bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
                  {count}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* Notifications List */}
      <div className="max-h-[380px] overflow-y-auto divide-y divide-[var(--line)] no-scrollbar">
        {loading ? (
          <div className="flex items-center justify-center py-12 text-[var(--muted)]">
            <Loader2 size={24} className="animate-spin text-[var(--accent)]" />
          </div>
        ) : displayedItems.length === 0 ? (
          <div className="py-12 text-center text-xs text-[var(--muted)]">
            لا توجد إشعارات ضمن هذا القسم
          </div>
        ) : (
          displayedItems.map((item) => (
            <div
              key={item.id}
              className={`p-3.5 transition flex items-start gap-3 ${
                item.unread
                  ? 'bg-[var(--row-selected)]'
                  : 'hover:bg-[var(--row-hover)]'
              }`}
            >
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--raised)] shrink-0 mt-0.5">
                {categoryIcon(item.category)}
              </div>

              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-1.5 flex-wrap min-w-0">
                    <h4 className="text-xs font-bold text-[var(--ink)] truncate">
                      {item.title}
                    </h4>
                    {/* Separate Read/Unread badge */}
                    {item.unread ? (
                      <span className="text-[9px] font-bold px-1.5 py-0.2 rounded-full bg-[var(--accent)]/15 text-[var(--accent)]">
                        غير مقروء
                      </span>
                    ) : (
                      <span className="text-[9px] font-medium px-1.5 py-0.2 rounded-full bg-[var(--raised)] text-[var(--muted)]">
                        مقروء
                      </span>
                    )}
                    {/* Separate Action/Decided badge */}
                    {item.requiresAction && (
                      item.actionStatus === 'decided' ? (
                        <span className="text-[9px] font-bold px-1.5 py-0.2 rounded-full bg-[var(--money-pos)]/15 text-[var(--money-pos)] inline-flex items-center gap-0.5">
                          <Check size={10} />
                          <span>تم البت</span>
                        </span>
                      ) : (
                        <span className="text-[9px] font-bold px-1.5 py-0.2 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] inline-flex items-center gap-0.5">
                          <Clock size={10} />
                          <span>بانتظار البت</span>
                        </span>
                      )
                    )}
                  </div>
                  <span className="num text-[10px] text-[var(--muted)] shrink-0">
                    {new Date(item.createdAt).toLocaleTimeString('ar-SA', {
                      hour: '2-digit',
                      minute: '2-digit',
                    })}
                  </span>
                </div>

                <p className="text-xs text-[var(--muted)] mt-1 line-clamp-2 leading-relaxed">
                  {item.message}
                </p>

                <div className="flex items-center justify-between gap-2 mt-2">
                  {item.link ? (
                    <Link
                      href={inApp(item.link)}
                      onClick={onClose}
                      className="inline-flex items-center gap-1 text-[11px] font-semibold text-[var(--accent)] hover:underline"
                    >
                      <span>{item.actionText || 'عرض التفاصيل'}</span>
                      <ExternalLink size={12} />
                    </Link>
                  ) : (
                    <span />
                  )}

                  {item.unread && (
                    <button
                      type="button"
                      onClick={(e) => handleMarkRead(item.id, e)}
                      title="تحديد كمقروء"
                      className="inline-flex items-center gap-1 text-[10px] font-medium text-[var(--muted)] hover:text-[var(--ink)] p-0.5 rounded"
                    >
                      <Check size={12} />
                      <span>كمقروء</span>
                    </button>
                  )}
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {/* Panel Footer */}
      <div className="p-2.5 border-t border-[var(--line)] bg-[var(--surface)] text-center">
        <Link
          href="/notifications/"
          onClick={onClose}
          className="inline-flex items-center justify-center w-full py-1.5 text-xs font-bold text-[var(--accent)] hover:bg-[var(--raised)] rounded-lg transition"
        >
          عرض كل الإشعارات
        </Link>
      </div>
    </div>
  );
}
