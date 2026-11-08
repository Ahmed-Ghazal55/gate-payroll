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
  titleAr?: string;
  message: string;
  bodyAr?: string;
  category: NotificationCategory;
  unread: boolean;
  createdAt: string;
  link?: string;
  actionText?: string;
  taskKind?: string;
  entityId?: string;
  requiresAction?: boolean;
  actionStatus?: 'open' | 'decided' | 'none';
  decidedBy?: string;
  decidedAt?: string;
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
  /** Absolute link from the server (Payroll:PublicBaseUrl); prefer it over origin + path. */
  publicUrl?: string;
  qrCodeUrl?: string;
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
  monthlyInstallment?: number;
  status: 'active' | 'settled' | 'waived' | 'disputed' | 'pending_approval' | 'cancelled' | string;
  sponsorId?: string;
  creditorSponsorId?: string;
  creditorSponsorNameAr?: string;
  entryMode?: string;
  balanceAsOf?: string;
  approvalRequestId?: string;
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
  kind?: string;
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

// ==========================================
// Pay Framework & 0.14.0 Types (R4)
// ==========================================

export interface PayCatalogApp {
  app: string;
  platform: string;
  receivable: string;
  principal: string | null;
  agent: string | null;
}

export interface PayBandItem {
  min: number;
  max?: number;
  base?: number;
  perOrderRate?: number;
  extraRate?: number;
  fuelCap?: number;
  internet?: number;
  multiplierMetric?: string;
  multiplierDivisor?: number;
}

export interface NinjaLevelConfig {
  maxBelow: number;
  missingOrderRate: number;
}

export interface NinjaCapabilityConfig {
  contractAmount: number;
  contractTarget: number;
  extraOrderRate: number;
  perOrderRate: number;
  levels: NinjaLevelConfig[];
}

export interface NinjaTermsParameters {
  monthDays: 'calendar' | 'standard';
  capabilities: Record<string, NinjaCapabilityConfig>;
  attendance: {
    minRatio: number;
    deductionPercent: number;
  };
  shortDays: {
    deductionPercentOfDailyAmount: number;
  };
  vatPercent: number;
  companyCostAccount?: string;
}

export interface NinjaTermsSeed {
  name: string;
  billingModel: 'ninja_target';
  parameters: NinjaTermsParameters;
}

export interface PaySchemeCategoryCell {
  enabled: boolean;
  dailyCommission?: number;
  monthlyCommission?: number;
  companyPercent?: number;
  prorate?: boolean;
  chargeCompanyVehicle?: boolean;
  bands?: PayBandItem[];
  fallbackRate?: number;
  operatingCost?: number;
  fuelCap?: number;
  internet?: number;
  operatingCostAccount?: string;
}

export interface PayEligibilityRule {
  metric: string;
  operator: string;
  threshold: number;
  effect: {
    type: 'zero' | 'next_band' | 'per_order' | string;
  };
  included?: boolean;
  app?: string;
}

export interface PayGlobalValues {
  standardDays: number;
  februaryDays: number;
  billingCutoff: string;
  timeZone: string;
  roundingDigits: number;
  roundingMode: string;
  vehicleRent: {
    company_car: number;
    company_bike: number;
    private: number;
  };
  accounts: {
    salaryExpense: string;
    vehicleIncomeCar: string;
    vehicleIncomeBike: string;
    outputVat: string;
  };
}

export interface PayConfigData {
  name?: string;
  type?: 'daily_commission' | 'percent_of_gross' | 'target_salary' | 'monthly_commission' | 'percent_monthly_with_vehicle' | string;
  billingModel?: 'per_order' | 'slabs' | 'flat_target' | 'ninja_target' | string;
  accountingRole?: 'principal' | 'agent' | string;
  eligibilitySetId?: string;
  parameters?: {
    categories?: Record<string, PaySchemeCategoryCell>;
    companyCostAccount?: string;
    pricePerOrder?: number;
    perKmRate?: number;
    includedKm?: number;
    maxBillableKm?: number;
    classRates?: Record<string, number>;
    metric?: string;
    bands?: PayBandItem[];
    targetOrders?: number;
    monthlyAmount?: number;
    extraRate?: number;
    shortfallPerOrder?: number;
    prorate?: boolean;
    monthDays?: 'calendar' | 'standard';
    capabilities?: Record<string, NinjaCapabilityConfig>;
    attendance?: { minRatio: number; deductionPercent: number };
    shortDays?: { deductionPercentOfDailyAmount: number };
    vatPercent?: number;
  };
  rules?: PayEligibilityRule[];
  values?: PayGlobalValues;
}

