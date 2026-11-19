import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  BALANCES_PLACEHOLDER_AR,
  CANONICAL_PLATFORMS,
  buildCompanyStatsExport,
  buildWorkforceCompanyUrl,
  normalizeCompaniesTab,
  platformCountFor,
  platformLabelAr,
} from '../../src/features/companies/company-stats.ts';

describe('companies stats helpers (U-W3-03)', () => {
  it('maps ?tab= values including stats and legacy aliases', () => {
    assert.equal(normalizeCompaniesTab('stats'), 'stats');
    assert.equal(normalizeCompaniesTab('cr'), 'cr');
    assert.equal(normalizeCompaniesTab('registrations'), 'cr');
    assert.equal(normalizeCompaniesTab('expiring'), 'expiring');
    assert.equal(normalizeCompaniesTab('documents'), 'expiring');
    assert.equal(normalizeCompaniesTab('companies'), 'companies');
    assert.equal(normalizeCompaniesTab(null), 'companies');
    assert.equal(normalizeCompaniesTab('unknown'), 'companies');
  });

  it('builds workforce filter URLs per company and optional platform', () => {
    assert.equal(buildWorkforceCompanyUrl('7000000001'), '/workforce/?company=7000000001');
    assert.equal(
      buildWorkforceCompanyUrl('7000000001', 'JAHEZ'),
      '/workforce/?company=7000000001&platform=JAHEZ'
    );
    assert.equal(buildWorkforceCompanyUrl(''), '/workforce/');
  });

  it('returns pure Arabic platform labels with no Latin letters', () => {
    for (const p of CANONICAL_PLATFORMS) {
      const label = platformLabelAr(p.code);
      assert.equal(/[A-Za-z]/.test(label), false, `Latin letter in ${p.code}: ${label}`);
    }
    assert.equal(platformLabelAr('CHEFZ'), 'ذا شفز');
    assert.equal(platformLabelAr('HUNGERSTATION'), 'هنقرستيشن');
    assert.equal(platformLabelAr('UNKNOWN_APP', 'Latin Name'), 'منصة أخرى');
    assert.equal(platformLabelAr('UNKNOWN_APP', 'تطبيق محلي'), 'تطبيق محلي');
  });

  it('counts canonical platforms and builds flat Arabic export rows', () => {
    const sample = {
      companyId: 'syn-c1',
      code: 'SYN',
      nameAr: 'شركة تجريبية',
      shortNameAr: 'تجريبية',
      unifiedNumber: '7000000001',
      commercialReg: '4000000001',
      status: 'active',
      erpLinked: true,
      sponsorships: { total: 10, riders: 8, admins: 2, active: 9, onLeave: 1 },
      platformAccounts: {
        total: 6,
        active: 5,
        assigned: 4,
        byPlatform: [
          { code: 'JAHEZ', nameAr: 'جاهز', total: 4, active: 4, assigned: 3 },
          { code: 'CHEFZ', nameAr: 'ذا شفز', total: 2, active: 1, assigned: 1 },
        ],
      },
    };
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'JAHEZ'), 4);
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'SHIFTZ'), 2);
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'KEETA'), 0);

    const exported = buildCompanyStatsExport([sample]);
    assert.equal(exported.headers.every((h) => !/[A-Za-z]/.test(h)), true);
    assert.equal(exported.rows.length, 1);
    assert.equal(exported.rows[0][4], 10);
    assert.equal(exported.rows[0][9], 6);
  });

  it('companies page uses contract types, ?tab=stats, and deferred balances message', () => {
    const pageSource = readFileSync(
      new URL('../../src/app/companies/page.tsx', import.meta.url),
      'utf8'
    );
    assert.equal(pageSource.includes("from '../../lib/types'"), false);
    assert.equal(pageSource.includes('api.getCompaniesStats'), true);
    assert.equal(pageSource.includes('BALANCES_PLACEHOLDER_AR'), true);
    assert.equal(BALANCES_PLACEHOLDER_AR, 'الأرصدة بعد ربط المحاسبة');
  });
});
