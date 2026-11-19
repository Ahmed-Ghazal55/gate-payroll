import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { campaignLink, shortCampaignLinkLabel } from '../../src/features/forms/campaign-link.ts';

describe('campaign short link (F-R1)', () => {
  it('builds full link from path', () => {
    assert.equal(
      campaignLink({ path: '/forms/abcdef0123456789abcdef/' }, 'https://pay.example'),
      'https://pay.example/forms/abcdef0123456789abcdef/'
    );
  });

  it('prefers publicUrl', () => {
    assert.equal(
      campaignLink({ publicUrl: 'https://forms.gate/x', path: '/forms/old/' }, 'https://pay.example'),
      'https://forms.gate/x'
    );
  });

  it('shortens the long forms hash instead of showing the raw path', () => {
    const short = shortCampaignLinkLabel(
      { path: '/forms/abcdef0123456789abcdef/' },
      'https://pay.example'
    );
    assert.equal(short, 'pay.example/forms/abcd…cdef');
    assert.equal(short.includes('abcdef0123456789abcdef'), false);
  });

  it('returns empty when no link exists', () => {
    assert.equal(shortCampaignLinkLabel({}, 'https://pay.example'), '');
  });
});
