// Display names for Saudi bank codes (characters 5–6 of a Saudi IBAN).
// Same list as the public rider form (RiderForm/src/rider-form.ts BANKS) and the SWIFT map in
// Payroll.Api/BankPayrollFiles.cs. It is a label lookup only: the code itself comes from the API
// (`bankCode`) or from the IBAN, never from a default.
const BANKS: Record<string, { ar: string; swift: string }> = {
  '80': { ar: 'مصرف الراجحي', swift: 'RJHI' },
  '10': { ar: 'البنك الأهلي السعودي', swift: 'NCBK' },
  '40': { ar: 'البنك الأهلي السعودي (سامبا سابقًا)', swift: 'NCBK' },
  '05': { ar: 'مصرف الإنماء', swift: 'INMA' },
  '20': { ar: 'بنك الرياض', swift: 'RIBL' },
  '15': { ar: 'بنك البلاد', swift: 'ALBI' },
  '45': { ar: 'البنك السعودي الأول (ساب)', swift: 'SABB' },
  '30': { ar: 'البنك العربي الوطني', swift: 'ARNB' },
  '55': { ar: 'البنك السعودي الفرنسي', swift: 'BSFR' },
  '60': { ar: 'بنك الجزيرة', swift: 'BJAZ' },
  '65': { ar: 'البنك السعودي للاستثمار', swift: 'SIBC' },
  '90': { ar: 'بنك الخليج الدولي (ميم)', swift: 'GULF' },
  '78': { ar: 'STC Bank', swift: 'STCJ' },
  '36': { ar: 'بنك D360', swift: 'DBAK' },
  '93': { ar: 'بنك فيجن', swift: 'VIIO' },
  '95': { ar: 'الإمارات دبي الوطني', swift: 'EBIL' },
};

/** Bank code of a Saudi IBAN (SAkk BB…), or '' when the value is not a Saudi IBAN. */
export const bankCodeOfIban = (iban?: string): string => {
  const clean = (iban || '').replace(/\s+/g, '').toUpperCase();
  return /^SA\d{4}/.test(clean) ? clean.slice(4, 6) : '';
};

/**
 * The bank name shown to the user: Arabic name plus the short SWIFT code.
 * Order: the API's bank code, then the code inside the IBAN, then the API's own bank text.
 * An unknown code shows the code itself; nothing known shows «—». Never a fixed bank.
 */
export const bankLabel = (input: { bankCode?: string; iban?: string; bank?: string }): string => {
  const code = (input.bankCode || '').trim() || bankCodeOfIban(input.iban);
  const known = BANKS[code];
  if (known) return `${known.ar} (${known.swift})`;
  if (input.bank) return input.bank;
  return code ? `بنك غير معرّف — كود ${code}` : '—';
};