export interface PayCatalog {
  apps: PayCatalogApp[];
  schemeTypes: string[];
  categories: string[];
  operators: string[];
  ninjaMetrics?: string[];
  defaults?: { values: PayGlobalValues };
  defaultsAreDraftOnly?: boolean;
}

export interface PayConfigDoc {
  id: string;
  kind: 'pay_terms' | 'pay_scheme' | 'pay_eligibility' | 'pay_setting' | string;
  app: string;
  period: string;
  code: string;
  version: number;
  status: 'draft' | 'approved' | 'rejected' | string;
  data: PayConfigData;
  createdBy: string;
  createdAt: string;
  approvedBy?: string;
  approvedAt?: string;
  rejectedBy?: string;
  rejectedAt?: string;
  rejectionReason?: string;
  effectiveFrom?: string;
  copiedFromId?: string;
  allowedActions?: string[];
}

export interface PayConfigListResponse {
  items: PayConfigDoc[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PayTermsCreateInput {
  app?: string;
  period?: string;
  code?: string;
  baseVersionId?: string;
  data: {
    name: string;
    billingModel: 'per_order' | 'slabs' | 'flat_target' | 'ninja_target' | string;
    parameters: Record<string, unknown>;
  };
}

export interface PaySchemeCreateInput {
  app?: string;
  period?: string;
  code: string;
  baseVersionId?: string;
  data: {
    name: string;
    type: 'daily_commission' | 'percent_of_gross' | 'target_salary' | 'monthly_commission' | 'percent_monthly_with_vehicle' | string;
    accountingRole: 'principal' | 'agent' | string;
    eligibilitySetId?: string;
    parameters: {
      categories: Record<string, PaySchemeCategoryCell>;
    };
  };
}

export interface PayEligibilityCreateInput {
  app?: string;
  period?: string;
  code?: string;
  baseVersionId?: string;
  data: {
    name: string;
    rules: PayEligibilityRule[];
  };
}

export interface PayGlobalCreateInput {
  effectiveFrom: string;
  fromDefaults?: boolean;
  baseVersionId?: string;
  data?: {
    values: PayGlobalValues;
  };
}

export interface PayCopyMonthResponse {
  drafts: PayConfigDoc[];
  created: number;
  unchanged: number;
}

export interface PayAssignment {
  id: string;
  rider: string;
  app: string;
  schemeId: string;
  period: string;
  companyId?: string;
  status: string;
  choiceApprovedAt: string;
  source?: {
    kind: string;
    formId: string;
    responseId: string;
    version: number;
    fieldId: string;
    approvedBy: string;
    fingerprint?: string;
  };
}

export interface PayAssignmentPreviewResult {
  rows: PayAssignment[];
  canCommit: boolean;
  errors: Array<{ code: string }>;
  fingerprint: string;
  counts: {
    new: number;
    unchanged: number;
    errors: number;
    lateIgnored: number;
    unapprovedIgnored: number;
  };
}

export interface PayCycle {
  id: string;
  period: string;
  companyId: string;
  sponsorId?: string;
  app?: string;
  stage: number;
  status: 'draft' | 'review' | 'needs_decision' | 'approved' | 'cancelled' | 'reversed' | string;
  invoices?: any[];
  riders?: Array<{
    rider: string;
    net: number;
    carried?: number;
    closing?: number;
    status: string;
    review?: string;
    gross?: number;
    commission?: number;
    afterCommission?: number;
    phase1?: number;
    preliminaryDue?: number;
    phase2?: number;
    phase3?: number;
  }>;
  deductions?: any[];
  adjustmentAccounts?: Record<string, string>;
  createdBy?: string;
  createdAt?: string;
  advancedBy?: string;
  approvedBy?: string;
  approvedAt?: string;
  allowedActions?: string[];
  journalEntryId?: string;
  history?: Array<{
    stage: number;
    advancedBy?: string;
    at?: string;
  }>;
}

export interface PayCycleLine {
  id: string;
  cycleId: string;
  rider: string;
  app: string;
  period: string;
  category: string;
  schemeId: string;
  schemeName?: string;
  status: 'ready' | 'needs_decision' | string;
  needsDecisionReason?: string;
  orders: number;
  baseGross: number;
  platformGross?: number;
  gross?: number;
  incentives?: number;
  entitlement?: number;
  vehicleCharge?: number;
  allocatedVehicleCharge?: number;
  accountDeduction?: number;
  net?: number;
  netPay?: number;
  lines?: Array<{
    label: string;
    formula: string;
    amount: number;
    category: string;
    inputs?: any;
    affectsNet?: boolean;
  }>;
}

export interface LegacyPayrollPreviewRow {
  iqama: string;
  orders: number;
  policyName: string;
  openingBalance: number;
  periodMovement: number;
  closingBalance: number;
  blockers: string[];
  fingerprint: string;
  breakdown?: any;
}

export interface LegacyPayrollRun {
  id: string;
  iqama: string;
  period: string;
  sponsorId: string;
  approvedAt: string;
  approvedBy: string;
  result: {
    orders: number;
    policyName: string;
    openingBalance: number;
    periodMovement: number;
    closingBalance: number;
    lines?: any[];
  };
}

export interface MonthlyStatementCard {
  app: string;
  platform: string;
  companyId: string;
  schemeName?: string;
  schemeType?: string;
  gross: number;
  earnedGross: number;
  vehicleCharge: number;
  incentives: number;
  companyCommission?: number;
  netBeforePersonal?: number;
  explain?: Array<{
    label: string;
    formula: string;
    amount: number;
    category: string;
  }>;
}

export interface MonthlyRiderStatement {
  header: {
    rider: string;
    name: string;
    period: string;
    status: string;
    ibanMasked: string;
    cycles: any[];
  };
  source: 'pay_cycle' | 'legacy_run' | 'none';
  status: 'approved' | 'preview' | 'no_approved_cycle';
  cards: MonthlyStatementCard[];
  deductions: Array<{
    category: string;
    amount: number;
    source: string;
    type?: string;
    description?: string;
    phase?: number;
    order?: number;
    labelAr?: string;
  }>;
  phases?: {
    gross: number;
    commission: number;
    afterCommission: number;
    phase1: number;
    preliminaryDue: number;
    phase2: number;
    phase3: number;
  };
  net: number;
  carried: number;
  closing: number;
  balanceBasis: 'cycle_snapshot' | 'approved_legacy_snapshot';
  approvedOnly: boolean;
}

// ========================================================
// R5 ADVANCES & ORG DIRECTORY TYPES (BR §25)
// ========================================================

export interface AdvanceRequestDecision {
  actor: string;
  action: 'submitted' | 'approved' | 'rejected' | 'proposed' | 'accepted_proposal' | 'rejected_proposal' | 'paid';
  note?: string;
  at: string;
  fromStatus?: string;
  snapshot?: {
    department?: string;
    rank?: string;
    via?: string;
    delegationId?: string;
  };
  monthlyInstallment?: number;
  coverageReason?: string;
  proposal?: {
    type?: 'cash' | 'bank';
    amount?: number;
    vaultCode?: string;
    proposedBy?: string;
  };
  reference?: string;
}

export interface AdvanceRequest {
  id: string;
  riderIqama: string;
  riderName: string;
  companyId: string;
  type: 'cash' | 'bank' | 'internet';
  fulfilment?: 'cash' | 'bank' | 'recharge_card';
  networkCode?: string;
  packageCode?: string;
  amount: number;
  vaultCode: string;
  reason: string;
  status: 'under_review' | 'approved' | 'rejected' | 'change_proposed' | 'cancelled' | 'paid';
  revision: number;
  requestedBy: string;
  createdAt: string;
  updatedAt?: string;
  approvedBy?: string;
  approvedAt?: string;
  paidBy?: string;
  paidAt?: string;
  payoutReference?: string;
  monthlyInstallment?: number;
  coverageAtApproval?: boolean;
  forecastAtApproval?: number;
  coverageReason?: string;
  debtId?: string;
  journalEntryId?: string;
  proposal?: {
    type: 'cash' | 'bank' | 'internet';
    amount: number;
    vaultCode: string;
    proposedBy: string;
  };
  decisions: AdvanceRequestDecision[];
}

export interface AdvanceRequestSummary {
  requestId: string;
  period: string;
  rider: {
    iqama: string;
    name: string;
    companyId: string;
  };
  ordersThisMonth: number;
  statement: {
    available: boolean;
    cards?: any[];
    net?: number;
    reason?: string;
  };
  openDebtBalance: number;
  openDebts: number;
  plannedInstallments: number;
  lastPayrollSettlementAt: string;
  advancesSinceLastSettlement: Array<{
    debtId: string;
    category: string;
    amount: number;
    remaining: number;
    status: string;
    createdAt: string;
  }>;
  forecastNet: number | null;
  amount: number;
  covered: boolean;
  coverageKnown: boolean;
  vaults: Array<{
    type: 'cash' | 'bank';
    code: string;
    balance: number;
    selected: boolean;
  }>;
}

export interface OrgCatalog {
  departments: string[];
  ranks: Array<{ rank: string; level: number }>;
  operations: string[];
}

export interface OrgMembership {
  id: string;
  userId: string;
  department: string;
  rank: string;
  validFrom?: string;
  validTo?: string;
  createdAt?: string;
}

export interface OrgDelegation {
  id: string;
  fromUser: string;
  toUser: string;
  operation: string;
  validFrom?: string;
  until: string;
  reason?: string;
  createdAt?: string;
}

export interface OrgSupervision {
  id: string;
  supervisorUserId: string;
  riderIqama: string;
  riderName?: string;
  validFrom?: string;
  validTo?: string;
  createdAt?: string;
}

export interface OrgRouting {
  id: string;
  operation: string;
  department: string;
  minRank: string;
  users: string[];
  validFrom?: string;
  validTo?: string;
  createdAt?: string;
}

export interface OrgResponsibleUser {
  userId: string;
  via: 'assignment' | 'delegation';
  delegationId?: string;
  department: string;
  rank: string;
}

export interface ErpSyncCount {
  kind: string;
  source: string;
  new: number;
  changed: number;
  unchanged: number;
  rejected: number;
}

export interface ErpSyncJobStatus {
  jobId: string;
  status: 'queued' | 'running' | 'completed' | 'refused' | 'failed';
  mode: 'dry_run' | 'commit';
  queuedAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  refusalCode?: string | null;
  counts: ErpSyncCount[];
}

export interface ErpSyncRun {
  id: string;
  startedAt: string;
  finishedAt: string;
  status: 'completed' | 'refused' | 'failed';
  mode: 'dry_run' | 'commit';
  refusalCode?: string | null;
  counts: ErpSyncCount[];
}

// =========================================================================
// R6b / K4: Payroll Review Grid (BR §32)
// =========================================================================
export interface PayReviewItem {
  key: string;
  phase: number;
  order: number;
  labelAr: string;
  amount: number;
  editable: boolean;
  max: number | null;
  source: string;
}

export interface PayReviewEdit {
  key: string;
  from: number;
  to: number;
  reason: string;
  by: string;
  at: string;
}

export interface PayReviewRow {
  rider: string;
  name: string;
  status: 'ready' | 'needs_decision';
  review: 'pending' | 'approved';
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  gross: number;
  commission: number;
  afterCommission: number;
  phase1: number;
  preliminaryDue: number;
  phase2: number;
  phase3: number;
  net: number;
  items: PayReviewItem[];
  edits: PayReviewEdit[];
}

export interface PayReviewTotals {
  riders: number;
  approved: number;
  pending: number;
  needsDecision: number;
  net: number;
}

export interface PayReviewResponse {
  cycle: {
    id: string;
    period: string;
    companyId: string;
    stage: number;
    status: string;
  };
  totals: PayReviewTotals;
  rows: PayReviewRow[];
}

// =========================================================================
// R6b / K1: Config Lists (BR §27)
// =========================================================================
export interface ConfigFieldDef {
  key: string;
  labelAr: string;
  type: 'text' | 'number' | 'select' | 'account' | 'bool';
  required: boolean;
  options?: string[];
}

export interface ConfigListItem {
  code: string;
  nameAr: string;
  active: boolean;
  values?: Record<string, any>;
  status?: string;
  version?: number;
}

export interface ConfigListGroup {
  group: string;
  labelAr: string;
  financial: boolean;
  fields: ConfigFieldDef[];
  items: ConfigListItem[];
}

// =========================================================================
// R6b / C6: Manual Journals (BR §28)
// =========================================================================
export interface ManualJournalLine {
  accountCode: string;
  accountNameAr?: string;
  debit: number;
  credit: number;
  note?: string;
}

export interface ManualJournal {
  id: string;
  companyId?: string;
  companyName?: string;
  date?: string;
  reference?: string;
  description?: string;
  status: string;
  lines?: ManualJournalLine[];
  maker?: string;
  createdBy?: string;
  createdAt?: string;
  approvedBy?: string;
  approvedAt?: string;
  rejectedBy?: string;
  rejectedAt?: string;
  rejectionReason?: string;
  reversalOfId?: string;
  reversalReason?: string;
  odooVoucherNo?: string;
  attachmentName?: string;
  hasAttachment?: boolean;
  totalDebit?: number;
  totalCredit?: number;
  allowedActions?: string[];
  journalEntryId?: string;
}

// =========================================================================
// R6b / N2/N3: Ninja Invoice Reconciliation (BR §26, §31)
// =========================================================================
export interface NinjaReconciliationDiff {
  field: string;
  invoice: number;
  engine: number;
  difference: number;
}

export interface NinjaReconciliationLine {
  row: number;
  captainId: string;
  status?: string;
  reason?: string;
  reasons?: string[];
  diffs?: NinjaReconciliationDiff[];
}

export interface NinjaReconciliationAdjustment {
  row: number;
  captainId: string;
  levelType: string;
  finalAmount: number;
  totalCost: number;
}

export interface NinjaInvoiceResult {
  fileName: string;
  supplier?: string;
  matched: number;
  different: number;
  captainsCount?: number;
  adjustments: NinjaReconciliationAdjustment[];
  totals: Record<string, { invoice: number; engine: number; difference: number }>;
  lines: NinjaReconciliationLine[];
}

export interface NinjaReconcileResponse {
  period: string;
  termsSource: string;
  invoices: NinjaInvoiceResult[];
}

// =========================================================================
// R6c / K6: Import Center Catalog & Steps
// =========================================================================
export interface ImportCatalogColumn {
  name: string;
  required: boolean;
  type: 'text' | 'number' | 'choice';
  identity?: boolean;
  choices?: string[] | null;
}

export type ImportStepUpload = 'xlsx' | 'json-rows' | 'platform-file' | 'erp';
export type ImportStepStatus = 'ready' | 'template_only' | 'planned' | 'erp';
export type ImportStepGroup = 'setup' | 'opening' | 'invoices' | 'operations';

export interface ImportCatalogStep {
  order: number;
  key: string;
  group: ImportStepGroup;
  labelAr: string;
  purposeAr: string;
  dependsOn: string[];
  permission: string;
  templateUrl: string | null;
  previewUrl: string | null;
  commitUrl: string | null;
  upload: ImportStepUpload;
  status: ImportStepStatus;
  noteAr?: string | null;
  columns: ImportCatalogColumn[];
}

export interface ImportPreviewRowIssue {
  row?: number;
  field?: string;
  error?: string;
  message?: string;
}

export interface ImportPreviewResult {
  rows?: any[];
  errors?: any[];
  fingerprint?: string;
  counts?: Record<string, number>;
  message?: string;
  batchId?: string;
  count?: number;
}



