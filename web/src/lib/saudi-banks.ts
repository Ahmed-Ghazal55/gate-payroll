// Display names for Saudi bank codes (characters 5–6 of a Saudi IBAN).
// Same list as the public rider form (RiderForm/src/rider-form.ts BANKS) and the SWIFT map in
// Payroll.Api/BankPayrollFiles.cs. It is a label lookup only: the code itself comes from the API
// (`bankCode`) or from the IBAN, never from a default.
const BANKS: Record<string, { ar: string; swift: string }> = {
  '05': { ar: 'مصرف الإنماء', swift: 'INMA' },
  '10': { ar: 'البنك الأهلي السعودي', swift: 'NCBK' },
  '15': { ar: 'بنك البلاد', swift: 'ALBI' },
  '20': { ar: 'بنك الرياض', swift: 'RIBL' },
  '30': { ar: 'البنك العربي الوطني', swift: 'ARNB' },
  '36': { ar: 'بنك D360', swift: 'DBAK' },
  '40': { ar: 'البنك الأهلي السعودي (سامبا سابقًا)', swift: 'NCBK' },
  '45': { ar: 'البنك السعودي الأول (ساب)', swift: 'SABB' },
  '55': { ar: 'البنك السعودي الفرنسي', swift: 'BSFR' },
  '60': { ar: 'بنك الجزيرة', swift: 'BJAZ' },
  '65': { ar: 'البنك السعودي للاستثمار', swift: 'SIBC' },
  '71': { ar: 'بنك عمان الوطني', swift: 'NBOB' },
  '72': { ar: 'بنك قطر الوطني', swift: 'QNBA' },
  '73': { ar: 'بنك أبوظبي الأول', swift: 'FABM' },
  '75': { ar: 'بنك الكويت الوطني', swift: 'NBOK' },
  '76': { ar: 'بنك مسقط', swift: 'BMUS' },
  '78': { ar: 'STC Bank', swift: 'STCJ' },
  '79': { ar: 'المصرف العراقي للتجارة', swift: 'NBIQ' },
  '80': { ar: 'مصرف الراجحي', swift: 'RJHI' },
  '81': { ar: 'دويتشه بنك', swift: 'DEUT' },
  '82': { ar: 'البنك الوطني الباكستاني', swift: 'NBPA' },
  '84': { ar: 'بنك زراعات التركي', swift: 'TCZB' },
  '85': { ar: 'بي إن بي باريبا', swift: 'BNPA' },
  '86': { ar: 'جي بي مورغان', swift: 'CHAS' },
  '87': { ar: 'بنك الصين الصناعي والتجاري', swift: 'ICBK' },
  '88': { ar: 'بنك طوكيو ميتسوبيشي', swift: 'BOTK' },
  '89': { ar: 'كريدي سويس', swift: 'CRES' },
  '90': { ar: 'بنك الخليج الدولي (ميم)', swift: 'GULF' },
  '91': { ar: 'ستاندرد تشارترد', swift: 'SCBL' },
  '92': { ar: 'بنك البحرين الوطني', swift: 'BSHR' },
  '93': { ar: 'بنك فيجن', swift: 'VIIO' },
  '95': { ar: 'الإمارات دبي الوطني', swift: 'EBIL' },
};

/** Strip spaces and uppercase; empty string when missing. */
export const cleanIban = (iban?: string): string =>
  (iban || '').replace(/\s+/g, '').toUpperCase();

/** Display form: groups of four, e.g. SA00 0000 0000 0000 0000 0000 */
export const formatIban = (iban?: string): string => {
  const clean = cleanIban(iban);
  return clean ? clean.replace(/(.{4})/g, '$1 ').trim() : '—';
};

/** Bank code of a Saudi IBAN (SAkk BB…), or '' when the value is not a Saudi IBAN. */
export const bankCodeOfIban = (iban?: string): string => {
  const clean = cleanIban(iban);
  return /^SA\d{4}/.test(clean) ? clean.slice(4, 6) : '';
};

/**
 * Saudi IBAN format + mod-97 check (ISO 13616). Fake fixture value
 * SA0000000000000000000000 fails the checksum and is rejected.
 */
export const isValidSaudiIban = (iban?: string): boolean => {
  const clean = cleanIban(iban);
  if (!/^SA[0-9]{22}$/.test(clean)) return false;
  const rearranged = clean.slice(4) + '2810' + clean.slice(2, 4);
  let remainder = 0;
  for (const ch of rearranged) {
    remainder = (remainder * 10 + Number(ch)) % 97;
  }
  return remainder === 1;
};

/** Short bank code (SWIFT prefix, about 4 capital letters) of a Saudi IBAN, or '' when unknown. */
export const bankShortCodeOfIban = (iban?: string): string => BANKS[bankCodeOfIban(iban)]?.swift ?? '';

/** Arabic bank name of a Saudi IBAN, or '' when unknown. */
export const bankNameOfIban = (iban?: string): string => BANKS[bankCodeOfIban(iban)]?.ar ?? '';

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
