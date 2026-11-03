'use client';

import React, { useState, useEffect, Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { api } from '../../lib/api';
import type { Campaign, Submission, CustomForm } from '../../lib/types';
import { PageHeader } from '../../components/ui/PageHeader';
import { Tabs } from '../../components/ui/Tabs';
import { Skeleton } from '../../components/ui/EmptyState';
import { useAuth } from '../../lib/auth-context';
import { CampaignsTab } from '../../features/forms/CampaignsTab';
import { BuilderTab } from '../../features/forms/BuilderTab';
import { ResponsesTab } from '../../features/forms/ResponsesTab';
import { AnalyticsTab } from '../../features/forms/AnalyticsTab';

export default function FormsPage() {
  return (
    <Suspense fallback={<Skeleton rows={6} className="h-16" />}>
      <FormsWorkspace />
    </Suspense>
  );
}

function FormsWorkspace() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { canManageForms } = useAuth();

  const activeTab = searchParams?.get('tab') || 'campaigns';

  // Data states
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignsLoading, setCampaignsLoading] = useState(false);
  const [campaignsError, setCampaignsError] = useState<string | null>(null);

  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [submissionsLoading, setSubmissionsLoading] = useState(false);
  const [submissionsError, setSubmissionsError] = useState<string | null>(null);

  const [customForms, setCustomForms] = useState<CustomForm[]>([]);
  const [formsLoading, setFormsLoading] = useState(false);
  const [formsError, setFormsError] = useState<string | null>(null);

  const loadCampaigns = async () => {
    setCampaignsLoading(true);
    setCampaignsError(null);
    try {
      const data = await api.getCampaigns();
      setCampaigns(data);
    } catch (err: any) {
      setCampaignsError(err?.message || 'تعذر تحميل الحملات');
    } finally {
      setCampaignsLoading(false);
    }
  };

  const loadSubmissions = async () => {
    setSubmissionsLoading(true);
    setSubmissionsError(null);
    try {
      const data = await api.getSubmissions();
      setSubmissions(data);
    } catch (err: any) {
      setSubmissionsError(err?.message || 'تعذر تحميل الردود');
    } finally {
      setSubmissionsLoading(false);
    }
  };

  const loadCustomForms = async () => {
    setFormsLoading(true);
    setFormsError(null);
    try {
      const data = await api.getCustomForms();
      setCustomForms(data);
    } catch (err: any) {
      setFormsError(err?.message || 'تعذر تحميل مصمم النماذج');
    } finally {
      setFormsLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'campaigns' || activeTab === 'analytics' || activeTab === 'responses') {
      loadCampaigns();
    }
    if (activeTab === 'responses' || activeTab === 'analytics') {
      loadSubmissions();
    }
    if (activeTab === 'builder') {
      loadCustomForms();
    }
  }, [activeTab]);

  const workspaceTabs = [
    { id: 'campaigns', label: 'الحملات والروابط', badge: campaigns.length || undefined },
    { id: 'builder', label: 'مصمم النماذج', badge: customForms.length || undefined },
    {
      id: 'responses',
      label: 'مراجعة الردود',
      badge: submissions.filter((s) => s.status === 'pending').length || undefined,
    },
    { id: 'analytics', label: 'التحليلات' },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="النماذج والردود"
        eyebrow="مساحة الإدارة 1.0"
        description="إدارة حملات استلام الآيبان وأنظمة العمل، مصمم النماذج المخصصة، وصندوق موحد لمراجعة واعتماد ردود المناديب."
      />

      {/* URL Tabs */}
      <Tabs
        tabs={workspaceTabs}
        activeTab={activeTab}
        onChange={(tabId) => {
          const params = new URLSearchParams(searchParams?.toString() || '');
          params.set('tab', tabId);
          router.push(`/forms/?${params.toString()}`);
        }}
      />

      {/* Active Tab Panel */}
      {activeTab === 'campaigns' && (
        <CampaignsTab
          campaigns={campaigns}
          loading={campaignsLoading}
          error={campaignsError}
          onRefresh={loadCampaigns}
        />
      )}

      {activeTab === 'builder' && (
        <BuilderTab
          forms={customForms}
          loading={formsLoading}
          error={formsError}
          onRefresh={loadCustomForms}
        />
      )}

      {activeTab === 'responses' && (
        <ResponsesTab
          submissions={submissions}
          campaigns={campaigns}
          loading={submissionsLoading}
          error={submissionsError}
          onRefresh={loadSubmissions}
        />
      )}

      {activeTab === 'analytics' && (
        <AnalyticsTab
          submissions={submissions}
          campaigns={campaigns}
        />
      )}
    </div>
  );
}
