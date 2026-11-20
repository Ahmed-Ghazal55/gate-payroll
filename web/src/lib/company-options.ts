import { api } from './api';
import type { CompanySponsor } from './types';

/** Load sponsor labels from the API — never hard-code BW/EX/TR display names (INT1). */
export async function loadCompanyOptions(): Promise<CompanySponsor[]> {
  try {
    const sponsors = await api.getFinanceSponsors();
    if (Array.isArray(sponsors) && sponsors.length > 0) return sponsors;
  } catch {
    /* fall through to companies.view list */
  }
  try {
    const companies = await api.getCompanies();
    return Array.isArray(companies) ? companies : [];
  } catch {
    return [];
  }
}

export function companyLabel(c: CompanySponsor): string {
  return c.shortNameAr || c.nameAr || c.code || c.id;
}
