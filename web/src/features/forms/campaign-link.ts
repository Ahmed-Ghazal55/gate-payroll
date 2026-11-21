/** Build the public campaign URL (server publicUrl, else origin + path). */
export function campaignLink(
  c: { publicUrl?: string; path?: string },
  origin = typeof window !== 'undefined' ? window.location.origin : ''
): string {
  if (c.publicUrl) return c.publicUrl;
  return c.path ? `${origin}${c.path}` : '';
}

/**
 * Short label for the campaigns list: host + truncated path so the raw
 * `forms/<long hash>/` string is not the only visible link text.
 */
export function shortCampaignLinkLabel(
  c: { publicUrl?: string; path?: string },
  origin = typeof window !== 'undefined' ? window.location.origin : ''
): string {
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
