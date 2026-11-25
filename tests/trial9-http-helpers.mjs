import assert from 'node:assert/strict';

/**
 * Grant approvals.approve_own then re-login.
 * `clearCookie` must drop the invalidated session cookie before login (PUT /users clears it).
 */
export async function grantApproveOwn(req, put, username, password, clearCookie) {
  const users = await req('/api/users');
  const list = Array.isArray(users) ? users : (users.items || users.users || []);
  const u = list.find(x => String(x.username || '').toLowerCase() === String(username).toLowerCase());
  assert.ok(u?.id, 'missing user for approve_own: ' + username);
  const perms = [...new Set([...(u.customPermissions || []), 'approvals.approve_own'])];
  await put(`/api/users/${u.id}`, { customPermissions: perms });
  if (typeof clearCookie === 'function') clearCookie();
  return req('/api/auth/login', { username, password });
}
