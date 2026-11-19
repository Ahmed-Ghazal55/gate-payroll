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

/** Category filter choices for server-side debts list (not derived from the current page). */
export const DEBT_CATEGORY_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'all', label: 'كافة الفئات' },
  { value: 'advance', label: 'سلف (نقدية / بنكية)' },
  { value: 'CashAdvance', label: 'سلفة نقدية' },
  { value: 'BankAdvance', label: 'سلفة بنكية' },
  { value: 'TrafficFine', label: 'مخالفة مرورية' },
  { value: 'EquipmentLoan', label: 'عهدة ومعدات' },
  { value: 'WorkPermit', label: 'رخصة عمل' },
  { value: 'IqamaRenewal', label: 'تجديد إقامة' },
  { value: 'HousingDeduction', label: 'استقطاع سكن' },
  { value: 'PlatformTransferredDebt', label: 'مديونية منقولة من منصة' },
  { value: 'NegativeSalaryCarryover', label: 'ترحيل راتب سالب' },
];
