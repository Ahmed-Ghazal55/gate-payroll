using Microsoft.OpenApi;
namespace Payroll;

// Schemas describe the document-store wire format; stored field names stay unchanged.
public static class ApiContracts
{
    static OpenApiSchema S()=>new(){Type=JsonSchemaType.String};
    static OpenApiSchema N()=>new(){Type=JsonSchemaType.Number};
    static OpenApiSchema B()=>new(){Type=JsonSchemaType.Boolean};
    static OpenApiSchema A(IOpenApiSchema item)=>new(){Type=JsonSchemaType.Array,Items=item};
    static OpenApiSchema O(Dictionary<string,IOpenApiSchema> properties,params string[] required)=>new(){Type=JsonSchemaType.Object,Properties=properties,Required=new HashSet<string>(required),AdditionalPropertiesAllowed=true};
    static OpenApiSchema Free()=>O([]);
    static OpenApiSchema Page(IOpenApiSchema item)=>O(new(){["items"]=A(item),["total"]=N(),["page"]=N(),["pageSize"]=N()},"items","total","page","pageSize");
    static OpenApiSchema OpsRow(params (string Key,OpenApiSchema Schema)[] fields)
    {
        var props=new Dictionary<string,IOpenApiSchema>{["id"]=S()};
        foreach(var (key,schema) in fields)props[key]=schema;
        return O(props,"id");
    }
    static OpenApiSchema OperationsItem()
    {
        // M8: one schema per Operations.FieldMap kind (plus id); keyed OneOf for contract regen.
        var schemas=new List<IOpenApiSchema>();
        foreach(var (kind,fields) in Operations.FieldMap.OrderBy(kv=>kv.Key,StringComparer.Ordinal))
        {
            var props=fields.Select(f=>(f,Guess(f))).ToArray();
            if(kind=="platform_account")props=props.Append(("assignments",A(AssignmentRow()))).Append(("relationship",S())).ToArray();
            schemas.Add(OpsRow(props));
        }
        schemas.Add(Free());
        return new(){AnyOf=schemas};
    }
    /// <summary>Same fields as <c>Operations.Assignment</c>; <c>relationship</c> is set by rider-account reads.</summary>
    static OpenApiSchema AssignmentRow()=>O(new(){["riderId"]=S(),["RawEffectiveFrom"]=S(),["RawEffectiveTo"]=S(),["EffectiveFrom"]=S(),["EffectiveTo"]=S(),["Status"]=S(),["IsRealRider"]=B()});
    static OpenApiSchema Guess(string field)=>field is "orders" or "hours" or "amount" or "vat" or "modelYear" or "currentOdometer" or "startOdometer" or "endOdometer" or "quantity" or "returnedQuantity" or "totalCost" or "collectedAmount" or "deductionsAmount" or "deliveryFees" or "cashCollected" or "totalValidOffers" or "acceptedOffers" or "declinedOffers" or "missedOffers" or "gross" or "companyAmount" or "riderAmount" or "rate" or "count" or "vehicleType" or "fuelType" or "ownershipType"?N():field is "isRealRider" or "frozen"?B():S();
    static OpenApiSchema OperationsPage()=>O(new(){["items"]=A(OperationsItem()),["total"]=N(),["page"]=N(),["pageSize"]=N(),["source"]=S(),["asOf"]=S(),["allowedActions"]=A(S())},"items","total","page","pageSize","source","asOf","allowedActions");
    static OpenApiSchema WorkforcePerson()=>O(new(){["id"]=S(),["iqama"]=S(),["name"]=S(),["phone"]=S(),["nationality"]=S(),["iban"]=S(),["ibanAlert"]=S(),["ibanSource"]=S(),["engagementType"]=S(),["status"]=S(),["sponsorNameAr"]=S(),["sponsor"]=S(),["platform"]=S(),["platforms"]=S(),["platformWorkId"]=S(),["city"]=S(),["workType"]=S(),["mudadSalary"]=S(),["jobTitle"]=S(),["hasCompanyVehicle"]=B(),["companyVehicleType"]=S(),["operationalRole"]=S(),["housing"]=S(),["allowedActions"]=A(S())},"id","iqama","name");
    static OpenApiSchema WorkforceCompanyCard()=>O(new(){["sponsorUnified"]=S(),["sponsorNameAr"]=S(),["totalCount"]=N()},"sponsorUnified","sponsorNameAr","totalCount");
    static OpenApiSchema WorkforceSummaryBody()=>O(new(){["totalCount"]=N(),["sponsoredCount"]=N(),["sponsoredRiders"]=N(),["sponsoredAdmins"]=N(),["sponsoredMissingIban"]=N(),["byCompany"]=A(WorkforceCompanyCard())},"totalCount","sponsoredCount","sponsoredRiders","sponsoredAdmins","sponsoredMissingIban");
    static OpenApiSchema WorkforcePage()=>O(new(){["items"]=A(WorkforcePerson()),["total"]=N(),["page"]=N(),["pageSize"]=N(),["summary"]=WorkforceSummaryBody(),["source"]=S(),["asOf"]=S(),["allowedActions"]=A(S())},"items","total","page","pageSize","source","asOf","allowedActions");
    static OpenApiSchema WorkforceSummary()=>O(new(){["summary"]=WorkforceSummaryBody(),["source"]=S(),["asOf"]=S(),["allowedActions"]=A(S())},"summary","source","asOf","allowedActions");
    static OpenApiSchema WorkforceListEnvelope()=>O(new(){["items"]=A(Free()),["total"]=N(),["page"]=N(),["pageSize"]=N(),["source"]=S(),["asOf"]=S(),["allowedActions"]=A(S())},"items","total","source","asOf","allowedActions");
    static OpenApiSchema Workforce360()=>O(new(){["person"]=WorkforcePerson(),["workRecord3Months"]=A(Free()),["financialEvaluation"]=Free(),["iqamaImage"]=Free(),["ibanImage"]=Free(),["promissoryNotes"]=A(Free()),["platformAccounts"]=A(Free()),["legalCases"]=A(Free()),["financialPosition"]=Free(),["source"]=S(),["asOf"]=S(),["allowedActions"]=A(S())},"person","workRecord3Months","financialEvaluation","iqamaImage","promissoryNotes","platformAccounts","financialPosition","source","asOf","allowedActions");

