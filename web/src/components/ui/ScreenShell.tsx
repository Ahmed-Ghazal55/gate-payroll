'use client';

import React from 'react';
import { PageHeader } from './PageHeader';
import { EmptyState } from './EmptyState';

export function ScreenShell({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="space-y-4">
      <PageHeader title={title} description={description} />
      {children ?? (
        <EmptyState
          title="الشاشة جاهزة للربط"
          description="المسار والقائمة الجانبية جاهزان؛ المحتوى التفصيلي يُكمَّل عند وحدة الشاشة الخاصة به."
        />
      )}
    </div>
  );
}
