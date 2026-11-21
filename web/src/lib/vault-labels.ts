/** Chart groups: 1110–1120 cashboxes, 1130 banks, 1140 petro clearing (FinancialCore). */

export function isBankVaultCode(code: string | null | undefined): boolean {
  if (!code) return false;
  return code.startsWith('113') || code.startsWith('BANK.');
}

export function isCashVaultCode(code: string | null | undefined): boolean {
  if (!code) return false;
  return (
    code.startsWith('111') ||
    code.startsWith('112') ||
    code.startsWith('114') ||
    code.startsWith('MAIN.') ||
    code.startsWith('SUP.') ||
    code.startsWith('PETRO.')
  );
}

export function vaultGroupLabelAr(code: string | null | undefined): string {
  if (isBankVaultCode(code)) return 'حساب بنكي رسمي';
  if (isCashVaultCode(code)) return 'صندوق نقدية تشغيلي';
  return 'حساب خزينة';
}

const VAULT_KIND_AR: Record<string, string> = {
  None: 'تجميعي (غير ترحيلي)',
  MainInternal: 'الصندوق الداخلي الرئيسي',
  JahezCashbox: 'خزنة جاهز ميدانية',
  Bank: 'حساب بنكي رسمي',
  PetroApp: 'مقاصة وقود بترو آب',
  CashBox: 'صندوق نقدية',
  Custody: 'عهدة',
};

export function vaultKindLabelAr(kind: string | null | undefined): string {
  if (!kind || !String(kind).trim()) return 'غير محدد';
  return VAULT_KIND_AR[kind] || kind;
}
