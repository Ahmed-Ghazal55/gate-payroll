'use client';

import { useCallback } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import {
  isValidPeriod,
  readUrlPeriod,
  resolvePeriod,
  withPeriodInUrl,
} from './period';

/**
 * Single URL-backed period hook (`?period=YYYY-MM`).
 * Both the global header picker and page-level pickers read and write the exact same URL parameter.
 */
export function usePeriod(fallback?: string) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const urlPeriod = readUrlPeriod(searchParams);
  const period = resolvePeriod(searchParams, fallback);

  const setPeriod = useCallback(
    (nextPeriod: string, extraPatch?: Record<string, string | null>) => {
      const clean = nextPeriod.trim();
      if (!isValidPeriod(clean)) return;
      const target = withPeriodInUrl(pathname || '/', searchParams, clean, extraPatch);
      router.replace(target, { scroll: false });
    },
    [pathname, router, searchParams]
  );

  return { period, urlPeriod, setPeriod };
}