    public static OpenApiSchema User()=>O(new(){["id"]=S(),["username"]=S(),["role"]=S(),["roleNameAr"]=S(),["roleSummaryAr"]=S(),["department"]=S(),["departmentNameAr"]=S(),["rankLevel"]=N(),["status"]=S(),["permissions"]=A(S()),["mustChangePassword"]=B(),["customPermissions"]=A(S()),["deniedPermissions"]=A(S()),["financialScope"]=Free(),["createdAt"]=S(),["lastLoginAt"]=S(),["approvalExceptionUntil"]=S(),["approvalExceptionReason"]=S(),["allowedActions"]=A(S())},"id","username","role","status","permissions","approvalExceptionUntil","approvalExceptionReason","allowedActions");
    public static OpenApiSchema Debt()=>O(new(){["id"]=S(),["iqama"]=S(),["category"]=S(),["categoryAr"]=S(),["status"]=S(),["totalAmount"]=N(),["remainingBalance"]=N(),["monthlyInstallment"]=N(),["paidAmount"]=N(),["creditorSponsorId"]=S(),["allowedActions"]=A(S()),["approvalBlockers"]=A(S())},"id","totalAmount","remainingBalance","status");
    static OpenApiSchema DeductionAllocation()=>O(new(){["bearer"]=S(),["riderId"]=S(),["amount"]=N(),["expenseAccount"]=S()},"bearer","amount");
    static OpenApiSchema InvoiceDeduction()=>O(new(){["category"]=S(),["amount"]=N(),["level"]=S(),["expenseAccount"]=S(),["riderId"]=S(),["migrationStatus"]=S(),["allocations"]=A(DeductionAllocation()),["proposal"]=A(DeductionAllocation())},"category","amount");
    public static OpenApiSchema InvoiceLine()=>O(new(){["id"]=S(),["invoiceId"]=S(),["sourceRow"]=N(),["platformAccountId"]=S(),["captainId"]=S(),["lineType"]=S(),["baseGross"]=N(),["incentives"]=N(),["deductionAmount"]=N(),["orders"]=N(),["vat"]=N(),["expected"]=N(),["invoiced"]=N(),["difference"]=N(),["reason"]=S(),["status"]=S(),["deductions"]=A(InvoiceDeduction())},"id","invoiceId","sourceRow","baseGross","incentives","deductionAmount","orders","vat","invoiced","status");
    public static OpenApiSchema Invoice()=>O(new(){["id"]=S(),["companyId"]=S(),["platform"]=S(),["period"]=S(),["reference"]=S(),["amountExclVat"]=N(),["deductionsTotal"]=N(),["vat"]=N(),["approvalStatus"]=S(),["status"]=S(),["allowedActions"]=A(S()),["approvalBlockers"]=A(S()),["journalEntryId"]=S(),["createdBy"]=S(),["approvedBy"]=S(),["accountingStatus"]=S(),["reversalJournalEntryId"]=S(),["disputes"]=A(O(new(){["id"]=S(),["status"]=S(),["reason"]=S(),["createdBy"]=S(),["resolvedBy"]=S()},"id","status","reason","createdBy")),["accountIds"]=new OpenApiSchema{Type=JsonSchemaType.Array|JsonSchemaType.Null,Items=S()},["lines"]=A(InvoiceLine())},"id","companyId","period","amountExclVat","deductionsTotal","vat","status","approvalStatus","allowedActions");
    /// <summary>Cycle status includes partially_approved (stage-7 partial salary approval).</summary>
    public static OpenApiSchema Cycle()=>O(new(){["approvalResult"]=O(new(){["approved"]=N(),["skipped"]=N(),["skippedRows"]=A(O(new(){["rider"]=S(),["reason"]=S()},"rider","reason"))},"approved","skipped","skippedRows"),["id"]=S(),["companyId"]=S(),["period"]=S(),["app"]=S(),["stage"]=N(),["status"]=S(),["allowedActions"]=A(S()),["approvalBlockers"]=A(S()),["debtDeductionStatus"]=S(),["invoices"]=A(Review()),["riders"]=A(O(new(){["rider"]=S(),["net"]=N(),["carried"]=N(),["closing"]=N(),["status"]=S(),["review"]=S(),["salaryNet"]=N(),["carryAmount"]=N(),["carryDebtId"]=S(),["salaryApprovedAt"]=S(),["debtDeductionStatus"]=S(),["blockers"]=A(S())},"rider","net","status")),["journalEntryId"]=S(),["history"]=A(Free()),["deductions"]=A(Free()),["inputsCapturedAt"]=S(),["inputsCapturedBy"]=S(),["inputsCaptureCounts"]=Counts(),["capture"]=O(new(){["cycleId"]=S(),["capturedAt"]=S(),["counts"]=Counts()},"cycleId","capturedAt","counts")},"id","companyId","period","stage","status","invoices");
    static OpenApiSchema SharedRider()=>O(new(){["rider"]=S(),["period"]=S(),["status"]=S(),["labelAr"]=S(),["missingApps"]=A(S()),["apps"]=Free(),["combinedNet"]=N(),["released"]=B()},"rider","period","status","labelAr","combinedNet");
    static OpenApiSchema Pipeline()=>O(new(){["cycleId"]=S(),["period"]=S(),["companyId"]=S(),["stage"]=N(),["status"]=S(),["riders"]=A(O(new(){["rider"]=S(),["name"]=S(),["stages"]=Free(),["lines"]=A(CycleLine())},"rider","stages"))},"cycleId","period","companyId","stage","status","riders");
    static OpenApiSchema TreasurySummarySchema()=>O(new(){["companies"]=A(O(new(){["companyId"]=S(),["code"]=S(),["nameAr"]=S(),["cash"]=N(),["bank"]=N(),["total"]=N()},"companyId","cash","bank","total")),["totals"]=O(new(){["cash"]=N(),["bank"]=N(),["total"]=N()},"cash","bank","total")},"companies","totals");
    static OpenApiSchema BankFilePreview()=>O(new(){["sponsorId"]=S(),["from"]=S(),["to"]=S(),["basis"]=S(),["comparisonOnly"]=B(),["doNotSend"]=B(),["doNotSendMark"]=S(),["rows"]=A(O(new(){["bank"]=S(),["iban"]=S(),["amount"]=N(),["notes"]=S(),["name"]=S(),["iqama"]=S(),["address"]=S(),["errors"]=A(S())},"iban","amount","iqama")),["excludedRows"]=A(Free()),["previewToken"]=S(),["expiresInSeconds"]=N(),["canExport"]=B()},"sponsorId","from","to","basis","rows","previewToken","canExport");
    static OpenApiSchema MultiRiderDeductions()=>O(new(){["invoiceId"]=S(),["deductionRows"]=N(),["deductionAmount"]=N(),["multiRiderRows"]=N(),["multiRiderAmount"]=N()},"invoiceId","deductionRows","deductionAmount","multiRiderRows","multiRiderAmount");
    static OpenApiSchema PayrollPreview()=>O(new(){["iqama"]=S(),["period"]=S(),["fingerprint"]=S(),["gross"]=N(),["net"]=N(),["blockers"]=A(S())},"iqama","period");
    static OpenApiSchema PayrollRun()=>O(new(){["id"]=S(),["iqama"]=S(),["period"]=S(),["approvedAt"]=S(),["net"]=N(),["status"]=S()},"id","iqama","period");
    /// <summary>Frozen run-input rows per kind (BR §39.2), e.g. {"ninja_daily":120}.</summary>
    static OpenApiSchema Counts()=>new(){Type=JsonSchemaType.Object,AdditionalProperties=N()};
    public static OpenApiSchema Review()=>O(new(){["invoiceId"]=S(),["app"]=S(),["accounts"]=A(O(new(){["accountId"]=S(),["sourceLineId"]=S(),["baseGross"]=N(),["incentives"]=N(),["orders"]=N(),["riders"]=A(O(new(){["rider"]=S(),["sourceLineId"]=S(),["operationSourceIds"]=A(S()),["baseGross"]=N(),["orders"]=N(),["deduction"]=N(),["category"]=S(),["days"]=N(),["metrics"]=Free()},"rider","baseGross","orders","deduction","category","days","metrics"))},"accountId","sourceLineId","baseGross","incentives","orders","riders")),["deductions"]=A(Free())},"invoiceId","app","accounts","deductions");
    public static OpenApiSchema CycleLine()=>O(new(){["id"]=S(),["cycleId"]=S(),["rider"]=S(),["app"]=S(),["period"]=S(),["status"]=S(),["platformGross"]=N(),["incentives"]=N(),["gross"]=N(),["companyCommission"]=N(),["entitlement"]=N(),["accountDeduction"]=N(),["allocatedVehicleCharge"]=N(),["sources"]=A(O(new(){["sourceLineId"]=S(),["accountId"]=S(),["operationSourceIds"]=A(S()),["baseGross"]=N(),["incentive"]=N(),["deduction"]=N()},"sourceLineId","accountId","baseGross")),["lines"]=A(Free()),["blockers"]=A(S())},"id","cycleId","rider","app","status","gross","sources");
    public static OpenApiSchema Setting()=>O(new(){["id"]=S(),["kind"]=S(),["app"]=S(),["period"]=S(),["code"]=S(),["version"]=N(),["status"]=S(),["data"]=O(new(){["name"]=S(),["type"]=S(),["billingModel"]=S(),["accountingRole"]=S(),["eligibilitySetId"]=S(),["parameters"]=Free(),["values"]=Free(),["rules"]=A(O(new(){["metric"]=S(),["operator"]=S(),["threshold"]=N(),["effect"]=O(new(){["type"]=S()},"type"),["included"]=B()},"metric","operator","threshold","effect"))}),["createdBy"]=S(),["effectiveFrom"]=S(),["allowedActions"]=A(S())},"id","app","period","status","version","data");
    public static OpenApiSchema Rider()=>O(new(){["id"]=S(),["iqama"]=S(),["name"]=S(),["status"]=S(),["companyId"]=S(),["phone"]=S(),["chips"]=A(S())},"id");
    public static OpenApiSchema Journal()=>O(new(){["id"]=S(),["status"]=S(),["companyId"]=S(),["date"]=S(),["reference"]=S(),["description"]=S(),["journalEntryId"]=S(),["allowedActions"]=A(S()),["lines"]=A(O(new(){["accountCode"]=S(),["debit"]=N(),["credit"]=N()},"accountCode","debit","credit"))},"id","status");
    public static OpenApiSchema Campaign()=>O(new(){["id"]=S(),["title"]=S(),["status"]=S(),["expiresAt"]=S(),["path"]=S(),["kind"]=S()},"id");
    static OpenApiSchema Explain()=>O(new(){["label"]=S(),["formula"]=S(),["inputs"]=Free(),["amount"]=N(),["category"]=S(),["affectsNet"]=B()},"label","formula","inputs","amount","category");
    static OpenApiSchema ReviewRow()=>O(new(){["rider"]=S(),["name"]=S(),["status"]=S(),["review"]=S(),["reviewedBy"]=new OpenApiSchema{Type=JsonSchemaType.String|JsonSchemaType.Null},["reviewedAt"]=new OpenApiSchema{Type=JsonSchemaType.String|JsonSchemaType.Null},["gross"]=N(),["commission"]=N(),["afterCommission"]=N(),["phase1"]=N(),["preliminaryDue"]=N(),["phase2"]=N(),["phase3"]=N(),["net"]=N(),["allowedActions"]=A(S()),["blockers"]=A(S()),["salaryApprovedAt"]=S(),["debtDeductionStatus"]=S(),["items"]=A(O(new(){["key"]=S(),["phase"]=N(),["order"]=N(),["labelAr"]=S(),["amount"]=N(),["requestedAmount"]=N(),["editable"]=B(),["max"]=new OpenApiSchema{Type=JsonSchemaType.Number|JsonSchemaType.Null},["source"]=S()},"key","phase","amount","editable","source")),["edits"]=A(Free())},"rider","status","review","net","items");
    static OpenApiSchema Statement()=>O(new(){["header"]=O(new(){["rider"]=S(),["name"]=S(),["period"]=S(),["status"]=S(),["ibanMasked"]=S(),["cycles"]=A(Free())},"rider","name","period","cycles"),["source"]=S(),["status"]=S(),["net"]=N(),["carried"]=N(),["closing"]=N(),["balanceBasis"]=S(),["approvedOnly"]=B(),["cards"]=A(O(new(){["app"]=S(),["platform"]=S(),["gross"]=N(),["earnedGross"]=N(),["companyCommission"]=N(),["vehicleCharge"]=N(),["netBeforePersonal"]=N(),["sources"]=A(O(new(){["sourceLineId"]=S(),["accountId"]=S(),["operationSourceIds"]=A(S()),["baseGross"]=N(),["incentive"]=N(),["deduction"]=N()},"sourceLineId","accountId","baseGross")),["explain"]=A(Explain())},"app","gross","earnedGross","explain")),["deductions"]=A(Free()),["phases"]=new OpenApiSchema{Type=JsonSchemaType.Object|JsonSchemaType.Null,AdditionalProperties=N()}},"header","source","status","net","carried","closing","cards","deductions");
    public static OpenApiSchema? Response(string method,string path)
    {
        bool get=method=="GET";
        if(path=="/api/me"||path=="/api/users/{id}"||path=="/api/users/{id}/status"||path=="/api/users/{id}/reset-password"||path=="/api/me/password")return User();
        if(path=="/api/users")return get?A(User()):User();
        if(path=="/api/admin/approval-exceptions")return A(O(new(){["id"]=S(),["action"]=S(),["operation"]=S(),["recordId"]=S(),["maker"]=S(),["actor"]=S(),["at"]=S(),["approvalExceptionUntil"]=S(),["selfApprovedException"]=B()},"id","action","operation","recordId","maker","actor","at","approvalExceptionUntil","selfApprovedException"));
        if(path=="/api/finance/statement/rider/{iqama}/monthly")return Statement();
        if(path=="/api/pay/cycles/{id}/debt-deductions")return O(new(){["cycleId"]=S(),["stage"]=N(),["status"]=S(),["allowedActions"]=A(S()),["rows"]=A(O(new(){["rider"]=S(),["salaryNet"]=N(),["net"]=N(),["status"]=S(),["allowedActions"]=A(S()),["items"]=A(O(new(){["key"]=S(),["source"]=S(),["category"]=S(),["labelAr"]=S(),["due"]=N(),["max"]=N(),["amount"]=N(),["remainingBalance"]=N(),["accountCode"]=S()},"key","source","category","labelAr","due","max","amount","remainingBalance","accountCode"))},"rider","salaryNet","net","status","allowedActions","items"))},"cycleId","stage","status","rows");
        if(path=="/api/pay/cycles/{id}/unrecovered-costs")return O(new(){["cycleId"]=S(),["status"]=S(),["total"]=N(),["rows"]=A(O(new(){["rider"]=S(),["operatingCost"]=N(),["fuelCost"]=N(),["total"]=N(),["accountCode"]=S(),["sourceLineIds"]=A(S())},"rider","operatingCost","fuelCost","total","accountCode","sourceLineIds"))},"cycleId","status","total","rows");
        if(path=="/api/pay/cycles/{id}/pipeline")return Pipeline();
        if(path=="/api/pay/cycles/{id}/review")return O(new(){["cycle"]=O(new(){["id"]=S(),["period"]=S(),["companyId"]=S(),["stage"]=N(),["status"]=S()},"id","period","companyId","stage","status"),["totals"]=O(new(){["riders"]=N(),["approved"]=N(),["pending"]=N(),["needsDecision"]=N(),["net"]=N()},"riders","approved","pending","needsDecision","net"),["rows"]=A(ReviewRow())},"cycle","totals","rows");
        if(path=="/api/pay/cycles/{id}/review/approve-all")return O(new(){["approved"]=N(),["skipped"]=N()},"approved","skipped");
        if(path=="/api/pay/cycles/{id}/review/{iqama}")return ReviewRow();
        if(path=="/api/pay/shared-riders")return A(SharedRider());
        if(path=="/api/pay/shared-riders/{rider}/{period}/release")return SharedRider();
        if(path=="/api/pay/review-draft")return O(new(){["app"]=S(),["period"]=S(),["companyId"]=S(),["invoices"]=A(Review()),["blockers"]=A(O(new(){["code"]=S(),["message"]=S(),["fixAction"]=S(),["sourceLineId"]=S()},"code","message","fixAction","sourceLineId")),["warnings"]=A(O(new(){["code"]=S(),["message"]=S(),["sourceLineId"]=S(),["erpOrders"]=N(),["invoiceOrders"]=N()},"code","message","sourceLineId")),["allocationProposals"]=A(Free()),["dataThrough"]=S(),["unassignedOrders"]=A(Free()),["ridersNotFound"]=A(Free()),["evidence"]=A(Free()),["canCreate"]=B(),["totals"]=O(new(){["gross"]=N(),["deductions"]=N(),["vat"]=N()},"gross","deductions","vat")},"app","period","companyId","invoices","blockers","unassignedOrders","ridersNotFound","evidence","canCreate","totals");
        if(path=="/api/finance/treasury/summary")return TreasurySummarySchema();
        if(path=="/api/finance/bank-file/preview")return BankFilePreview();
        if(path=="/api/finance/vault-transfer")return Journal();
        if(path=="/api/finance/sponsors")return A(O(new(){["id"]=S(),["sponsorId"]=S(),["code"]=S(),["nameAr"]=S(),["shortNameAr"]=S(),["vaultBalance"]=N(),["mainVaultBalance"]=N(),["bankBalance"]=N(),["allowedActions"]=A(S())},"id","code"));
        if(path=="/api/debts/{id}"||(path=="/api/debts"&&!get)||path.EndsWith("/transfer-sponsor")||path.EndsWith("/collect")&&path.StartsWith("/api/debts"))return path.EndsWith("/collect")?O(new(){["debt"]=Debt(),["settlement"]=Free(),["journal"]=Free()},"debt"):Debt();
        if(path=="/api/debts"&&get)return new(){OneOf=[A(Debt()),Page(Debt())]};
        if(path=="/api/debts/summary"&&get)return O(new(){["totalCount"]=N(),["activeRemainingTotal"]=N(),["advanceRemainingTotal"]=N(),["pendingOpeningCount"]=N(),["pendingOpeningTotal"]=N()},"totalCount","activeRemainingTotal","advanceRemainingTotal","pendingOpeningCount","pendingOpeningTotal");
        if(path=="/api/riders"&&get)return new(){OneOf=[A(Rider()),Page(Rider())]};
        if(path=="/api/workforce"&&get)return WorkforcePage();
        if(path=="/api/workforce/summary"&&get)return WorkforceSummary();
        if(path=="/api/workforce/{id}/360"&&get)return Workforce360();
        if(get&&path is "/api/workforce/saudi-payroll-employees" or "/api/workforce/terminated" or "/api/workforce/absence-cases" or "/api/workforce/legal-cases" or "/api/workforce/data-quality")return WorkforceListEnvelope();
        if(path=="/api/workforce/export"&&get)return Free();
        if(path=="/api/hr-catalogs/{kind}"&&get)return WorkforceListEnvelope();
        if(path=="/api/operations/{kind}"&&get)return OperationsPage();
        if(path=="/api/riders/{iqama}/accounts"&&get)return OperationsPage();
        if(path=="/api/riders/lookup"&&get)return A(Rider());
        if(path is "/api/campaigns" or "/api/campaigns/{id}")return get&&path=="/api/campaigns"?new(){OneOf=[A(Campaign()),Page(Campaign())]}:Campaign();
        if(path.StartsWith("/api/finance/journals/manual")&&!path.EndsWith("/attachment"))return get&&path.EndsWith("/manual")?Page(Journal()):Journal();
        if(path=="/api/pay/cycles/{id}/lines")return Page(CycleLine());
        if(path.StartsWith("/api/pay/cycles")&&!path.Contains("/review")&&!path.Contains("/pipeline")&&!path.Contains("/unrecovered")&&!path.Contains("/debt-deductions"))return get?Page(Cycle()):Cycle();
        if(path.StartsWith("/api/platforms/ninja/invoices")||path.StartsWith("/api/platforms/keeta/invoices")&&(get||path.EndsWith("/discard")))
        {
            if(path.EndsWith("/multi-rider-deductions"))return MultiRiderDeductions();
            if(path.EndsWith("/lines"))return A(InvoiceLine());
            if(path is "/api/platforms/ninja/invoices" or "/api/platforms/keeta/invoices")return get?Page(Invoice()):new(){OneOf=[Invoice(),O(new(){["rows"]=A(Invoice()),["canCommit"]=B(),["fingerprint"]=S()},"rows","canCommit","fingerprint")]};
            return Invoice();
        }
        if(path=="/api/pay/settings"||path.StartsWith("/api/pay/settings/{id}")||path.StartsWith("/api/pay/apps/{app}/")&&new[]{"terms","schemes","eligibility"}.Any(s=>path.Contains("/"+s)))return method=="DELETE"?O(new(){["deleted"]=B(),["id"]=S()},"deleted","id"):get?Page(Setting()):Setting();
        if(path=="/api/payroll/preview")return A(PayrollPreview());
        if(path=="/api/payroll/runs")return A(PayrollRun());
        if(path is "/api/adjustments" or "/api/payments")return get?A(Free()):Free();
        if(path=="/api/ledger/{iqama}")return O(new(){["rows"]=A(O(new(){["id"]=S(),["iqama"]=S(),["amount"]=N(),["kind"]=S(),["period"]=S()},"id","iqama","amount")),["balance"]=N()},"rows","balance");
        return null;
    }
    public static void Describe(OpenApiOperation operation,string method,string path)
    {
        var schema=Response(method,path.Split('?')[0]);if(schema==null)return;
        operation.Responses??=new();operation.Responses["200"]=new OpenApiResponse{Description="Successful response (stored wire field names)",Content=new Dictionary<string,OpenApiMediaType>{["application/json"]=new(){Schema=schema}}};
    }
}
