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
    public static OpenApiSchema Debt()=>O(new(){["id"]=S(),["iqama"]=S(),["category"]=S(),["categoryAr"]=S(),["status"]=S(),["totalAmount"]=N(),["remainingBalance"]=N(),["monthlyInstallment"]=N(),["paidAmount"]=N(),["creditorSponsorId"]=S()},"id","totalAmount","remainingBalance","status");
    public static OpenApiSchema InvoiceLine()=>O(new(){["id"]=S(),["invoiceId"]=S(),["sourceRow"]=N(),["platformAccountId"]=S(),["lineType"]=S(),["baseGross"]=N(),["incentives"]=N(),["deductionAmount"]=N(),["orders"]=N(),["vat"]=N(),["expected"]=N(),["invoiced"]=N(),["difference"]=N(),["reason"]=S(),["status"]=S(),["deductions"]=A(O(new(){["category"]=S(),["amount"]=N(),["level"]=S(),["expenseAccount"]=S()},"category","amount","level"))},"id","invoiceId","sourceRow","baseGross","incentives","deductionAmount","orders","vat","invoiced","status");
    public static OpenApiSchema Invoice()=>O(new(){["id"]=S(),["companyId"]=S(),["platform"]=S(),["period"]=S(),["reference"]=S(),["amountExclVat"]=N(),["deductionsTotal"]=N(),["vat"]=N(),["approvalStatus"]=S(),["status"]=S(),["allowedActions"]=A(S()),["journalEntryId"]=S(),["accountIds"]=new OpenApiSchema{Type=JsonSchemaType.Array|JsonSchemaType.Null,Items=S()},["lines"]=A(InvoiceLine())},"id","companyId","period","amountExclVat","deductionsTotal","vat","status");
    public static OpenApiSchema Cycle()=>O(new(){["id"]=S(),["companyId"]=S(),["period"]=S(),["app"]=S(),["stage"]=N(),["status"]=S(),["invoices"]=A(Review()),["riders"]=A(O(new(){["rider"]=S(),["net"]=N(),["carried"]=N(),["closing"]=N(),["status"]=S(),["review"]=S()},"rider","net","status")),["journalEntryId"]=S(),["history"]=A(Free()),["deductions"]=A(Free())},"id","companyId","period","stage","status","invoices");
    public static OpenApiSchema Review()=>O(new(){["invoiceId"]=S(),["app"]=S(),["accounts"]=A(O(new(){["accountId"]=S(),["sourceLineId"]=S(),["baseGross"]=N(),["incentives"]=N(),["orders"]=N(),["riders"]=A(O(new(){["rider"]=S(),["baseGross"]=N(),["orders"]=N(),["deduction"]=N(),["category"]=S(),["days"]=N(),["metrics"]=Free()},"rider","baseGross","orders","deduction","category","days","metrics"))},"accountId","sourceLineId","baseGross","incentives","orders","riders")),["deductions"]=A(Free())},"invoiceId","app","accounts","deductions");
    public static OpenApiSchema CycleLine()=>O(new(){["id"]=S(),["cycleId"]=S(),["rider"]=S(),["app"]=S(),["period"]=S(),["status"]=S(),["platformGross"]=N(),["incentives"]=N(),["gross"]=N(),["companyCommission"]=N(),["entitlement"]=N(),["accountDeduction"]=N(),["allocatedVehicleCharge"]=N(),["sources"]=A(Free()),["lines"]=A(Free()),["blockers"]=A(S())},"id","cycleId","rider","app","status","gross","sources");
    public static OpenApiSchema Setting()=>O(new(){["id"]=S(),["kind"]=S(),["app"]=S(),["period"]=S(),["code"]=S(),["version"]=N(),["status"]=S(),["data"]=Free(),["createdBy"]=S(),["effectiveFrom"]=S(),["allowedActions"]=A(S())},"id","app","period","status","version","data");
    public static OpenApiSchema Rider()=>O(new(){["id"]=S(),["iqama"]=S(),["name"]=S(),["status"]=S(),["companyId"]=S(),["phone"]=S(),["chips"]=A(S())},"id");
    public static OpenApiSchema Journal()=>O(new(){["id"]=S(),["status"]=S(),["companyId"]=S(),["date"]=S(),["reference"]=S(),["description"]=S(),["journalEntryId"]=S(),["allowedActions"]=A(S()),["lines"]=A(O(new(){["accountCode"]=S(),["debit"]=N(),["credit"]=N()},"accountCode","debit","credit"))},"id","status");
    public static OpenApiSchema Campaign()=>O(new(){["id"]=S(),["title"]=S(),["status"]=S(),["expiresAt"]=S(),["path"]=S(),["kind"]=S()},"id");
    public static OpenApiSchema? Response(string method,string path)
    {
        bool get=method=="GET";
        if(path=="/api/pay/review-draft")return O(new(){["app"]=S(),["period"]=S(),["companyId"]=S(),["invoices"]=A(Review()),["blockers"]=A(O(new(){["code"]=S(),["message"]=S(),["fixAction"]=S(),["sourceLineId"]=S()},"code","message","fixAction","sourceLineId")),["unassignedOrders"]=A(Free()),["ridersNotFound"]=A(Free()),["evidence"]=A(Free()),["canCreate"]=B(),["totals"]=O(new(){["gross"]=N(),["deductions"]=N(),["vat"]=N()},"gross","deductions","vat")},"app","period","companyId","invoices","blockers","unassignedOrders","ridersNotFound","evidence","canCreate","totals");
        if(path=="/api/debts"&&get)return new(){OneOf=[A(Debt()),Page(Debt())]};
        if(path=="/api/riders"&&get)return new(){OneOf=[A(Rider()),Page(Rider())]};
        if(path=="/api/riders/lookup"&&get)return A(Rider());
        if(path.StartsWith("/api/campaigns"))return get&&path=="/api/campaigns"?new(){OneOf=[A(Campaign()),Page(Campaign())]}:Campaign();
        if(path.StartsWith("/api/finance/journals/manual"))return get&&path.EndsWith("/manual")?Page(Journal()):Journal();
        if(path=="/api/pay/cycles/{id}/lines")return Page(CycleLine());
        if(path.StartsWith("/api/pay/cycles")&&!path.Contains("/review"))return get?Page(Cycle()):Cycle();
        if(path.StartsWith("/api/platforms/ninja/invoices"))
        {
            if(path.EndsWith("/lines"))return A(InvoiceLine());
            if(path=="/api/platforms/ninja/invoices")return get?Page(Invoice()):new(){OneOf=[Invoice(),O(new(){["rows"]=A(Invoice()),["canCommit"]=B(),["fingerprint"]=S()},"rows","canCommit","fingerprint")]};
            return Invoice();
        }
        if(path=="/api/pay/settings"||path.StartsWith("/api/pay/settings/{id}")||path.StartsWith("/api/pay/apps/{app}/")&&new[]{"terms","schemes","eligibility"}.Any(s=>path.Contains("/"+s)))return method=="DELETE"?O(new(){["deleted"]=B(),["id"]=S()},"deleted","id"):get?Page(Setting()):Setting();
        return null;
    }
    public static void Describe(OpenApiOperation operation,string method,string path)
    {
        var schema=Response(method,path.Split('?')[0]);if(schema==null)return;
        operation.Responses??=new();operation.Responses["200"]=new OpenApiResponse{Description="Successful response (stored wire field names)",Content=new Dictionary<string,OpenApiMediaType>{["application/json"]=new(){Schema=schema}}};
    }
}
