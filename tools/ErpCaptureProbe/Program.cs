using System.Globalization;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;

// Local-only live capture proof for period 2026-10 / app ninja.
// Prints counts only (no names, Iqama, phones, IBANs, secrets). Never committed output.

string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string sourceFile=args.SkipWhile(a=>a!="--source-env").Skip(1).FirstOrDefault()
    ??Path.Combine(home,"Private/gate-data/erp-readonly.env");
string period=args.SkipWhile(a=>a!="--period").Skip(1).FirstOrDefault()??"2026-10";
string app=args.SkipWhile(a=>a!="--app").Skip(1).FirstOrDefault()??"ninja";
string outPath=args.SkipWhile(a=>a!="--out").Skip(1).FirstOrDefault()
    ??Path.Combine(home,".agents/reviews/M1-live-capture-proof.md");

var from=DateTime.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
string periodFrom=from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
string periodTo=from.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

// Same three Gate sponsors as Payroll.Api FinancialCore (codes + unified numbers only).
(string Code,string Unified)[] companies=[
    ("BW","7015658094"),
    ("EX","7034861059"),
    ("TR","7038745530"),
];
ISyncSponsor[] catalog=companies.Select(c=>(ISyncSponsor)new ProbeSponsor(c.Code,c.Code,c.Unified,"","", "")).ToArray();

string[] kinds=["ninja_daily","ninja_daily_deduction","platform_account","vehicle_assignment","rider_work_timeline","platform_day_usage","vehicle_expense","fuel_usage","housing_residence","vehicle"];

using var erp=Connections.Source(Connections.Env(sourceFile,"ERP_READONLY_DB"));
erp.Open();
SourceReader.VerifyReadOnly(erp);

var lines=new List<string>
{
    "# M1 live capture proof",
    "",
    $"- Period: `{period}` (`{periodFrom}` … `{periodTo}`)",
    $"- App: `{app}`",
    $"- ERP catalog: `db67927` read-only",
    $"- Generated: {DateTimeOffset.UtcNow:O} (UTC)",
    "",
    "## Per company",
    "",
};

