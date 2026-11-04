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

export interface IsicActivity {
  isic: string;
  nameAr: string;
}

export interface Company {
  id: string;
  sponsorId?: string;
  code: string;
  nameAr: string;
  nameEn?: string;
  shortNameAr?: string;
  unifiedNumber?: string;
  commercialReg?: string;
  crNumber?: string;
  crType?: string;
  entityType?: string;
  characteristic?: string;
  city?: string;
  address?: string;
  capitalSar?: number;
  crVersion?: number;
  issueDate?: string;
  issueDateHijri?: string;
  annualConfirmation?: string;
  annualConfirmationHijri?: string;
  activities?: IsicActivity[];
  activitiesIncomplete?: boolean;
  sponsoredRidersCount?: number;
  status: 'active' | 'inactive' | 'archived';
  createdAt?: string;
  updatedAt?: string;
}

export interface CommercialRegistration {
  id: string;
  companyId: string;
  company?: string;
  number: string;
  city?: string;
  issueDate?: string;
  issueDateHijri?: string;
  annualConfirmation?: string;
  annualConfirmationHijri?: string;
  expiryDate?: string;
  crVersion?: number;
  status: 'active' | 'inactive' | 'expired' | 'archived';
  createdAt?: string;
}

export interface CompanyDocument {
  id: string;
  companyId: string;
  type: string;
  number?: string;
  expiryDate?: string;
  fileName?: string;
  fileRelativePath?: string;
  file?: string;
  contentType?: string;
  status: 'active' | 'archived';
  createdAt?: string;
}

export interface ExpiringRecord {
  id: string;
  companyId: string;
  kind: 'commercial_registration' | 'company_document';
  number?: string;
  type?: string;
  annualConfirmation?: string;
  expiryDate?: string;
  status: string;
}

