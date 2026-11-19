'use client';

// Campaigns, responses and builder of the one forms system (R3); moved from the old /forms/ page.
import React, { useEffect, useState } from 'react';
import { BarChart3, List } from 'lucide-react';
import { api } from '../../lib/api';
import type { Campaign, Submission, CustomForm } from '../../lib/types';
import { SectionTabs } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { CampaignsTab } from '../forms/CampaignsTab';
import { BuilderTab } from '../forms/BuilderTab';
import { ResponsesTab } from '../forms/ResponsesTab';
import { AnalyticsTab } from '../forms/AnalyticsTab';

export function FormsPanel({ tab, subtab }: { tab: 'campaigns' | 'responses' | 'builder'; subtab?: string }) {
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignsLoading, setCampaignsLoading] = useState(false);
  const [campaignsError, setCampaignsError] = useState<string | null>(null);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [submissionsLoading, setSubmissionsLoading] = useState(false);
  const [submissionsError, setSubmissionsError] = useState<string | null>(null);
  const [customForms, setCustomForms] = useState<CustomForm[]>([]);
  const [formsLoading, setFormsLoading] = useState(false);
  const [formsError, setFormsError] = useState<string | null>(null);

  const analytics = tab === 'campaigns' && subtab === 'analytics';

  const loadCampaigns = async () => {
    setCampaignsLoading(true);
    setCampaignsError(null);
    try {
      setCampaigns(await api.getCampaigns());
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
      setSubmissions(await api.getSubmissions());
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
      setCustomForms(await api.getCustomForms());
    } catch (err: any) {
      setFormsError(err?.message || 'تعذر تحميل مصمم النماذج');
    } finally {
      setFormsLoading(false);
    }
  };

  useEffect(() => {
    if (tab === 'builder') {
      loadCustomForms();
      return;
    }
    // Campaign KPI cards and analytics need the responses; the responses list needs the campaigns.
    loadCampaigns();
    loadSubmissions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  if (tab === 'builder') {
    return <BuilderTab forms={customForms} loading={formsLoading} error={formsError} onRefresh={loadCustomForms} />;
  }

  if (tab === 'responses') {
    return (
      <ResponsesTab
        submissions={submissions}
        campaigns={campaigns}
        loading={submissionsLoading}
        error={submissionsError}
        onRefresh={loadSubmissions}
      />
    );
  }

  const openCampaigns = campaigns.filter(
    (c) => c.status === 'open' && (!c.expiresAt || new Date(c.expiresAt).getTime() > Date.now())
  ).length;
  const pending = submissions.filter((s) => s.status === 'pending').length;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <StatCard label="الحملات المفتوحة" value={campaignsLoading ? '…' : openCampaigns} loading={campaignsLoading} />
        <StatCard label="إجمالي الردود" value={submissionsLoading ? '…' : submissions.length} loading={submissionsLoading} />
        <StatCard label="ردود بانتظار المراجعة" value={submissionsLoading ? '…' : pending} loading={submissionsLoading} />
      </div>
      <SectionTabs
        paramName="subtab"
        activeTab={analytics ? 'analytics' : 'list'}
        variant="pills"
        ariaLabel="عرض الحملات"
        tabs={[
          { id: 'list', label: 'قائمة الحملات', icon: <List size={14} /> },
          { id: 'analytics', label: 'التحليلات', icon: <BarChart3 size={14} /> },
        ]}
      />
      {analytics ? (
        <AnalyticsTab submissions={submissions} campaigns={campaigns} />
      ) : (
        <CampaignsTab campaigns={campaigns} loading={campaignsLoading} error={campaignsError} onRefresh={loadCampaigns} />
      )}
    </div>
  );
}
