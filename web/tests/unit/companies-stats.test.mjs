import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  CANONICAL_PLATFORMS,
  auditActionLabelAr,
  buildCompanyStatsExport,
  buildWorkforceCompanyUrl,
  computeCompanyStatsTotals,
  filterCompanyStatsItems,
  getPlatformColumns,
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

  it('returns pure Arabic platform labels and audit labels with no Latin letters', () => {
    for (const p of CANONICAL_PLATFORMS) {
      const label = platformLabelAr(p.code);
      assert.equal(/[A-Za-z]/.test(label), false, `Latin letter in ${p.code}: ${label}`);
    }
    assert.equal(platformLabelAr('CHEFZ'), 'ذا شفز');
    assert.equal(platformLabelAr('HUNGERSTATION'), 'هنقرستيشن');
    assert.equal(platformLabelAr('OTHER'), 'أخرى');
    assert.equal(platformLabelAr('UNKNOWN_APP', 'Latin Name'), 'منصة أخرى');
    assert.equal(platformLabelAr('UNKNOWN_APP', 'تطبيق محلي'), 'تطبيق محلي');

    for (const code of [
      'company.seeded',
      'company.created',
      'company.document_created',
      'company.registration_updated',
      'unknown.latin.action',
    ]) {
      const ar = auditActionLabelAr(code);
      assert.equal(/[A-Za-z]/.test(ar), false, `Latin letter in audit label ${code}: ${ar}`);
    }
  });

  it('counts canonical + non-canonical platforms and builds matching Arabic export rows', () => {
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
        total: 9,
        active: 8,
        assigned: 6,
        byPlatform: [
          { code: 'JAHEZ', nameAr: 'جاهز', total: 4, active: 4, assigned: 3 },
          { code: 'CHEFZ', nameAr: 'ذا شفز', total: 2, active: 1, assigned: 1 },
          { code: 'OTHER', nameAr: 'أخرى', total: 3, active: 3, assigned: 2 },
        ],
      },
    };
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'JAHEZ'), 4);
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'SHIFTZ'), 2);
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'OTHER'), 3);
    assert.equal(platformCountFor(sample.platformAccounts.byPlatform, 'KEETA'), 0);

    const cols = getPlatformColumns([sample]);
    assert.equal(cols.some((c) => c.code === 'OTHER' && c.labelAr === 'أخرى'), true);

    const exported = buildCompanyStatsExport([sample]);
    assert.equal(exported.headers.every((h) => !/[A-Za-z]/.test(h)), true);
    assert.equal(exported.rows.length, 1);
    assert.equal(exported.rows[0][4], 10);
    assert.equal(exported.rows[0][9], 9);
    // Sum of all exported platform columns must equal platformAccounts.total
    const platformCellsSum = exported.rows[0]
      .slice(12)
      .reduce((acc, v) => acc + Number(v || 0), 0);
    assert.equal(platformCellsSum, 9);
  });

  it('recomputes full, filtered, and empty-search totals including non-canonical platforms', () => {
    const items = [
      {
        companyId: 'syn-c1',
        code: 'BW',
        nameAr: 'شركة تجريبية أولى',
        shortNameAr: 'الأولى',
        unifiedNumber: '7000000001',
        commercialReg: '4000000001',
        status: 'active',
        erpLinked: true,
        sponsorships: { total: 10, riders: 8, admins: 2, active: 9, onLeave: 1 },
        platformAccounts: {
          total: 7,
          active: 6,
          assigned: 5,
          byPlatform: [
            { code: 'JAHEZ', nameAr: 'جاهز', total: 4, active: 4, assigned: 3 },
            { code: 'OTHER', nameAr: 'أخرى', total: 3, active: 2, assigned: 2 },
          ],
        },
      },
      {
        companyId: 'syn-c2',
        code: 'EX',
        nameAr: 'شركة تجريبية ثانية',
        shortNameAr: 'الثانية',
        unifiedNumber: '7000000002',
        commercialReg: '4000000002',
        status: 'active',
        erpLinked: false,
        sponsorships: { total: 5, riders: 4, admins: 1, active: 4, onLeave: 1 },
        platformAccounts: {
          total: 3,
          active: 2,
          assigned: 1,
          byPlatform: [
            { code: 'JAHEZ', nameAr: 'جاهز', total: 1, active: 1, assigned: 1 },
            { code: 'LOCAL_APP', nameAr: 'منصة محلية', total: 2, active: 1, assigned: 0 },
          ],
        },
      },
    ];

    const fullTotals = computeCompanyStatsTotals(items);
    assert.equal(fullTotals.companiesCount, 2);
    assert.equal(fullTotals.sponsoredTotal, 15);
    assert.equal(fullTotals.sponsoredRiders, 12);
    assert.equal(fullTotals.sponsoredAdmins, 3);
    assert.equal(fullTotals.platformAccountsTotal, 10);
    assert.equal(platformCountFor(fullTotals.byPlatform, 'JAHEZ'), 5);
    assert.equal(platformCountFor(fullTotals.byPlatform, 'OTHER'), 3);
    assert.equal(platformCountFor(fullTotals.byPlatform, 'LOCAL_APP'), 2);

    const filtered = filterCompanyStatsItems(items, '7000000002');
    assert.equal(filtered.length, 1);
    const filteredTotals = computeCompanyStatsTotals(filtered);
    assert.equal(filteredTotals.companiesCount, 1);
    assert.equal(filteredTotals.sponsoredTotal, 5);
    assert.equal(filteredTotals.platformAccountsTotal, 3);
    assert.equal(platformCountFor(filteredTotals.byPlatform, 'JAHEZ'), 1);
    assert.equal(platformCountFor(filteredTotals.byPlatform, 'LOCAL_APP'), 2);

    const empty = filterCompanyStatsItems(items, 'غير-موجود');
    assert.equal(empty.length, 0);
    const emptyTotals = computeCompanyStatsTotals(empty);
    assert.equal(emptyTotals.companiesCount, 0);
    assert.equal(emptyTotals.sponsoredTotal, 0);
    assert.equal(emptyTotals.platformAccountsTotal, 0);
    assert.deepEqual(emptyTotals.byPlatform, []);
  });
});
