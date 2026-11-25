/** Sidebar active-item matching: path + query (tab/app/…), not path alone (T8/T9). */

const IGNORED_QUERY = new Set(['period', 'month']);

function cleanPath(path: string): string {
  return (path || '/').split('?')[0].replace(/\/$/, '') || '/';
}

function parseHref(href: string): { path: string; params: URLSearchParams } {
  const [rawPath, rawQuery = ''] = href.split('?');
  return { path: cleanPath(rawPath), params: new URLSearchParams(rawQuery) };
}

function relevantEntries(params: URLSearchParams): Array<[string, string]> {
  return [...params.entries()].filter(([k]) => !IGNORED_QUERY.has(k));
}

/**
 * True when this nav href should show as the active sidebar child.
 * - Path must match (exact, or approvals prefix, or current under href path).
 * - Every non-period query on the href (tab, app, sub, …) must equal the current URL.
 * - If the href omits `app` but same-path siblings set `app` for the same tab, current must have no `app`.
 * - Bare hrefs (no tab/app) are inactive when the current tab/app matches another same-path sibling.
 */
export function isNavHrefActive(
  href: string,
  pathname: string,
  search: string | URLSearchParams | null | undefined,
  options?: { groupId?: string; siblingHrefs?: string[] }
): boolean {
  const target = parseHref(href);
  const currentPath = cleanPath(pathname);
  const current = search
    ? new URLSearchParams(typeof search === 'string' ? search : search.toString())
    : new URLSearchParams();
  const siblings = (options?.siblingHrefs ?? []).map(parseHref);

  const pathMatch =
    currentPath === target.path ||
    (options?.groupId === 'approvals' && currentPath.startsWith('/approvals')) ||
    (target.path !== '/' && currentPath.startsWith(`${target.path}/`));

  if (!pathMatch) return false;

  const targetEntries = relevantEntries(target.params);

  if (targetEntries.length > 0) {
    for (const [key, value] of targetEntries) {
      if ((current.get(key) ?? '') !== value) return false;
    }
    if (!target.params.has('app')) {
      const tab = target.params.get('tab');
      const hasAppSibling = siblings.some(
        (s) =>
          s.path === target.path &&
          s.params.has('app') &&
          (!tab || s.params.get('tab') === tab)
      );
      if (hasAppSibling && current.get('app')) return false;
    }
    return true;
  }

  // Bare href: inactive when a same-path sibling's tab/app fully matches the current URL.
  const siblingOwnsCurrent = siblings.some((s) => {
    if (s.path !== target.path) return false;
    const entries = relevantEntries(s.params);
    if (entries.length === 0) return false;
    return entries.every(([k, v]) => (current.get(k) ?? '') === v);
  });
  if (siblingOwnsCurrent) return false;

  // Child-path sibling (e.g. /debts/promissory) mapped to ?tab=promissory
  if (target.path === '/debts' && current.get('tab') === 'promissory') return false;

  return (
    currentPath === target.path ||
    (options?.groupId === 'approvals' && currentPath.startsWith('/approvals'))
  );
}
