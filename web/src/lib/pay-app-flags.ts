/** Per-app payroll UI/engine flags (aligned with Payroll.Api PayPlatforms). */

const NO_ELIGIBILITY = new Set(['jahez', 'chefz', 'hs', 'hungerstation', 'thechefz', 'the-chefz']);

export function normalizePayAppSlug(app: string): string {
  const code = (app || '').trim().toLowerCase().replace(/[\s_]+/g, '-');
  if (code === 'hungerstation' || code === 'hunger-station' || code === 'hunger') return 'hs';
  if (code === 'the-chefz' || code === 'thechefz' || code === 'shiftz' || code === 'chefsz') return 'chefz';
  return code;
}

/** T11: Jahez / Chefz / HungerStation have no eligibility rules tab or engine requirement. */
export function payAppHasEligibility(app: string): boolean {
  const slug = normalizePayAppSlug(app);
  return !NO_ELIGIBILITY.has(slug) && !NO_ELIGIBILITY.has(app.trim().toLowerCase());
}