export interface CompanyImportPlan {
  rows: any[];
  fingerprint: string;
  counts: {
    company: { new: number; changed: number; unchanged: number };
    commercial_registration: { new: number; changed: number; unchanged: number };
  };
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

// ==========================================
// HungerStation Platform Types (R3 Part A)
// ==========================================

export interface HsInvoice {
  id: string;
  companyId: string;
  period: string;
  platform: string;
  status: 'draft' | 'pending_allocation' | 'disputed' | 'approved' | 'posted';
  settlementHold?: boolean;
  approvalStatus?: string;
  approvedBy?: string;
  approvedAt?: string;
  createdBy?: string;
  createdAt?: string;
  journalEntryId?: string;
  reversalJournalEntryId?: string;
  totalAmount?: number;
  vat?: number;
  linesCount?: number;
  disputesCount?: number;
  allocationsCount?: number;
  flags?: string[];
  blockers?: string[];
}

export interface HsDispute {
  id: string;
  invoiceId: string;
  lineId: string;
  companyId: string;
  period: string;
  platform: string;
  category: string;
  amount: number;
  reason: string;
  status: 'open' | 'escalated' | 'resolved';
  outcome?: 'platform_credit' | 'platform_rejected';
  platformReference?: string;
  resolutionReason?: string;
  resolvedBy?: string;
  resolvedAt?: string;
  createdBy: string;
  createdAt: string;
  accountingReconciliation?: string;
}

export interface HsAllocationRiderShare {
  riderId: string;
  amount: number;
}

export interface HsAllocation {
  id: string;
  invoiceId: string;
  lineId: string;
  companyId: string;
  period: string;
  amount: number;
  status: 'pending_allocation' | 'pending' | 'approved';
  companyAmount?: number;
  riderAmount?: number;
  splitPercent?: number;
  riderShares?: HsAllocationRiderShare[];
  createdBy?: string;
  requestedBy?: string;
  createdAt?: string;
  approvedBy?: string;
  approvedAt?: string;
  revision?: number;
}

export interface HsFact {
  id: string;
  companyId: string;
  period?: string;
  kind: 'hs_wallet_event' | 'hs_remittance' | 'hs_remittance_allocation' | 'hs_handover_review' | 'hs_month_close';
  sourceFacts?: Record<string, any>;
  status?: string;
  sourceEventId?: string;
  linkedSourceEventId?: string;
  isReversal?: boolean;
  reversalOf?: string;
}

export interface HsJournalLine {
  accountCode: string;
  accountNameAr?: string;
  debit: number;
  credit: number;
  description: string;
  riderId?: string;
  isTargetScheme?: boolean;
}

export interface HsProposedJournal {
  invoiceId: string;
  companyId: string;
  period: string;
  lines: HsJournalLine[];
  blockers: string[];
  readyForApproval: boolean;
  postingEnabled: boolean;
  postingBlockedByDispute: boolean;
  postingAllowed: boolean;
  postingBlockers: string[];
  disputedAmount?: number;
  fingerprint?: string;
}

export interface HsImportFile {
  fileName: string;
  dataBase64: string;
}

export interface HsImportPreviewResult {
  rows: any[];
  canCommit: boolean;
  fingerprint: string;
  errors: Array<{ fileIndex: number; sheet: string; row: number; code: string; message?: string }>;
  counts: Record<string, number>;
  flags?: string[];
  blockers?: string[];
}

// ==========================================
// Treasury & Banks Types (R3 Part B1)
// ==========================================

export interface FinanceAccount {
  code: string;
  nameAr: string;
  accountType: string;
  naturalSide: 'Debit' | 'Credit';
  naturalSideAr: string;
  parentCode?: string;
  isGroup: boolean;
  isVault: boolean;
  vaultKind?: 'MainInternal' | 'CashBox' | 'Bank' | 'Custody' | string;
  vaultPurpose?: string;
  allowedDirections?: string;
  allowedDirectionsAr?: string;
  allowedSectors?: string[];
  debit: number;
  credit: number;
  netBalance: number;
}

export interface FinanceJournalLine {
  accountCode: string;
  accountNameAr?: string;
  debit: number;
  credit: number;
  description?: string;
  sponsorId?: string;
  iqama?: string;
  reference?: string;
}

export interface FinanceJournal {
  id: string;
  journalNumber?: string;
  transactionType?: string;
  sourceKind?: string;
  sourceRef?: string;
  period?: string;
  date?: string;
  entryDate?: string;
  description: string;
  sponsorId?: string;
  originSponsorId?: string;
  originSponsorNameAr?: string;
  creditorSponsorId?: string;
  creditorSponsorNameAr?: string;
  iqama?: string;
  debtId?: string;
  totalDebit?: number;
  totalCredit?: number;
  chart?: string;
  lines: FinanceJournalLine[];
  reversesJournalId?: string;
  reversalOf?: string;
  reversedBy?: string;
  createdAt: string;
  createdBy: string;
}

export interface BankPayrollFileRow {
  riderIqama: string;
  riderName: string;
  iban: string;
  bankName: string;
  amount: number;
  status: 'valid' | 'invalid_iban' | 'missing_data' | 'zero_balance';
  validationMessage?: string;
}

export interface BankPayrollFilePreview {
  period: string;
  companyId?: string;
  totalAmount: number;
  validRecordsCount: number;
  excludedCount: number;
  records: BankPayrollFileRow[];
  canExport: boolean;
}

// ==========================================
// Debts & Advances Types (R3 Part B2)
// ==========================================

export interface DebtRecord {
  id: string;
  iqama: string;
  riderName?: string;
  category: 'advance' | 'traffic_fine' | 'equipment' | 'hr_cost' | 'loan' | string;
  categoryAr?: string;
  originalAmount: number;
  remainingAmount: number;
  monthlyDeduction?: number;
  status: 'active' | 'settled' | 'waived' | 'disputed';
  sponsorId?: string;
  createdAt: string;
  notes?: string;
  installments?: Array<{ period: string; amount: number; settled: boolean }>;
}

export interface ApprovalRequest {
  id: string;
  fromDepartment: string;
  targetDepartment: string;
  requestType: string;
  requestTypeAr?: string;
  status: 'pending' | 'approved' | 'rejected' | 'suspended';
  amount?: number;
  iqama?: string;
  riderName?: string;
  reason: string;
  requestedBy: string;
  requestedAt: string;
  decisionBy?: string;
  decisionAt?: string;
  decisionNote?: string;
}

export interface PromissoryNote {
  id: string;
  noteNumber: string;
  iqama: string;
  riderName?: string;
  beneficiarySponsorId: string;
  amount: number;
  issueDate: string;
  dueDate?: string;
  status: 'active' | 'settled' | 'cancelled';
  fileAttachmentUrl?: string;
  createdAt: string;
}

export interface RiderRecord {
  id: string;
  iqama: string;
  name: string;
  phone?: string;
  nationality?: string;
  sponsor?: string;
  sponsorId?: string;
  city?: string;
  engagementType?: 'SponsoredInternal' | 'External' | string;
  operationalRole?: 'Car' | 'Motorcycle' | string;
  drivingLicense?: string;
  platforms?: string;
  iban?: string;
  status: string;
  policyId?: string;
  createdAt?: string;
}

export interface RiderDocumentChecklistItem {
  documentTypeId: string;
  code: string;
  nameAr: string;
  requiresExpiry: boolean;
  isUploaded: boolean;
  documentId: string;
  documentNumber: string;
  issueDate: string;
  expiryDate: string;
  fileName: string;
  storagePath: string;
  status: string;
}

export interface RiderProfile360 {
  rider: RiderRecord;
  documentChecklist: RiderDocumentChecklistItem[];
  documentsCount: number;
  activeDebtsCount: number;
  totalRemainingDebt: number;
  workRecordsCount: number;
  totalOrders: number;
  submissions: any[];
}

export interface RidersDataQuality {
  totalRiders: number;
  localBaselineAvailable: boolean;
  missingPhoneCount: number;
  missingCityCount: number;
  missingSponsorCount: number;
  missingNationalityCount: number;
  missingPolicyCount: number;
  missingIbanCount: number;
  needsReviewCount: number;
}

export interface RidersBaselineSyncResult {
  fingerprint?: string;
  sourceFingerprint?: string;
  total?: number;
  excludedAdministrative?: number;
  addCount?: number;
  updateCount?: number;
  existingCount?: number;
  conflicts?: any[];
  errors?: any[];
  rows?: any[];
  count?: number;
  updatedCount?: number;
  alreadyImported?: boolean;
  batchId?: string;
}

export interface RiderStatementEntry {
  date: string;
  entryType: string;
  description: string;
  debit: number;
  credit: number;
  balance: number;
  reference?: string;
}

export interface RiderStatement {
  iqama: string;
  riderName: string;
  period: { from: string; to: string };
  openingBalance: number;
  closingBalance: number;
  entries: RiderStatementEntry[];
}

// ==========================================
// Accounting & Reports Types (R3 Part B3)
// ==========================================

export interface TargetCoaAccount {
  code: string;
  nameAr: string;
  level: number;
  parent?: string | null;
  parentCode?: string;
  posting?: boolean;
  isPosting?: boolean;
  status?: string;
  statusNote?: string | null;
  isActive?: boolean;
  nature?: string;
  type?: string;
  company?: string;
  subledger?: string;
  source?: string;
  sourceNote?: string;
  sourceType?: 'excel2024' | 'modified' | 'new' | string;
  balance?: number;
}

export interface TargetCoaSummary {
  total: number;
  activePosting: number;
  sourceBreakdown: {
    excel2024: number;
    modified: number;
    newAccounts: number;
  };
}

export interface TargetCoaResponse {
  schemaVersion?: number;
  version?: string;
  source?: string;
  accounts: TargetCoaAccount[];
  summary?: TargetCoaSummary;
  mapping?: any[];
}

export interface TrialBalanceCompanyRow {
  code: string;
  nameAr?: string;
  name?: string;
  parent?: string | null;
  posting?: boolean;
  level?: number;
  openingDebit?: number;
  openingCredit?: number;
  periodDebit?: number;
  periodCredit?: number;
  closingDebit?: number;
  closingCredit?: number;
  [key: string]: any;
}

export interface TrialBalanceCompany {
  sponsorId: string;
  companyCode: string;
  rows: TrialBalanceCompanyRow[];
  unmapped?: any[];
  totals?: {
    openingDebit?: number;
    openingCredit?: number;
    periodDebit?: number;
    periodCredit?: number;
    closingDebit?: number;
    closingCredit?: number;
  };
  mappedTotals?: any;
  unmappedTotals?: any;
  mappingComplete?: boolean;
  scopeLimited?: boolean;
  excludedLineCount?: number;
  isComplete?: boolean;
  isBalanced?: boolean;
}

export interface TrialBalanceItem {
  accountCode: string;
  accountNameAr: string;
  parentCode?: string;
  level: number;
  isGroup: boolean;
  openingDebit: number;
  openingCredit: number;
  movementDebit: number;
  movementCredit: number;
  closingDebit: number;
  closingCredit: number;
  netBalance: number;
}

export interface TrialBalanceResponse {
  version?: string;
  view?: string;
  from?: string;
  to?: string;
  companies?: TrialBalanceCompany[];
  period?: { from?: string; to?: string };
  sponsorId?: string;
  items?: TrialBalanceItem[];
  totals?: {
    movementDebit?: number;
    movementCredit?: number;
    closingDebit?: number;
    closingCredit?: number;
  };
}

export interface IncomeStatementCompany {
  sponsorId: string;
  companyCode: string;
  revenueGroups?: Array<{
    code: string;
    nameAr: string;
    parent?: string | null;
    posting?: boolean;
    amount: number;
  }>;
  expenseGroups?: Array<{
    code: string;
    nameAr: string;
    parent?: string | null;
    posting?: boolean;
    amount: number;
  }>;
  revenue?: number;
  expense?: number;
  net?: number;
  netIsComplete?: boolean;
  scopeLimited?: boolean;
  excludedLineCount?: number;
  unmapped?: any[];
}

export interface IncomeStatementGroup {
  code: string;
  nameAr: string;
  amount: number;
  subAccounts?: Array<{ code: string; nameAr: string; amount: number }>;
}

export interface IncomeStatementResponse {
  version?: string;
  view?: string;
  from?: string;
  to?: string;
  companies?: IncomeStatementCompany[];
  period?: { from?: string; to?: string };
  sponsorId?: string;
  revenues?: IncomeStatementGroup[];
  totalRevenues?: number;
  expenses?: IncomeStatementGroup[];
  totalExpenses?: number;
  netIncome?: number;
}