foreach(var company in companies)
{
    Console.Error.WriteLine($"company={company.Code}");
    lines.Add($"### {company.Code}");
    lines.Add("");
    lines.Add("| Kind | Source total | Kept | Rejected | Pages |");
    lines.Add("|---|---:|---:|---:|---:|");

    // Expand riders who held company platform accounts in the period (A6).
    var profiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var employees=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using(var cmd=erp.CreateCommand())
    {
        cmd.CommandTimeout=30;
        cmd.Parameters.AddWithValue("@u",company.Unified);
        cmd.Parameters.AddWithValue("@pf",periodFrom);
        cmd.Parameters.AddWithValue("@pt",periodTo);
        cmd.Parameters.AddWithValue("@pc",ErpPlatformCode.Primary(app));
        cmd.CommandText="""
            SELECT DISTINCT rca.[RiderProfileId], rp.[EmployeeId]
            FROM [app].[RiderClientAssignments] rca
            INNER JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=rca.[PlatformRiderAccountId]
            INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId]
            INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId]
            LEFT JOIN [app].[RiderProfiles] rp ON rp.[Id]=rca.[RiderProfileId]
            WHERE s.[UnifiedNationalNumber]=@u AND rca.[IsDeleted]=0 AND pa.[IsDeleted]=0
              AND cp.[Code]=@pc
              AND rca.[EffectiveFrom] < DATEADD(day,1,CONVERT(date,@pt))
              AND (rca.[EffectiveTo] IS NULL OR rca.[EffectiveTo] >= CONVERT(date,@pf))
            """;
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            if(!reader.IsDBNull(0)){string p=reader.GetValue(0)?.ToString()??"";if(p!="")profiles.Add(p);}
            if(!reader.IsDBNull(1)){string e=reader.GetValue(1)?.ToString()??"";if(e!="")employees.Add(e);}
        }
    }

    // Sponsored riders for this company (employee ids via payroll would add more; ERP-only probe uses sponsor match).
    using(var cmd=erp.CreateCommand())
    {
        cmd.CommandTimeout=30;
        cmd.Parameters.AddWithValue("@u",company.Unified);
        cmd.CommandText="""
            SELECT e.[Id], rp.[Id]
            FROM [app].[Employees] e
            INNER JOIN [app].[Sponsors] s ON s.[Id]=e.[SponsorId]
            LEFT JOIN [app].[RiderProfiles] rp ON rp.[EmployeeId]=e.[Id] AND rp.[IsDeleted]=0
            WHERE s.[UnifiedNationalNumber]=@u AND e.[IsDeleted]=0
            """;
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            if(!reader.IsDBNull(0)){string e=reader.GetValue(0)?.ToString()??"";if(e!="")employees.Add(e);}
            if(!reader.IsDBNull(1)){string p=reader.GetValue(1)?.ToString()??"";if(p!="")profiles.Add(p);}
        }
    }

    var scope=new LiveOperationsQuery.Scope(false,[company.Unified],employees.ToList(),profiles.ToList(),[],null,null,false);
    // Prove sponsor match exists.
    using(var cmd=erp.CreateCommand())
    {
        cmd.CommandTimeout=15;
        cmd.Parameters.AddWithValue("@u",company.Unified);
        cmd.CommandText="SELECT TOP 1 1 FROM [app].[Sponsors] WHERE [UnifiedNationalNumber]=@u AND [IsDeleted]=0";
        if(cmd.ExecuteScalar() is null)
        {
            lines.Add($"| _(sponsor)_ | — | — | unmatched | — |");
            lines.Add("");
            continue;
        }
    }

    foreach(string kind in kinds)
    {
        if(!LiveOperationsQuery.HasKind(kind))continue;
        var rejected=new Dictionary<string,int>(StringComparer.Ordinal);
        var kept=new HashSet<string>(StringComparer.Ordinal);
        string after="";
        long? firstTotal=null;
        int pages=0,sourceWalked=0;
        const int pageSize=500,pageCap=10000;
        while(true)
        {
            pages++;
            if(pages>pageCap)throw new InvalidOperationException("page_cap:"+kind);
            var request=new LiveOperationsQuery.PageRequest(1,pageSize,"id","",0,pageSize,"",periodFrom,periodTo,app,after);
            var result=LiveOperationsQuery.Page(erp,kind,request,scope,catalog,CancellationToken.None,firstTotal);
            firstTotal??=result.Total;
            var projected=LiveOperationsSource.Project(result.Snapshot,catalog,kind);
            sourceWalked+=projected.SourceRowCount;
            foreach(var (reason,count) in projected.Rejected)
                rejected[reason]=rejected.GetValueOrDefault(reason)+count;
            foreach(var doc in projected.Documents)
            {
                string id=doc["id"]?.ToString()?.Trim()??"";if(id!="")kept.Add(id);
            }
            if(projected.LastSourceId==""||projected.SourceRowCount==0)break;
            if(sourceWalked>=firstTotal.Value)break;
            after=projected.LastSourceId;
        }
        int rejectedSum=rejected.Values.Sum();
        string rejectCell=rejectedSum==0?"0":string.Join(", ",rejected.OrderBy(kv=>kv.Key).Select(kv=>$"{kv.Key}:{kv.Value}"));
        lines.Add($"| `{kind}` | {firstTotal??0} | {kept.Count} | {rejectCell} | {pages} |");
        Console.Error.WriteLine($"  {kind}: source={firstTotal} kept={kept.Count} rejected={rejectedSum} pages={pages}");
    }
    lines.Add("");
}

lines.Add("## Notes");
lines.Add("");
lines.Add("- Counts only; no PII.");
lines.Add("- Expected order of magnitude (all companies, 2026-10 Ninja): daily ~3629, assignments ~1387, vehicle assignments ~890, fuel ~597.");
lines.Add("- Keyset pages beyond 500 rows prove A5.");
lines.Add("");

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath,string.Join('\n',lines));
Console.Error.WriteLine("wrote "+outPath);

sealed record ProbeSponsor(string Id,string Code,string UnifiedNumber,string NameAr,string ShortNameAr,string CommercialReg):ISyncSponsor;
