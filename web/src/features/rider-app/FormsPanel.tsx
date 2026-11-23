'use client';

// Campaigns, responses and builder of the one forms system (R3); moved from the old /forms/ page.
import React, { useEffect, useState } from 'react';
import { BarChart3, ClipboardCheck, List } from 'lucide-react';
import { api } from '../../lib/api';
import type { Campaign, Submission, CustomForm } from '../../lib/types';
import { useAuth } from '../../lib/auth-context';
import { SectionTabs } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { CampaignsTab } from '../forms/CampaignsTab';
import { BuilderTab } from '../forms/BuilderTab';
import { ResponsesTab } from '../forms/ResponsesTab';
import { AnalyticsTab } from '../forms/AnalyticsTab';
import { EvaluationsTab } from '../forms/EvaluationsTab';

export function FormsPanel({ tab, subtab }: { tab: 'campaigns' | 'responses' | 'builder'; subtab?: string }) {
  const { can, isAdmin, user } = useAuth();
  const isSupervisor = Boolean(user?.role && user.role.toLowerCase().includes('supervisor'));
  const canViewForms = isAdmin || can('forms.view');
  const canViewEvaluations = !isSupervisor && (isAdmin || can('forms.evaluations.view'));

  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignsLoading, setCampaignsLoading] = useState(false);
  const [campaignsError, setCampaignsError] = useState<string | null>(null);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [submissionsLoading, setSubmissionsLoading] = useState(false);
  const [submissionsError, setSubmissionsError] = useState<string | null>(null);
  const [customForms, setCustomForms] = useState<CustomForm[]>([]);
  const [formsLoading, setFormsLoading] = useState(false);
  const [formsError, setFormsError] = useState<string | null>(null);

  const showEvaluations =
    subtab === 'evaluations' || (!canViewForms && canViewEvaluations);
  const analytics = tab === 'campaigns' && subtab === 'analytics' && !showEvaluations;

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
    if (!canViewForms) return;
    // Campaign KPI cards and analytics need the responses; the responses list needs the campaigns.
    loadCampaigns();
    loadSubmissions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, canViewForms]);

  if (tab === 'builder') {
    return <BuilderTab forms={customForms} loading={formsLoading} error={formsError} onRefresh={loadCustomForms} />;
  }

  if (tab === 'responses') {
    return (
      <div className="space-y-4">
        {canViewEvaluations && canViewForms && (
          <SectionTabs
            paramName="subtab"
            activeTab={showEvaluations ? 'evaluations' : 'forms'}
            variant="pills"
            ariaLabel="نوع الردود"
            tabs={[
              { id: 'forms', label: 'ردود الحملات والنماذج', icon: <List size={14} /> },
              {
                id: 'evaluations',
                label: 'تقييم المشرفين والمنصات',
                icon: <ClipboardCheck size={14} />,
              },
            ]}
          />
        )}
        {showEvaluations ? (
          <EvaluationsTab />
        ) : (
          <ResponsesTab
            submissions={submissions}
            campaigns={campaigns}
            loading={submissionsLoading}
            error={submissionsError}
            onRefresh={loadSubmissions}
          />
        )}
      </div>
    );
  }

  const openCampaigns = campaigns.filter(
    (c) => c.status === 'open' && (!c.expiresAt || new Date(c.expiresAt).getTime() > Date.now())
  ).length;
  const pending = submissions.filter((s) => s.status === 'pending').length;

  return (
    <div className="space-y-4">
      {canViewForms && !showEvaluations && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <StatCard label="الحملات المفتوحة" value={campaignsLoading ? '…' : openCampaigns} loading={campaignsLoading} />
          <StatCard label="إجمالي الردود" value={submissionsLoading ? '…' : submissions.length} loading={submissionsLoading} />
          <StatCard label="ردود بانتظار المراجعة" value={submissionsLoading ? '…' : pending} loading={submissionsLoading} />
        </div>
      )}
      {canViewForms && (
        <SectionTabs
          paramName="subtab"
          activeTab={showEvaluations ? 'evaluations' : analytics ? 'analytics' : 'list'}
          variant="pills"
          ariaLabel="عرض الحملات"
          tabs={[
            { id: 'list', label: 'قائمة الحملات', icon: <List size={14} /> },
            { id: 'analytics', label: 'التحليلات', icon: <BarChart3 size={14} /> },
            ...(canViewEvaluations
              ? [
                  {
                    id: 'evaluations',
                    label: 'تقييم المشرفين والمنصات',
                    icon: <ClipboardCheck size={14} />,
                  },
                ]
              : []),
          ]}
        />
      )}
      {showEvaluations ? (
        <EvaluationsTab />
      ) : analytics ? (
        <AnalyticsTab submissions={submissions} campaigns={campaigns} />
      ) : (
        <CampaignsTab campaigns={campaigns} loading={campaignsLoading} error={campaignsError} onRefresh={loadCampaigns} />
      )}
    </div>
  );
}
