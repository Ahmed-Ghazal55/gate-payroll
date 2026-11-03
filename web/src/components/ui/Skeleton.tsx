import React from 'react';

export interface SkeletonProps {
  className?: string;
  rows?: number;
}

export function Skeleton({ className = '', rows = 1 }: SkeletonProps) {
  if (rows > 1) {
    return (
      <div className="space-y-2.5 w-full">
        {Array.from({ length: rows }).map((_, i) => (
          <div
            key={i}
            className={`animate-pulse rounded-lg bg-[var(--raised)] h-4 w-full ${className}`}
          />
        ))}
      </div>
    );
  }

  return (
    <div
      className={`animate-pulse rounded-lg bg-[var(--raised)] ${className}`}
    />
  );
}
