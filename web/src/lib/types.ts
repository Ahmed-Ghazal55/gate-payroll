export interface UserProfile {
  username: string;
  role: string;
  displayNameAr: string;
  permissions?: string[];
}

export type NotificationCategory = 'general' | 'personal' | 'system' | 'approvals';

export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  category: NotificationCategory;
  unread: boolean;
  createdAt: string;
  link?: string;
  actionText?: string;
}

export interface NotificationSummary {
  unreadCount: number;
  approvalCount: number;
  byCategory: Record<NotificationCategory, number>;
}

export interface SearchResult {
  id: string;
  kind: 'rider' | 'company' | 'debt' | 'campaign';
  title: string;
  subtitle: string;
  url: string;
}

export interface CompanySponsor {
  id: string;
  code: string;
  shortNameAr: string;
  nameAr: string;
  commercialReg?: string;
}

export interface DashboardMetrics {
  riders: number;
  imports: number;
  pendingResponses: number;
  approvedRuns: number;
  latestAudit: Array<{
    id: string;
    action: string;
    actor: string;
    at: string;
  }>;
}

export interface Campaign {
  id: string;
  title: string;
  kind: 'work-choice' | 'iban';
  status: 'open' | 'closed';
  expiresAt: string;
  createdAt: string;
  path: string;
  responseCount: number;
  cities?: Array<{ ar: string; en: string }>;
}

export interface SubmissionHistory {
  version: number;
  status: string;
  workSystem?: string;
  iban?: string;
  bank?: string;
  submittedAt: string;
  reviewNote?: string;
  reviewedBy?: string;
}

export interface Submission {
  id: string;
  campaignId: string;
  campaignTitle?: string;
  name: string;
  iqama: string;
  phone?: string;
  city?: string;
  workSystem?: string;
  iban?: string;
  bank?: string;
  platforms?: string[];
  status: 'pending' | 'approved' | 'rejected';
  isEdited?: boolean;
  version: number;
  createdAt: string;
  updatedAt?: string;
  reviewNote?: string;
  history?: SubmissionHistory[];
}

export interface CustomFormQuestion {
  id: string;
  label: string;
  type: 'text' | 'choice' | 'file';
  required: boolean;
  options?: string[];
}

export interface CustomForm {
  id: string;
  title: string;
  status: 'draft' | 'published' | 'closed';
  questions: CustomFormQuestion[];
  responsesCount: number;
  createdAt: string;
}

export interface PaginatedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ApiErrorPayload {
  error: string;
  code?: string;
}
