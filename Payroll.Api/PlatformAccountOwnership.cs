using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-12b (M-0684): resolve invoice-period owner from ops.platform_account_ownership;
/// snapshot account→owner; wrong owner blocked.</summary>
public static class PlatformAccountOwnership
{
    public const string SourceErp="erp_snapshot",SourceManual="manual",SourceImport="import";

    public static void Ensure(DbConnection c,DbTransaction? tx=null)=>SchemaOpsPlatformAccount.Ensure(c,tx);

    public static JsonObject RecordOwnership(DbConnection c,DbTransaction tx,string erpAccountId,string companyId,string validFrom,string? validTo,string source,string actor)
    {
        Ensure(c,tx);
        J.Need(source is SourceErp or SourceManual or SourceImport,"مصدر ملكية غير معروف.",400);
        string from=Day(validFrom);string? to=string.IsNullOrWhiteSpace(validTo)?null:Day(validTo!);
        // Close any open row that starts before the new interval.
        if(to==null)
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"ops","platform_account_ownership")}
                SET valid_to=@from,updated_by=@by,updated_at=@at
                WHERE erp_platform_account_id=@acc AND valid_to IS NULL AND valid_from<@from
                """,("@from",from),("@by",actor),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),("@acc",erpAccountId));
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"ops","platform_account_ownership")}
            (id,erp_platform_account_id,company_id,valid_from,valid_to,source,taken_at,created_by,created_at)
            VALUES(@id,@acc,@c,@from,@to,@src,@at,@by,@at)
            """,("@id",id),("@acc",erpAccountId),("@c",companyId),("@from",from),
            ("@to",(object?)to??DBNull.Value),("@src",source),
            ("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),("@by",actor));
        return new(){["id"]=id,["erpPlatformAccountId"]=erpAccountId,["companyId"]=companyId,["validFrom"]=from,["validTo"]=to??"",["source"]=source};
    }

    /// <summary>Owner for an invoice period = the ownership row whose [valid_from, valid_to) covers period end (or period_month).</summary>
    public static string? ResolveOwnerCompany(DbConnection c,DbTransaction? tx,string erpAccountId,string periodMonth)
    {
        Ensure(c,tx);
        string asOf=Day(periodMonth);
        // Prefer period end of month when only yyyy-MM-01 is given.
        if(asOf.EndsWith("-01",StringComparison.Ordinal)&&DateOnly.TryParse(asOf,CultureInfo.InvariantCulture,DateTimeStyles.None,out var month))
            asOf=month.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT company_id FROM {SchemaSql.Q(c,"ops","platform_account_ownership")}
            WHERE erp_platform_account_id=@acc AND valid_from<=@asof AND (valid_to IS NULL OR valid_to>@asof)
            ORDER BY valid_from DESC
            """;
        Add(cmd,"@acc",erpAccountId);Add(cmd,"@asof",asOf);
        return cmd.ExecuteScalar()?.ToString();
    }

    public static void DemandOwnerMatches(DbConnection c,DbTransaction? tx,string erpAccountId,string periodMonth,string invoiceCompanyId)
    {
        string? owner=ResolveOwnerCompany(c,tx,erpAccountId,periodMonth);
        J.Need(owner!=null,"لا تاريخ ملكية لحساب المنصة في فترة الفاتورة.",409);
        J.Need(string.Equals(owner,invoiceCompanyId,StringComparison.Ordinal),"مالك الحساب في تاريخ الملكية لا يطابق شركة الفاتورة.",409);
    }

    public static JsonObject SnapshotOwners(DbConnection c,DbTransaction? tx,string periodMonth,IEnumerable<(string AccountId,string AccountKey)> accounts,string invoiceCompanyId)
    {
        var rows=new JsonArray();
        foreach(var (account,key) in accounts)
        {
            DemandOwnerMatches(c,tx,account,periodMonth,invoiceCompanyId);
            rows.Add(new JsonObject{["erpPlatformAccountId"]=account,["accountKey"]=key,["ownerCompanyId"]=invoiceCompanyId});
        }
        return new(){["periodMonth"]=periodMonth,["invoiceCompanyId"]=invoiceCompanyId,["accounts"]=rows};
    }

    static string Day(string value)=>value.Length>=10?value[..10]:value;
    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}
