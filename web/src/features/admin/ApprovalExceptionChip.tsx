'use client';

import { useEffect, useState } from 'react';
import type { paths } from '../../lib/api-contract';
import { useAuth } from '../../lib/auth-context';

type Me = paths['/api/me']['get']['responses'][200]['content']['application/json'] & {
  approvalBypassUntil?: string;
  approvalBypassBadge?: boolean;
  allowedActions?: string[];
};

export function ApprovalExceptionChip({ collapsed }: { collapsed: boolean }) {
  const { user } = useAuth();
  const [until, setUntil] = useState('');
  const [bypass, setBypass] = useState(false);
  useEffect(() => {
    if (!user?.username) return;
    const controller = new AbortController();
    async function refresh() {
      try {
        const response = await fetch('/api/me', { credentials: 'same-origin', signal: controller.signal });
        if (!response.ok) { setUntil(''); setBypass(false); return; }
        const me: Me = await response.json();
        const actions = me.allowedActions ?? [];
        if (actions.includes('approval_bypass') && me.approvalBypassUntil) {
          setBypass(true); setUntil(me.approvalBypassUntil); return;
        }
        setBypass(false);
        setUntil(actions.includes('self_approve_exception') ? me.approvalExceptionUntil ?? '' : '');
      } catch { if (!controller.signal.aborted) { setUntil(''); setBypass(false); } }
    }
    void refresh();
    const timer = setInterval(() => { void refresh(); }, 60_000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [user?.username]);
  if (!until || Date.now() > Date.parse(`${until}T23:59:59+03:00`)) return null;
  const label = bypass ? `تجاوز اعتماد مفعّل حتى ${until}` : `استثناء اعتماد مفعّل حتى ${until}`;
  return <div role="status" title={label} className="m-2 rounded-lg border border-red-500 bg-red-50 p-2 text-xs font-bold text-red-800 dark:bg-red-950 dark:text-red-200">{collapsed ? '!' : label}</div>;
}
