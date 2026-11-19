'use client';

import React from 'react';
import {
  bankCodeOfIban,
  bankLabel,
  formatIban,
  isValidSaudiIban,
} from '../../lib/saudi-banks';

export interface IbanDisplayProps {
  iban?: string;
  bankCode?: string;
  bank?: string;
  /** When true, show a masked value (first 4 + last 4). */
  masked?: boolean;
  className?: string;
}

function maskIban(iban: string): string {
  const clean = iban.replace(/\s+/g, '').toUpperCase();
  if (clean.length < 10) return clean || '—';
  return `${clean.slice(0, 4)} **** **** ${clean.slice(-4)}`;
}

/**
 * Shared IBAN cell: formatted value, validity cue, and bank name/code underneath.
 * Uses saudi-banks.ts only — never invents a bank.
 */
export function IbanDisplay({
  iban,
  bankCode,
  bank,
  masked = false,
  className = '',
}: IbanDisplayProps) {
  const value = (iban || '').trim();
  if (!value) {
    return <span className={`text-[var(--muted)] ${className}`}>—</span>;
  }

  const valid = isValidSaudiIban(value);
  const code = (bankCode || '').trim() || bankCodeOfIban(value);
  const label = bankLabel({ bankCode: code || undefined, iban: value, bank });

  return (
    <div className={`flex flex-col gap-0.5 min-w-0 ${className}`}>
      <span
        className={`num font-mono text-xs tracking-wide ${
          valid ? 'text-[var(--ink)]' : 'text-[var(--warning)]'
        }`}
        title={valid ? undefined : 'صيغة الآيبان غير صحيحة'}
      >
        {masked ? maskIban(value) : formatIban(value)}
      </span>
      <span className="text-[11px] text-[var(--muted)] leading-snug">
        {label}
        {code ? <span className="font-mono ms-1">· {code}</span> : null}
        {!valid ? <span className="text-[var(--warning)] ms-1">· غير صالح</span> : null}
      </span>
    </div>
  );
}
