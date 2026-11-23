'use client';

import React, { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import { Skeleton } from '../../components/ui/EmptyState';
import { EvaluationPublicForm } from '../../features/forms/EvaluationPublicForm';

function EvaluationPageContent() {
  const searchParams = useSearchParams();
  const token = searchParams?.get('token') || searchParams?.get('evaluation') || '';
  const mode = searchParams?.get('mode') === 'app' ? 'app' : 'public';
  return <EvaluationPublicForm token={token} mode={mode} />;
}

export default function EvaluationPage() {
  return (
    <Suspense fallback={<Skeleton rows={6} className="h-24" />}>
      <EvaluationPageContent />
    </Suspense>
  );
}
