import { api } from '../../lib/api';
import type { CoaAccountOption } from './payment-labels';

export * from './payment-labels';

export async function loadPostableAccounts(): Promise<{
  accounts: CoaAccountOption[];
  canCreate: boolean;
}> {
  try {
    const res = (await api.getFinanceCoa()) as unknown as {
      items?: Array<Record<string, unknown>>;
      allowedActions?: string[];
    };
    const items = Array.isArray(res?.items) ? res.items : [];
    const accounts: CoaAccountOption[] = items
      .map((r) => ({
        id: String(r.id || ''),
        code: String(r.code || ''),
        nameAr: String(r.nameAr || r.code || ''),
        parentCode: r.parentCode ? String(r.parentCode) : undefined,
        isPostable: Boolean(r.isPostable),
        isActive: Boolean(r.isActive),
      }))
      .filter((a) => a.code && a.isActive && a.isPostable);
    const canCreate = Array.isArray(res?.allowedActions) && res.allowedActions.includes('create');
    return { accounts, canCreate };
  } catch {
    return { accounts: [], canCreate: false };
  }
}

export async function exportPaymentsSheet(
  headers: string[],
  rows: Array<Array<string | number>>,
  numericColumns: number[],
  filename: string
): Promise<void> {
  const blob = await api.exportExcel({ headers, rows, numericColumns, filename });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename.endsWith('.xlsx') ? filename : `${filename}.xlsx`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
