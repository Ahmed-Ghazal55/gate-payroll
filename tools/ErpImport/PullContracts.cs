using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

// Reviewed, named final tables only. Field maps are explicit; no SELECT *, import rows or credential tables.
public sealed record PullContract(string Kind,string Schema,string Table,string Fields,string Map,string Relation="",string Totals="",string From="",string To="");
public static class PullContracts
{
    public static readonly PullContract[] Catalog =
    [
        new("fuel_card","app","FuelCards","Id SponsorId OperatingCityId Provider IdentifierType CardNumber PlateNumberText IsDeleted","provider:Provider identifierType:IdentifierType cardNumber:CardNumber plateNumber:PlateNumberText","card"),
        new("fuel_card_assignment","app","FuelCardRiderAssignments","Id FuelCardId RiderProfileId EmployeeId EffectiveFrom EffectiveTo","effectiveFrom:EffectiveFrom effectiveTo:EffectiveTo","fuel_rider","","EffectiveFrom","EffectiveTo"),
        new("fuel_usage_month","app","FuelCardMonthlyUsages","Id FuelCardId RiderProfileId EmployeeId ReportMonth TotalLiters TotalAmount AmountBeforeTax VatAmount TransactionCount FuelType FirstTransactionAtUtc LastTransactionAtUtc IsDeleted","month:ReportMonth liters:TotalLiters amount:TotalAmount amountBeforeTax:AmountBeforeTax vat:VatAmount transactionCount:TransactionCount fuelType:FuelType firstTransactionAt:FirstTransactionAtUtc lastTransactionAt:LastTransactionAtUtc","fuel_rider","liters amount amountBeforeTax vat transactionCount")
        ,new("custody_equipment","maintenance","RiderInventoryIssues","Id IssueNumber RiderProfileId IssuedFromLocationId IssuedAtUtc RelatedAssignmentId Status IsDeleted","issueNumber:IssueNumber issuedAt:IssuedAtUtc inventoryLocationSourceId:IssuedFromLocationId assignmentSourceId:RelatedAssignmentId","equipment","quantity returnedQuantity totalCost"),
        new("","maintenance","RiderInventoryIssueLines","Id RiderInventoryIssueId InventoryItemId Quantity TotalCost ExpectedReturn ReturnedQuantity",""),
        new("custody_sim","app","RiderPhoneSimAssignments","Id PhoneSimCardId RiderProfileId EffectiveFrom EffectiveTo","simSourceId:PhoneSimCardId effectiveFrom:EffectiveFrom effectiveTo:EffectiveTo","rider","","EffectiveFrom","EffectiveTo"),
        new("vehicle_accident","app","VehicleAccidents","Id AccidentNumber VehicleId RiderProfileId EmployeeId RiderVehicleAssignmentId OccurredAtUtc ClosedAtUtc Severity Status IsDeleted","number:AccidentNumber occurredAt:OccurredAtUtc closedAt:ClosedAtUtc severity:Severity assignmentSourceId:RiderVehicleAssignmentId","accident"),
        new("","app","VehicleAccidentCases","Id VehicleAccidentId Stage RiderFaultPercentage EstimatedRepairCost OpeningFeeAmount SettlementAmount RefundStatus RefundRequestedAmount RefundReceivedAmount IsDeleted",""),
        new("","app","VehicleAccidentInstallments","Id VehicleAccidentId PeriodFrom PeriodTo PaidOn Amount RefundEligibleAmount IsDeleted",""),
        new("vehicle_expense","maintenance","VehicleExpenses","Id VehicleId RiderVehicleAssignmentId RiderProfileId ExpenseType SourceEntityType SourceEntityId OccurredOn AmountBeforeTax TaxAmount TotalAmount CurrencyCode ReversalOfExpenseId","type:ExpenseType occurredOn:OccurredOn amountBeforeTax:AmountBeforeTax vat:TaxAmount amount:TotalAmount currency:CurrencyCode reversesSourceId:ReversalOfExpenseId sourceEntityType:SourceEntityType sourceEntityId:SourceEntityId assignmentSourceId:RiderVehicleAssignmentId","vehicle","amountBeforeTax vat amount")
        ,new("chefz_account","chefz","AccountDetails","Id PlatformRiderAccountId ReportIdNumber AccountType VehicleType DashboardStatus IsDeleted","reportIdNumber:ReportIdNumber accountType:AccountType vehicleType:VehicleType dashboardStatus:DashboardStatus","platform_account"),
        new("chefz_daily","chefz","DailyPerformance","Id PlatformRiderAccountId RiderClientAssignmentId RiderProfileId SourceId ReportDate ReportIdNumber SourceRowNumber TotalValidOffers AcceptedOffers DeclinedOffers MissedOffers AcceptanceRate","date:ReportDate reportIdNumber:ReportIdNumber sourceId:SourceId sourceRow:SourceRowNumber totalValidOffers:TotalValidOffers acceptedOffers:AcceptedOffers declinedOffers:DeclinedOffers missedOffers:MissedOffers acceptanceRate:AcceptanceRate","platform_rider","totalValidOffers acceptedOffers declinedOffers missedOffers"),
        new("chefz_settlement","chefz","WalletSettlements","Id PlatformRiderAccountId RiderClientAssignmentId RiderProfileId AccountType PeriodFrom PeriodTo ChargedFrom ChargedTo AssignedDays DeliveryFees VatAmount Tips Compensation Bonuses Deductions GrossEarnings CommissionRate DailyFee CompanyAmount RiderAmount StartWalletAmount EndWalletAmount","accountType:AccountType periodFrom:PeriodFrom periodTo:PeriodTo chargedFrom:ChargedFrom chargedTo:ChargedTo assignedDays:AssignedDays deliveryFees:DeliveryFees vat:VatAmount tips:Tips compensation:Compensation bonuses:Bonuses deductions:Deductions gross:GrossEarnings commissionRate:CommissionRate dailyFee:DailyFee companyAmount:CompanyAmount riderAmount:RiderAmount startWalletAmount:StartWalletAmount endWalletAmount:EndWalletAmount","platform_rider","deliveryFees vat tips compensation bonuses deductions gross companyAmount riderAmount","PeriodFrom","PeriodTo")
        ,new("jahez_account_session","jahez_v2","AccountSessions","Id PlatformRiderAccountId RiderProfileId RiderClientAssignmentId StartedAtUtc EndedAtUtc CommissionStartsOn CommissionPostedThrough LastSettlementPaymentAtUtc IsDeleted","startedAt:StartedAtUtc endedAt:EndedAtUtc commissionStartsOn:CommissionStartsOn commissionPostedThrough:CommissionPostedThrough lastSettlementPaymentAt:LastSettlementPaymentAtUtc","platform_rider","","StartedAtUtc","EndedAtUtc"),
        new("jahez_earnings_statement","jahez_v2","EarningsStatements","Id HandoverId FromDate ToDate TotalDeliveryPrice TotalPenalties TotalCashAmount TotalDriverDebit TotalServiceDeduction TotalDriverCredit TotalBonuses TotalTips TotalFreeOrders SupersedesId","from:FromDate to:ToDate deliveryPrice:TotalDeliveryPrice penalties:TotalPenalties cashAmount:TotalCashAmount driverDebit:TotalDriverDebit serviceDeduction:TotalServiceDeduction driverCredit:TotalDriverCredit bonuses:TotalBonuses tips:TotalTips freeOrders:TotalFreeOrders supersedesSourceId:SupersedesId","jahez","deliveryPrice penalties cashAmount driverDebit serviceDeduction driverCredit bonuses tips freeOrders","FromDate","ToDate"),
        new("jahez_transaction","jahez_v2","PlatformTransactions","Id AccountId HandoverId BatchId ImportRowId OccurredAtUtc DeliveryPrice CashAmount NetAmount DriverAdjustment SupersededByBatchId","occurredAt:OccurredAtUtc deliveryPrice:DeliveryPrice cashAmount:CashAmount net:NetAmount driverAdjustment:DriverAdjustment batchSourceId:BatchId importRowSourceId:ImportRowId supersededByBatchSourceId:SupersededByBatchId","jahez","deliveryPrice cashAmount net driverAdjustment"),
        new("jahez_ledger_entry","jahez_v2","AccountingEntries","Id AccountId HandoverId Bucket Kind Amount SourceId ReversesEntryId OccurredAtUtc FromDate ThroughDate","bucket:Bucket kindCode:Kind amount:Amount sourceEventId:SourceId reversesSourceId:ReversesEntryId occurredAt:OccurredAtUtc from:FromDate through:ThroughDate","jahez","amount"),
        new("jahez_account_fee","jahez_v2","AccountFees","Id HandoverId Amount WaivedAmount ApprovalRequestId IsDeleted","amount:Amount waivedAmount:WaivedAmount approvalSourceId:ApprovalRequestId","jahez","amount waivedAmount"),
        new("jahez_account_fee_refund","jahez_v2","AccountFeeRefunds","Id HandoverId AccountFeeId MoneyMovementId GrossFeeRefund CancelledUnpaidFee WaiveCommission CalculatedCommission DeductedCommission RefundAmount OccurredAtUtc","accountFeeSourceId:AccountFeeId moneyMovementSourceId:MoneyMovementId grossFeeRefund:GrossFeeRefund cancelledUnpaidFee:CancelledUnpaidFee waiveCommission:WaiveCommission calculatedCommission:CalculatedCommission deductedCommission:DeductedCommission refundAmount:RefundAmount occurredAt:OccurredAtUtc","jahez","grossFeeRefund cancelledUnpaidFee calculatedCommission deductedCommission refundAmount"),
        new("jahez_transferred_debt","jahez_v2","TransferredDebts","Id OperationId RiderProfileId Amount PaidAmount PaidAtUtc IsDeleted","operationSourceId:OperationId amount:Amount paidAmount:PaidAmount paidAt:PaidAtUtc","jahez_debt","amount paidAmount"),
        new("","jahez_v2","Operations","Id HandoverId ExpectedSponsorId Status PaymentStatus Net Commission CalculatedCommission Remaining Fund Purpose TransferToRider ExecutedAtUtc","" )
    ];
    public static IEnumerable<string> Kinds => Catalog.Where(c=>c.Kind!="").Select(c=>c.Kind);
    public static IEnumerable<SourceTable> Tables => Catalog.Select(c=>new SourceTable(c.Schema,c.Table,c.Fields,true));
    public static string Table(string kind)=>Catalog.FirstOrDefault(c=>c.Kind==kind)?.Table??"";
    public static void AddToPlan(SourceSnapshot source,ImportPlan plan,Dictionary<string,string> companies,Dictionary<string,string> employees,Dictionary<string,string> profiles,Func<string,string> city)
    {
        List<JsonObject> Rows(string table)=>source.Tables.GetValueOrDefault(table)??[];
        var indexes=new Dictionary<string,Dictionary<string,JsonObject>>();
        JsonObject? Find(string table,string id)
        {if(id=="")return null;if(!indexes.TryGetValue(table,out var index))indexes[table]=index=Rows(table).ToDictionary(r=>r.S("Id"));return index.GetValueOrDefault(id);}
        var riderCompanies=plan.Documents.Where(d=>d.Kind=="rider").ToDictionary(d=>d.Body.S("id"),d=>d.Body.S("sponsorId"));
        string Key(string kind,string id)=>id==""?"":"erp:"+kind+":"+id;
        string Rider(JsonObject row)
        {
            string a=employees.GetValueOrDefault(row.S("EmployeeId"))??"",b=profiles.GetValueOrDefault(row.S("RiderProfileId"))??"";
            if(row.S("EmployeeId")!=""&&a==""||row.S("RiderProfileId")!=""&&b==""||a!=""&&b!=""&&a!=b)return "";
            return a!=""?a:b;
        }
        foreach(var contract in Catalog)
        {
            if(contract.Kind=="")continue;
            if(!source.Tables.TryGetValue(contract.Table,out var rows)){plan.Unavailable[contract.Kind]="source_table_absent";continue;}
            if(rows.Any(r=>r.S("Id")=="")||rows.GroupBy(r=>r.S("Id")).Any(g=>g.Count()>1))throw new ImportFailure("source_identity_invalid:"+contract.Kind);
            foreach(var row in rows)
            {
                if(contract.From!=""&&(row.S(contract.From)==""||row.S(contract.To)!=""&&string.CompareOrdinal(row.S(contract.From),row.S(contract.To))>0))
                {plan.Reject(contract.Kind,"invalid_date_range");continue;}
                var doc=new JsonObject{["id"]=Key(contract.Kind,row.S("Id")),["erpSourceId"]=row.S("Id"),["erpSource"]=contract.Kind,["sourceDeleted"]=row.B("IsDeleted"),["erpStatusCode"]=row["Status"]?.DeepClone()};
                foreach(string pair in contract.Map.Split(' ',StringSplitOptions.RemoveEmptyEntries)){var p=pair.Split(':');doc[p[0]]=row[p[1]]?.DeepClone();}
                if(contract.Relation=="card")
                {
                    doc["companyId"]=companies.GetValueOrDefault(row.S("SponsorId"))??"";doc["cityId"]=city(row.S("OperatingCityId"));
                    if(doc.S("companyId")==""){plan.Reject(contract.Kind,"missing_company_relation");continue;}
                }
                if(contract.Relation=="fuel_rider")
                {
                    var card=Find("FuelCards",row.S("FuelCardId"));string rider=Rider(row);
                    if(card==null){plan.Reject(contract.Kind,"missing_fuel_card_relation");continue;}
                    if(rider==""){plan.Reject(contract.Kind,"unmatched_rider");continue;}
                    doc["riderId"]=rider;doc["fuelCardId"]=Key("fuel_card",card.S("Id"));doc["companyId"]=companies.GetValueOrDefault(card.S("SponsorId"))??"";
                    if(doc.S("companyId")==""){plan.Reject(contract.Kind,"missing_company_relation");continue;}
                }
                if(contract.Relation is "rider" or "equipment" or "accident")
                {
                    string rider=Rider(row);if(rider==""){plan.Reject(contract.Kind,"unmatched_rider");continue;}
                    doc["riderId"]=rider;
                    doc["companyId"]=riderCompanies.GetValueOrDefault(rider)??"";
                }
                if(contract.Relation=="equipment")
                {
                    var lines=Rows("RiderInventoryIssueLines").Where(x=>x.S("RiderInventoryIssueId")==row.S("Id")).ToList();
                    if(lines.Count==0){plan.Reject(contract.Kind,"missing_issue_lines");continue;}
                    if(lines.Any(x=>x.N("Quantity")<=0||x.N("ReturnedQuantity")<0||x.N("ReturnedQuantity")>x.N("Quantity"))){plan.Reject(contract.Kind,"invalid_return_quantity");continue;}
                    doc["lines"]=new JsonArray(lines.Select(x=>(JsonNode)x.Clone()).ToArray());doc["quantity"]=lines.Sum(x=>x.N("Quantity"));doc["returnedQuantity"]=lines.Sum(x=>x.N("ReturnedQuantity"));doc["totalCost"]=lines.Sum(x=>x.N("TotalCost"));
                }
                if(contract.Relation is "vehicle" or "accident")
                {
                    var vehicle=Find("Vehicles",row.S("VehicleId"));if(vehicle==null){plan.Reject(contract.Kind,"missing_vehicle_relation");continue;}
                    doc["vehicleId"]=Key("vehicle",vehicle.S("Id"));doc["companyId"]=companies.GetValueOrDefault(vehicle.S("SponsorId"))??"";
                    if(doc.S("companyId")==""){plan.Reject(contract.Kind,"missing_company_relation");continue;}
                    if(contract.Relation=="vehicle"&&row.S("RiderProfileId")!="")
                    {string rider=Rider(row);if(rider==""){plan.Reject(contract.Kind,"unmatched_rider");continue;}doc["riderId"]=rider;}
                    if(contract.Relation=="accident")
                    {
                        var cases=Rows("VehicleAccidentCases").Where(x=>x.S("VehicleAccidentId")==row.S("Id")).ToList();
                        var installments=Rows("VehicleAccidentInstallments").Where(x=>x.S("VehicleAccidentId")==row.S("Id")).ToList();
                        if(installments.Any(x=>string.CompareOrdinal(x.S("PeriodFrom"),x.S("PeriodTo"))>0)){plan.Reject(contract.Kind,"invalid_installment_dates");continue;}
                        doc["cases"]=new JsonArray(cases.Select(x=>(JsonNode)x.Clone()).ToArray());doc["installments"]=new JsonArray(installments.Select(x=>(JsonNode)x.Clone()).ToArray());
                    }
                }
                if(contract.Relation is "platform_account" or "platform_rider")
                {
                    var account=Find("PlatformRiderAccounts",row.S("PlatformRiderAccountId"));
                    if(account==null){plan.Reject(contract.Kind,"missing_platform_account_relation");continue;}
                    doc["platformAccountId"]=Key("platform_account",account.S("Id"));doc["companyId"]=companies.GetValueOrDefault(account.S("SponsorId"))??"";
                    if(doc.S("companyId")==""){plan.Reject(contract.Kind,"missing_company_relation");continue;}
                    if(contract.Relation=="platform_account")doc["registeredRiderId"]=employees.GetValueOrDefault(account.S("RegisteredEmployeeId"))??"";
                    else
                    {
                        string rider=Rider(row);var assignment=Find("RiderClientAssignments",row.S("RiderClientAssignmentId"));
                        if(rider==""){plan.Reject(contract.Kind,"unmatched_rider");continue;}
                        if(assignment==null||assignment.S("PlatformRiderAccountId")!=account.S("Id")||assignment.S("RiderProfileId")!=row.S("RiderProfileId"))
                        {plan.Reject(contract.Kind,"unmatched_assignment");continue;}
                        doc["riderId"]=rider;doc["assignmentSourceId"]=assignment.S("Id");
                    }
                }
                if(contract.Relation is "jahez" or "jahez_debt")
                {
                    string sessionId=row.S("HandoverId");
                    if(contract.Relation=="jahez_debt")
                    {var operation=Find("Operations",row.S("OperationId"));if(operation==null){plan.Reject(contract.Kind,"missing_operation_relation");continue;}sessionId=operation.S("HandoverId");}
                    var session=Find("AccountSessions",sessionId);
                    string accountId=row.S("AccountId");
                    if(session!=null)
                    {
                        if(accountId!=""&&accountId!=session.S("PlatformRiderAccountId")){plan.Reject(contract.Kind,"conflicting_account_relation");continue;}
                        accountId=session.S("PlatformRiderAccountId");string rider=Rider(session);
                        if(rider==""||contract.Relation=="jahez_debt"&&Rider(row)!=rider){plan.Reject(contract.Kind,"unmatched_rider");continue;}
                        doc["riderId"]=rider;doc["sessionId"]=Key("jahez_account_session",sessionId);
                    }
                    else if(sessionId!=""||accountId==""){plan.Reject(contract.Kind,"missing_session_relation");continue;}
                    else {doc["riderAttribution"]="account_only";plan.Observations[contract.Kind+"_account_only"]=plan.Observations.GetValueOrDefault(contract.Kind+"_account_only")+1;}
                    var account=Find("PlatformRiderAccounts",accountId);
                    if(account==null){plan.Reject(contract.Kind,"missing_platform_account_relation");continue;}
                    doc["platformAccountId"]=Key("platform_account",accountId);doc["companyId"]=companies.GetValueOrDefault(account.S("SponsorId"))??"";
                    if(doc.S("companyId")==""){plan.Reject(contract.Kind,"missing_company_relation");continue;}
                }
                plan.Documents.Add(new(contract.Kind,doc));
            }
        }
    }
}
