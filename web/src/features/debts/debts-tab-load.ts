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

/** Default client page size while the debts API still returns a full array. */
export const DEBTS_CLIENT_PAGE_SIZE = 50;

/**
 * Slice a full list for display. Real server paging needs GET /debts?page=&pageSize=
 * (contract documents a paged shape; the live MapGet still returns the full list).
 */
export function pageSlice<T>(items: T[], page: number, pageSize = DEBTS_CLIENT_PAGE_SIZE): T[] {
  const p = Math.max(1, page);
  const start = (p - 1) * pageSize;
  return items.slice(start, start + pageSize);
}
