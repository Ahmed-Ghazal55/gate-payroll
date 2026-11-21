// Pure preview rules for the statements tab (phone-width card).
// Warn on status, not on approvedOnly — the API preview already requests approved-only rows.

export function shouldWarnUnapproved(status?: string | null): boolean {
  return status !== 'approved';
}
