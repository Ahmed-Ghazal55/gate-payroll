import { describe, it } from 'node:test';
import assert from 'node:assert/strict';

// Mirrors web/src/features/forms/campaign-link.ts (pure helpers; keep in sync).
function campaignLink(c, origin = 'https://pay.example') {
  if (c.publicUrl) return c.publicUrl;
  return c.path ? `${origin}${c.path}` : '';
}

function shortCampaignLinkLabel(c, origin = 'https://pay.example') {
  const full = campaignLink(c, origin);
  if (!full) return '';
  try {
    const url = new URL(full);
    const parts = url.pathname.replace(/\/+$/, '').split('/').filter(Boolean);
    const last = parts[parts.length - 1] || '';
    const shortTail = last.length > 10 ? `${last.slice(0, 4)}…${last.slice(-4)}` : last;
    const prefix = parts.length > 1 ? `${parts[0]}/` : '';
    return `${url.host}/${prefix}${shortTail}`;
  } catch {
    const path = c.path || full;
    const hash = path.replace(/^.*\//, '').replace(/\/$/, '');
    if (hash.length > 10) return `forms/${hash.slice(0, 4)}…${hash.slice(-4)}`;
    return path;
  }
}

describe('campaign short link (F-R1)', () => {
  it('builds full link from path', () => {
    assert.equal(
      campaignLink({ path: '/forms/abcdef0123456789abcdef/' }),
      'https://pay.example/forms/abcdef0123456789abcdef/'
    );
  });

  it('prefers publicUrl', () => {
    assert.equal(
      campaignLink({ publicUrl: 'https://forms.gate/x', path: '/forms/old/' }),
      'https://forms.gate/x'
    );
  });

  it('shortens the long forms hash instead of showing the raw path', () => {
    const short = shortCampaignLinkLabel({
      path: '/forms/abcdef0123456789abcdef/',
    });
    assert.equal(short, 'pay.example/forms/abcd…cdef');
    assert.equal(short.includes('abcdef0123456789abcdef'), false);
  });

  it('returns empty when no link exists', () => {
    assert.equal(shortCampaignLinkLabel({}), '');
  });
});
