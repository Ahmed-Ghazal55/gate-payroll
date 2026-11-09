'use client';

import { useEffect, useState } from 'react';
import type { paths } from '../../lib/api-contract';
import { useAuth } from '../../lib/auth-context';

type Me = paths['/api/me']['get']['responses'][200]['content']['application/json'];

export function ApprovalExceptionChip({ collapsed }: { collapsed: boolean }) {
  const { user } = useAuth();
  const [until, setUntil] = useState('');
  useEffect(() => {
    if (user?.username.toLowerCase() !== 'ghazal') return;
    const controller = new AbortController();
    async function refresh() {
      try {
        const response = await fetch('/api/me', { credentials: 'same-origin', signal: controller.signal });
        if (!response.ok) { setUntil(''); return; }
        const me: Me = await response.json();
        setUntil(me.allowedActions.includes('self_approve_exception') ? me.approvalExceptionUntil : '');
      } catch { if (!controller.signal.aborted) setUntil(''); }
    }
    void refresh();
    const timer = setInterval(() => { void refresh(); }, 60_000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [user?.username]);
  if (user?.username.toLowerCase() !== 'ghazal' || !until || Date.now() > Date.parse(`${until}T23:59:59+03:00`)) return null;
  const label = `استثناء اعتماد مفعّل حتى ${until}`;
  return <div role="status" title={label} className="m-2 rounded-lg border border-red-500 bg-red-50 p-2 text-xs font-bold text-red-800 dark:bg-red-950 dark:text-red-200">{collapsed ? '!' : label}</div>;
}
