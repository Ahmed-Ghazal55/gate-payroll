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
    ];
    public static IEnumerable<string> Kinds => Catalog.Select(c=>c.Kind);
    public static IEnumerable<SourceTable> Tables => Catalog.Select(c=>new SourceTable(c.Schema,c.Table,c.Fields,true));
    public static string Table(string kind)=>Catalog.FirstOrDefault(c=>c.Kind==kind)?.Table??"";
    public static void AddToPlan(SourceSnapshot source,ImportPlan plan,Dictionary<string,string> companies,Dictionary<string,string> employees,Dictionary<string,string> profiles,Func<string,string> city)
    {
        List<JsonObject> Rows(string table)=>source.Tables.GetValueOrDefault(table)??[];
        JsonObject? Find(string table,string id)=>id==""?null:Rows(table).SingleOrDefault(r=>r.S("Id")==id);
        string Key(string kind,string id)=>id==""?"":"erp:"+kind+":"+id;
        string Rider(JsonObject row)
        {
            string a=employees.GetValueOrDefault(row.S("EmployeeId"))??"",b=profiles.GetValueOrDefault(row.S("RiderProfileId"))??"";
            if(row.S("EmployeeId")!=""&&a==""||row.S("RiderProfileId")!=""&&b==""||a!=""&&b!=""&&a!=b)return "";
            return a!=""?a:b;
        }
        foreach(var contract in Catalog)
        {
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
                plan.Documents.Add(new(contract.Kind,doc));
            }
        }
    }
}
