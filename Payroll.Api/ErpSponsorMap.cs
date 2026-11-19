using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
namespace Payroll;

/// <summary>Live read-only admin map: our company code → ERP sponsor match by UnifiedNationalNumber.</summary>
public static class ErpSponsorMap
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/system/erp-sponsor-map",(HttpContext ctx,IConfiguration config)=>
        {
            if(!AccessPolicy.Administrator(ctx.User))throw new ApiError("خريطة الكفلاء لمسؤول النظام فقط.",403);
            string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
            if(string.IsNullOrWhiteSpace(sourceConnection))throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
            using var erp=ErpImport.Connections.Source(sourceConnection);erp.Open();ErpImport.SourceReader.VerifyReadOnly(erp);
            var matched=new HashSet<string>(StringComparer.Ordinal);
            using(var cmd=erp.CreateCommand())
            {
                cmd.CommandTimeout=ErpImport.LiveOperationsQuery.CommandTimeoutSeconds;
                cmd.CommandText="SELECT [UnifiedNationalNumber] FROM [app].[Sponsors] WHERE [IsDeleted]=0 AND [UnifiedNationalNumber] IS NOT NULL AND [UnifiedNationalNumber]<>''";
                using var reader=cmd.ExecuteReader();
                while(reader.Read())
                {
                    string? value=reader.IsDBNull(0)?null:reader.GetValue(0)?.ToString();
                    if(!string.IsNullOrWhiteSpace(value))matched.Add(value.Trim());
                }
            }
            return FinancialCore.Sponsors.Select(s=>new{
                code=s.Code,
                matched=!string.IsNullOrWhiteSpace(s.UnifiedNumber)&&matched.Contains(s.UnifiedNumber)
            }).ToList();
        }).WithTags("Administration").WithSummary("Administrator-only company-code to ERP-sponsor match flags (no names)");
    }
}
