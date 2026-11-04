using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace Payroll;

public sealed record ReportDocument(string Id,string Kind,string Body);
public sealed record ReportColumn(string Name,string SqlType);
public sealed record ReportTable(string Name,string Kind,ReportColumn[] Extra);
public sealed record ReportRow(string Table,string Kind,JsonObject Values);
public static partial class Reporting
{
    static ReportColumn C(string name,string type="nvarchar(128)")=>new(name,type);
    public static readonly ReportColumn[] Common=[C("RowKey","char(64)"),C("SourceRecordId"),C("CompanyId"),C("RiderId","nvarchar(64)"),C("Platform","nvarchar(40)"),C("Period","char(7)"),C("SourceId","nvarchar(512)"),C("SourceSha","char(64)")];
    public static readonly ReportTable[] Tables=[
        new("Riders","rider",[C("Name","nvarchar(300)"),C("Status","nvarchar(40)"),C("EngagementType","nvarchar(40)"),C("IsDeliveryRider","bit"),C("CityId")]),
        new("Companies","company",[C("Code","nvarchar(64)"),C("Name","nvarchar(300)")]),
        new("PlatformAccounts","platform_account",[C("ExternalAccountId"),C("RegisteredRiderId","nvarchar(64)"),C("CityId"),C("StartAt","datetimeoffset(7)"),C("EndAt","datetimeoffset(7)")]),
        new("AccountAssignments","platform_account",[C("AccountId"),C("FromAt","datetimeoffset(7)"),C("ToAt","datetimeoffset(7)"),C("Status","nvarchar(40)")]),
        new("Vehicles","vehicle",[C("AssetNumber"),C("PlateNumber"),C("VehicleType","nvarchar(40)"),C("OwnershipType","nvarchar(40)"),C("CityId")]),
        new("VehicleAssignments","vehicle_assignment",[C("VehicleId"),C("FromAt","datetimeoffset(7)"),C("ToAt","datetimeoffset(7)"),C("StartOdometer","decimal(19,4)"),C("EndOdometer","decimal(19,4)")]),
        new("Debts","debt",[C("Category","nvarchar(64)"),C("Status","nvarchar(40)"),C("TotalAmount","decimal(19,4)"),C("PaidAmount","decimal(19,4)"),C("RemainingBalance","decimal(19,4)"),C("MonthlyInstallment","decimal(19,4)")]),
        new("DebtSettlements","debt_settlement",[C("DebtId"),C("JournalId"),C("Method","nvarchar(64)"),C("CollectedAmount","decimal(19,4)"),C("WaivedAmount","decimal(19,4)"),C("RemainingAfter","decimal(19,4)")]),
        new("JournalLines","journal",[C("JournalId"),C("AccountCode","nvarchar(64)"),C("PlatformAccountId"),C("DebtId"),C("Debit","decimal(19,4)"),C("Credit","decimal(19,4)"),C("Chart","nvarchar(20)"),C("SourceType","nvarchar(64)")]),
        new("PayCycles","pay_cycle",[C("Stage","int"),C("Status","nvarchar(40)"),C("PostingPeriod","char(7)"),C("JournalId")]),
        new("PayCycleLines","pay_cycle_line",[C("CycleId"),C("SchemeId"),C("Orders","int"),C("Gross","decimal(19,4)"),C("PlatformGross","decimal(19,4)"),C("AccountDeduction","decimal(19,4)"),C("Incentives","decimal(19,4)"),C("CompanyCommission","decimal(19,4)"),C("VehicleCharge","decimal(19,4)"),C("OperatingCost","decimal(19,4)"),C("Entitlement","decimal(19,4)"),C("NetBeforePersonal","decimal(19,4)"),C("Status","nvarchar(40)")]),
        new("PlatformInvoices","platform_invoice",[C("ApprovalStatus","nvarchar(40)"),C("Reference","nvarchar(512)"),C("AmountExclVat","decimal(19,4)"),C("Vat","decimal(19,4)"),C("DeductionsTotal","decimal(19,4)"),C("JournalId")]),
        new("PlatformInvoiceLines","platform_invoice_line",[C("InvoiceId"),C("PlatformAccountId"),C("LineType","nvarchar(40)"),C("Orders","int"),C("BaseGross","decimal(19,4)"),C("Incentives","decimal(19,4)"),C("DeductionAmount","decimal(19,4)")]),
        new("Accounts","@coa",[C("Code","nvarchar(64)"),C("Name","nvarchar(300)"),C("ParentCode","nvarchar(64)"),C("Nature","nvarchar(20)"),C("Status","nvarchar(40)"),C("Posting","bit")])
    ];
    public static string Sha(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static JsonNode? Text(JsonObject source,string key)=>source.S(key)==""?null:JsonValue.Create(source.S(key));
    static JsonNode? Date(JsonObject source,string key)
    {string value=source.S(key);if(value=="")return null;if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date))throw new InvalidOperationException("report_projection_invalid_date");return JsonValue.Create(date.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
    static JsonNode? Number(JsonObject source,string key,bool integer=false)
    {
        if(source.S(key)=="")return null;
        if(!decimal.TryParse(source.S(key),NumberStyles.Number,CultureInfo.InvariantCulture,out var n)||n!=decimal.Round(n,4)||integer&&(n!=decimal.Truncate(n)||n>int.MaxValue||n<int.MinValue))throw new InvalidOperationException("report_projection_invalid_number");return integer?JsonValue.Create((int)n):JsonValue.Create(n);
    }
    public static ReportDocument Chart()=>new("@coa","@coa",new JsonArray(FinanceViews.Accounts.Select(a=>(JsonNode?)a.Clone()).ToArray()).ToJsonString());
    public static IReadOnlyList<ReportRow> Project(ReportDocument doc)
    {
        if(!Tables.Any(t=>t.Kind==doc.Kind))return [];
        var output=new List<ReportRow>();string sha=Sha(doc.Body);var parsed=JsonNode.Parse(doc.Body)!;var root=parsed as JsonObject??new();
        void Add(string table,JsonObject data,JsonObject? owner=null,string ordinal="0")
        {
            owner??=root;var row=new JsonObject{["RowKey"]=Sha(doc.Id+"\n"+ordinal),["SourceRecordId"]=doc.Id,["CompanyId"]=Text(owner,owner.S("companyId")!=""?"companyId":owner.S("creditorSponsorId")!=""?"creditorSponsorId":owner.S("sponsorId")!=""?"sponsorId":"originSponsorId"),["RiderId"]=Text(owner,owner.S("rider")!=""?"rider":owner.S("riderId")!=""?"riderId":"iqama"),["Platform"]=Text(owner,owner.S("platform")!=""?"platform":owner.S("app")!=""?"app":"platformCode"),["Period"]=Text(owner,"period"),["SourceId"]=Text(owner,owner.S("sourceRef")!=""?"sourceRef":"sourceId"),["SourceSha"]=sha};
            foreach(var p in data)row[p.Key]=p.Value?.DeepClone();foreach(var column in Tables.Single(t=>t.Name==table).Extra)if(!row.ContainsKey(column.Name))row[column.Name]=null;output.Add(new(table,doc.Kind,row));
        }
        JsonObject Fields(params (string Column,string Field,string Type)[] pairs)
        {var o=new JsonObject();foreach(var p in pairs)o[p.Column]=p.Type switch{"n"=>Number(root,p.Field),"i"=>Number(root,p.Field,true),"d"=>Date(root,p.Field),"b"=>JsonValue.Create(root.B(p.Field)),_=>Text(root,p.Field)};return o;}
        switch(doc.Kind)
        {
            case "rider":Add("Riders",Fields(("Name","name","s"),("Status","status","s"),("EngagementType","engagementType","s"),("IsDeliveryRider","isDeliveryRider","b"),("CityId","cityId","s")));break;
            case "company":Add("Companies",new(){["Code"]=Text(root,"code"),["Name"]=Text(root,root.S("nameAr")!=""?"nameAr":"name")});break;
            case "platform_account":
                Add("PlatformAccounts",Fields(("ExternalAccountId","externalAccountId","s"),("RegisteredRiderId","registeredRiderId","s"),("CityId","cityId","s"),("StartAt","startDate","d"),("EndAt","endDate","d")));
                int a=0;foreach(var child in (root["assignments"] as JsonArray??[]).OfType<JsonObject>())
                {var owner=root.Clone();owner["riderId"]=child.S("riderId");Add("AccountAssignments",new(){["AccountId"]=doc.Id,["FromAt"]=Date(child,"RawEffectiveFrom"),["ToAt"]=Date(child,"RawEffectiveTo"),["Status"]=Text(child,"Status")},owner,(a++).ToString(CultureInfo.InvariantCulture));}break;
            case "vehicle":Add("Vehicles",Fields(("AssetNumber","assetNumber","s"),("PlateNumber","plateNumberAr","s"),("VehicleType","vehicleType","s"),("OwnershipType","ownershipType","s"),("CityId","cityId","s")));break;
            case "vehicle_assignment":Add("VehicleAssignments",Fields(("VehicleId","vehicleId","s"),("FromAt","startedAt","d"),("ToAt","endedAt","d"),("StartOdometer","startOdometer","n"),("EndOdometer","endOdometer","n")));break;
            case "debt":Add("Debts",Fields(("Category","category","s"),("Status","status","s"),("TotalAmount","totalAmount","n"),("PaidAmount","paidAmount","n"),("RemainingBalance","remainingBalance","n"),("MonthlyInstallment","monthlyInstallment","n")));break;
            case "debt_settlement":Add("DebtSettlements",Fields(("DebtId","debtId","s"),("JournalId","journalEntryId","s"),("Method","method","s"),("CollectedAmount","collectedAmount","n"),("WaivedAmount","waivedAmount","n"),("RemainingAfter","remainingAfter","n")));break;
            case "journal":
                int j=0;foreach(var line in (root["lines"] as JsonArray??[]).OfType<JsonObject>())
                {var owner=root.Clone();foreach(string key in new[]{"sponsorId","iqama","platform","sourceId"})if(line.S(key)!="")owner[key]=line[key]?.DeepClone();owner.Remove("companyId");owner.Remove("creditorSponsorId");Add("JournalLines",new(){["JournalId"]=doc.Id,["AccountCode"]=Text(line,"accountCode"),["PlatformAccountId"]=Text(line,"platformAccountId"),["DebtId"]=Text(line,"debtId"),["Debit"]=Number(line,"debit"),["Credit"]=Number(line,"credit"),["Chart"]=Text(root,"chart"),["SourceType"]=Text(root,"transactionType")},owner,(j++).ToString(CultureInfo.InvariantCulture));}break;
            case "pay_cycle":Add("PayCycles",Fields(("Stage","stage","i"),("Status","status","s"),("PostingPeriod","postingPeriod","s"),("JournalId","journalEntryId","s")));break;
            case "pay_cycle_line":Add("PayCycleLines",Fields(("CycleId","cycleId","s"),("SchemeId","schemeId","s"),("Orders","orders","i"),("Gross","gross","n"),("PlatformGross","platformGross","n"),("AccountDeduction","accountDeduction","n"),("Incentives","incentives","n"),("CompanyCommission","companyCommission","n"),("VehicleCharge","allocatedVehicleCharge","n"),("OperatingCost","operatingCost","n"),("Entitlement","entitlement","n"),("NetBeforePersonal","netBeforePersonal","n"),("Status","status","s")));break;
            case "platform_invoice":Add("PlatformInvoices",Fields(("ApprovalStatus","approvalStatus","s"),("Reference","reference","s"),("AmountExclVat","amountExclVat","n"),("Vat","vat","n"),("DeductionsTotal","deductionsTotal","n"),("JournalId","journalEntryId","s")));break;
            case "platform_invoice_line":Add("PlatformInvoiceLines",Fields(("InvoiceId","invoiceId","s"),("PlatformAccountId","platformAccountId","s"),("LineType","lineType","s"),("Orders","orders","i"),("BaseGross","baseGross","n"),("Incentives","incentives","n"),("DeductionAmount","deductionAmount","n")));break;
            case "@coa":foreach(var account in parsed.AsArray().OfType<JsonObject>())Add("Accounts",new(){["Code"]=Text(account,"code"),["Name"]=Text(account,"nameAr"),["ParentCode"]=Text(account,"parent"),["Nature"]=Text(account,"nature"),["Status"]=Text(account,"status"),["Posting"]=account.B("posting")},new JsonObject(),account.S("code"));break;
        }
        return output;
    }
    public static string RowsSha(IEnumerable<ReportRow> rows)=>Sha(string.Join("\n",rows.OrderBy(r=>r.Table,StringComparer.Ordinal).ThenBy(r=>r.Values.S("RowKey"),StringComparer.Ordinal).Select(r=>r.Table+":"+FinancialImports.Canonical(r.Values))));
    public static List<ReportDocument> Snapshot(DbConnection c,DbTransaction? tx=null)
    {
        string kinds=string.Join(",",Tables.Select(t=>t.Kind).Where(k=>k!="@coa").Distinct().Select(k=>"'"+k+"'"));
        using var cmd=Store.Command(c,tx,"SELECT id,kind,body FROM records WHERE kind IN ("+kinds+") ORDER BY id");using var reader=cmd.ExecuteReader();var result=new List<ReportDocument>();while(reader.Read())if(Tables.Any(t=>t.Kind==reader.GetString(1)))result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2)));return result;
    }
}
