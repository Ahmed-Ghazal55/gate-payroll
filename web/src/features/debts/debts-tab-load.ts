/** Debts page tabs — each tab loads only its own endpoints (F-D2). */
export type DebtsTabId =
  | 'debts'
  | 'actions'
  | 'advance_requests'
  | 'statements'
  | 'approvals'
  | 'promissory';

export type DebtsLoadKind = 'debts' | 'approvals' | 'promissory' | 'advances' | 'none';

/**
 * Which API reads the open tab needs. `actions` and `statements` reuse debts
 * (or load on demand) and do not pull approvals / promissory up front.
 */
export function debtsLoadKindsForTab(tab: DebtsTabId): DebtsLoadKind[] {
  switch (tab) {
    case 'debts':
    case 'actions':
      return ['debts'];
    case 'approvals':
      return ['approvals'];
    case 'promissory':
      return ['promissory'];
    case 'advance_requests':
      return ['advances'];
    case 'statements':
      return ['none'];
    default:
      return ['none'];
  }
}

/** Default server page size for GET /debts?page=&pageSize= (max 200 on the API). */
export const DEBTS_PAGE_SIZE = 50;

export type DebtCategoryOption = { value: string; label: string };

/**
 * Category filter choices from the scoped finance catalog, plus the advance group tab.
 * Built from catalog data (not a fixed 10-item list / not from the current page rows).
 */
export function debtCategoryOptionsFromCatalog(
  categories: Array<{ code?: string | null; nameAr?: string | null }> | null | undefined
): DebtCategoryOption[] {
  const options: DebtCategoryOption[] = [
    { value: 'all', label: 'كافة الفئات' },
    { value: 'advance', label: 'سلف (نقدية / بنكية)' },
  ];
  const seen = new Set(['all', 'advance']);
  for (const cat of categories ?? []) {
    const code = (cat.code || '').trim();
    if (!code || seen.has(code)) continue;
    seen.add(code);
    options.push({ value: code, label: (cat.nameAr || '').trim() || code });
  }
  return options;
}
