// Section 9 (R3, U-W3-16): one main section «تطبيق المناديب والنماذج» at /rider-app/.
// The 8 sidebar entries ARE its navigation (rule 3.0: the page shows no second bar of the same tabs).

export const RIDER_APP_TABS = [
  'campaigns',
  'responses',
  'builder',
  'accounts',
  'broadcasts',
  'statements',
  'requests',
  'settings',
] as const;

export type RiderAppTab = (typeof RIDER_APP_TABS)[number];

export const RIDER_APP_TAB_LABELS: Record<RiderAppTab, string> = {
  campaigns: 'الحملات والروابط',
  responses: 'مراجعة الردود',
  builder: 'مصمم النماذج',
  accounts: 'الحسابات والأجهزة',
  broadcasts: 'الإشعارات والإرسال',
  statements: 'إرسال كشوف الحساب',
  requests: 'الطلبات والمواعيد',
  settings: 'إعدادات التطبيق',
};

/** Inner sub-tabs per entry (`?subtab=`); allowed by rule 3.0 because they are not sidebar entries. */
export const RIDER_APP_SUBTABS: Partial<Record<RiderAppTab, readonly string[]>> = {
  campaigns: ['list', 'analytics'],
  broadcasts: ['broadcast', 'individual', 'history'],
  requests: ['all', 'annual_leave', 'appointments', 'other', 'advances'],
};

// Old ids from the separate forms page and the old rider-app page.
const LEGACY: Record<string, { tab: RiderAppTab; subtab?: string }> = {
  'statement-preview': { tab: 'statements' },
  statement: { tab: 'statements' },
  analytics: { tab: 'campaigns', subtab: 'analytics' },
};

export function isRiderAppTab(id: string | null | undefined): id is RiderAppTab {
  return !!id && (RIDER_APP_TABS as readonly string[]).includes(id);
}

/** Tab (and sub-tab) the page shows for a `?tab=` value; unknown → campaigns. */
export function resolveRiderAppTab(raw: string | null | undefined): { tab: RiderAppTab; subtab?: string; legacy: boolean } {
  if (isRiderAppTab(raw)) return { tab: raw, legacy: false };
  const old = raw ? LEGACY[raw] : undefined;
  if (old) return { ...old, legacy: true };
  return { tab: 'campaigns', legacy: !!raw };
}

/** Target of the old `/forms/` admin page (never the public `/forms/<token>` form link). */
export function formsRedirectTarget(search: string): string {
  const params = new URLSearchParams(search);
  const { tab, subtab } = resolveRiderAppTab(params.get('tab'));
  const out = new URLSearchParams();
  params.forEach((value, key) => {
    if (key !== 'tab' && key !== 'subtab') out.set(key, value);
  });
  out.set('tab', tab);
  const sub = subtab ?? params.get('subtab');
  if (sub) out.set('subtab', sub);
  return `/rider-app/?${out.toString()}`;
}
