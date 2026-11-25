'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw, Send } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';

type ChatChannel = {
  id?: string;
  code?: string;
  nameAr?: string;
  companyId?: string;
  allowedActions?: string[];
};

type ChatMessage = {
  id?: string;
  bodyAr?: string;
  body?: string;
  createdBy?: string;
  createdAt?: string;
};

export function StaffChatPanel() {
  const { toast } = useToast();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [channels, setChannels] = useState<ChatChannel[]>([]);
  const [listAllowed, setListAllowed] = useState<string[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [newCode, setNewCode] = useState('');
  const [newName, setNewName] = useState('');

  const loadChannels = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.listStaffChatChannels();
      const items = Array.isArray(res.items) ? (res.items as ChatChannel[]) : [];
      setChannels(items);
      setListAllowed(Array.isArray(res.allowedActions) ? (res.allowedActions as string[]) : []);
      if (!selectedId && items[0]?.id) setSelectedId(String(items[0].id));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل القنوات');
    } finally {
      setLoading(false);
    }
  }, [selectedId]);

  const loadMessages = useCallback(async (channelId: string) => {
    if (!channelId) {
      setMessages([]);
      return;
    }
    try {
      const res = await api.listStaffChatMessages(channelId);
      if (Array.isArray(res)) setMessages(res as ChatMessage[]);
      else setMessages(Array.isArray(res.items) ? (res.items as ChatMessage[]) : []);
    } catch {
      setMessages([]);
    }
  }, []);

  useEffect(() => {
    void loadChannels();
  }, [loadChannels]);

  useEffect(() => {
    void loadMessages(selectedId);
  }, [selectedId, loadMessages]);

  const createChannel = async () => {
    if (!listAllowed.includes('create_channel')) {
      toast('إنشاء القناة يحتاج صلاحية إدارة المحادثة', 'warning');
      return;
    }
    setBusy(true);
    try {
      const created = await api.createStaffChatChannel({ code: newCode.trim(), nameAr: newName.trim() });
      toast('تم إنشاء القناة', 'success');
      setNewCode('');
      setNewName('');
      await loadChannels();
      if (created.id) setSelectedId(String(created.id));
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء القناة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const postMessage = async () => {
    if (!selectedId || !body.trim()) return;
    setBusy(true);
    try {
      await api.postStaffChatMessage(selectedId, { bodyAr: body.trim() });
      setBody('');
      await loadMessages(selectedId);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر إرسال الرسالة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <Skeleton className="h-40 w-full" />;
  if (error) return <ErrorState title="محادثة الموظفين" message={error} onRetry={loadChannels} />;

  const selected = channels.find((c) => c.id === selectedId);
  const canPost = selected?.allowedActions?.includes('post_message');

  return (
    <div className="space-y-4" data-testid="staff-chat-panel">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <div className="flex items-center justify-between gap-2">
          <h3 className="text-sm font-bold">محادثة الموظفين</h3>
          <button type="button" onClick={() => void loadChannels()} className="inline-flex items-center gap-1 text-xs font-semibold">
            <RefreshCw size={14} /> تحديث
          </button>
        </div>
        <p className="text-xs text-[var(--muted)]">المحادثة لا تنشئ إشعارات. الفيديو غير مسموح.</p>

        {listAllowed.includes('create_channel') && (
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
            <input
              placeholder="رمز القناة"
              value={newCode}
              onChange={(e) => setNewCode(e.target.value)}
              className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
            />
            <input
              placeholder="اسم القناة"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
            />
            <button
              type="button"
              disabled={busy || !newCode.trim() || !newName.trim()}
              onClick={() => void createChannel()}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
            >
              إنشاء قناة
            </button>
          </div>
        )}

        {channels.length === 0 ? (
          <EmptyState title="لا قنوات" description="أنشئ قناة أو اطلب عضوية من المسؤول." />
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-[220px_1fr] gap-3">
            <ul className="space-y-1">
              {channels.map((ch) => (
                <li key={String(ch.id)}>
                  <button
                    type="button"
                    onClick={() => setSelectedId(String(ch.id))}
                    className={`w-full text-start px-3 py-2 rounded-xl text-xs font-semibold border ${
                      selectedId === ch.id
                        ? 'border-[var(--accent)] bg-[var(--accent)]/10'
                        : 'border-[var(--line)] bg-[var(--raised)]'
                    }`}
                  >
                    {ch.nameAr || ch.code}
                  </button>
                </li>
              ))}
            </ul>
            <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3 space-y-3 min-h-[220px]">
              <div className="space-y-2 max-h-64 overflow-auto">
                {messages.length === 0 ? (
                  <p className="text-xs text-[var(--muted)]">لا رسائل بعد.</p>
                ) : (
                  messages.map((m) => (
                    <div key={String(m.id)} className="text-xs border-b border-[var(--line)] pb-2">
                      <div className="font-bold">{m.createdBy || '—'}</div>
                      <div>{m.bodyAr || m.body || ''}</div>
                      <div className="text-[var(--muted)] font-mono">{m.createdAt || ''}</div>
                    </div>
                  ))
                )}
              </div>
              {canPost ? (
                <div className="flex gap-2">
                  <input
                    value={body}
                    onChange={(e) => setBody(e.target.value)}
                    placeholder="اكتب رسالة…"
                    className="flex-1 p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
                  />
                  <button
                    type="button"
                    disabled={busy || !body.trim()}
                    onClick={() => void postMessage()}
                    className="inline-flex items-center gap-1 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
                  >
                    <Send size={14} /> إرسال
                  </button>
                </div>
              ) : (
                <p className="text-xs text-[var(--muted)]">لا صلاحية إرسال في هذه القناة.</p>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
