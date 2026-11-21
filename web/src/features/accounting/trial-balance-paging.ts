/** Filter-all then paginate: export keeps the full filtered set. */
export function paginateFilteredRows<T>(
  filteredRows: T[],
  page: number,
  pageSize: number
): {
  totalPages: number;
  safePage: number;
  pageRows: T[];
  exportRows: T[];
} {
  const totalPages = Math.max(1, Math.ceil(filteredRows.length / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const start = (safePage - 1) * pageSize;
  return {
    totalPages,
    safePage,
    pageRows: filteredRows.slice(start, start + pageSize),
    exportRows: filteredRows,
  };
}
