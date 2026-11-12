using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// One-kind paged SELECT against logistics ERP. Table/column names come only from the allowlist.
/// Values are always parameters. Never writes.
/// </summary>
public static class LiveOperationsQuery
{
    public const int MaxPageSize=200,CommandTimeoutSeconds=15,ParentBatchSize=500;
    public sealed record PageRequest(int Page,int PageSize,string Sort,string Query,int Skip,int Take);
    public sealed record Scope(bool AllCompanies,IReadOnlyList<string> SponsorCommercialRegs,IReadOnlyList<string> EmployeeIds,IReadOnlyList<string> RiderProfileIds,IReadOnlyList<string> PlatformCodes,string? RiderAccountEmployeeId=null,IReadOnlyList<string>? RiderAccountProfileIds=null);
    public sealed record PageResult(SourceSnapshot Snapshot,long Total,PageRequest Request,DateTimeOffset AsOf);
    public sealed record ParentNeed(string Schema,string Table,string KeyColumn,Func<JsonObject,string> IdFromRow);
    sealed record KindSpec(string Kind,string Schema,string Table,string[] Columns,string[] SearchColumns,string? CompanyColumn,string? RiderEmployeeColumn,string? RiderProfileColumn,bool ViaVehicleSponsor,bool ViaPlatformAccountSponsor,bool HousingByResidence,bool PlatformCodeJoin,ParentNeed[] Parents);

    static SourceTable Table(string schema,string name)
    {
        var match=SourceReader.Catalog.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??PullContracts.Tables.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??throw new ImportFailure("live_ops_unknown_table:"+schema+"."+name);
        return match;
    }
    static string[] Cols(string schema,string name)=>Table(schema,name).Fields.Split(' ',StringSplitOptions.RemoveEmptyEntries);

    static readonly Dictionary<string,KindSpec> Specs=BuildSpecs();
    static Dictionary<string,KindSpec> BuildSpecs()
    {
        ParentNeed P(string schema,string table,Func<JsonObject,string> id)=>new(schema,table,"Id",id);
        var map=new Dictionary<string,KindSpec>(StringComparer.Ordinal);
        void Add(KindSpec s)=>map[s.Kind]=s;
        Add(new("city","platform","GlobalCities",Cols("platform","GlobalCities"),["Code","NameAr","NameEn"],null,null,null,false,false,false,false,[P("app","OperatingCities",_=>"")]));
        Add(new("housing","app","Housing",Cols("app","Housing"),["Code","NameAr","NameEn"],null,null,null,false,false,true,false,[
            P("app","HousingFloors",r=>r.S("Id")),P("app","HousingRooms",r=>r.S("Id")),P("app","HousingExternalOccupants",r=>r.S("Id")),P("app","HousingResidencePeriods",r=>r.S("Id"))]));
        Add(new("housing_residence","app","HousingResidencePeriods",Cols("app","HousingResidencePeriods"),[],null,"EmployeeId",null,false,false,false,false,[
            P("app","HousingRooms",r=>r.S("RoomId")),P("app","Housing",r=>"" )]));
        Add(new("vehicle","app","Vehicles",Cols("app","Vehicles"),["AssetNumber","SerialNumber","PlateNumberAr","PlateNumberEn","Vin","ChassisNumber"],"SponsorId",null,null,false,false,false,false,[
            P("app","VehicleRegistrations",r=>r.S("Id")),P("app","VehicleInsurancePolicies",r=>r.S("Id"))]));
        Add(new("vehicle_assignment","app","RiderVehicleAssignments",Cols("app","RiderVehicleAssignments"),[],null,null,"RiderProfileId",false,false,false,false,[
            P("app","Vehicles",r=>r.S("VehicleId")),P("app","RealRiders",r=>r.S("Id"))]));
        Add(new("custody_handover","app","RiderVehicleAssignmentEvents",Cols("app","RiderVehicleAssignmentEvents"),[],null,null,null,false,false,false,false,[
            P("app","RiderVehicleAssignments",r=>r.S("RiderVehicleAssignmentId")),P("app","Vehicles",r=>""),P("app","RealRiders",r=>"")]));
        Add(new("legal_case","app","HrLegalCases",Cols("app","HrLegalCases"),["CaseNumber","PersonName","Details"],"SponsorId","EmployeeId","RiderProfileId",false,false,false,false,[
            P("app","HrLegalCaseHearings",r=>r.S("Id"))]));
        Add(new("platform_account","app","PlatformRiderAccounts",Cols("app","PlatformRiderAccounts"),["Code","ExternalAccountId"],"SponsorId","RegisteredEmployeeId",null,false,false,false,true,[
            P("platform","ClientPlatforms",r=>r.S("ClientPlatformId")),P("app","RiderClientAssignments",r=>r.S("Id")),P("app","PlatformAccountRegistrations",r=>r.S("Id"))]));
        Add(new("vehicle_operational_status","app","VehicleOperationalStatusPeriods",Cols("app","VehicleOperationalStatusPeriods"),["Status","Reason","ReasonCode"],null,null,null,true,false,false,false,[
            P("app","Vehicles",r=>r.S("VehicleId"))]));
        Add(new("platform_day_usage","app","PlatformAccountDayUsages",Cols("app","PlatformAccountDayUsages"),[],null,null,null,false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId")),P("platform","ClientPlatforms",r=>"")]));
        Add(new("rider_work_timeline","app","RiderClientAssignments",Cols("app","RiderClientAssignments"),[],null,null,"RiderProfileId",false,true,false,true,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("platform","ClientPlatforms",r=>""),P("app","RiderAssignmentEvents",r=>r.S("Id")),P("app","PlatformAccountDayUsages",r=>r.S("Id"))]));
        Add(new("custody_equipment","maintenance","RiderInventoryIssues",Cols("maintenance","RiderInventoryIssues"),["IssueNumber"],null,null,"RiderProfileId",false,false,false,false,[
            P("maintenance","RiderInventoryIssueLines",r=>r.S("Id"))]));
        Add(new("vehicle_expense","maintenance","VehicleExpenses",Cols("maintenance","VehicleExpenses"),[],null,null,"RiderProfileId",true,false,false,false,[
            P("app","Vehicles",r=>r.S("VehicleId"))]));
        Add(new("chefz_account","chefz","AccountDetails",Cols("chefz","AccountDetails"),["ReportIdNumber","AccountType","VehicleType","DashboardStatus"],null,null,null,false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("platform","ClientPlatforms",r=>"")]));
        Add(new("chefz_account_type_change","chefz","AccountTypeChanges",Cols("chefz","AccountTypeChanges"),[],null,null,null,false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId"))]));
        Add(new("chefz_daily","chefz","DailyPerformance",Cols("chefz","DailyPerformance"),[],null,null,"RiderProfileId",false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId"))]));
        Add(new("chefz_settlement","chefz","WalletSettlements",Cols("chefz","WalletSettlements"),[],null,null,"RiderProfileId",false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId"))]));
        Add(new("chefz_order_row","chefz","ImportedOrderRows",Cols("chefz","ImportedOrderRows"),["OrderId"],null,null,null,false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId"))]));
        Add(new("ninja_daily","ninja","NinjaDailyPerformance",Cols("ninja","NinjaDailyPerformance"),["CaptainId"],null,null,"RiderProfileId",false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId"))]));
        Add(new("ninja_daily_deduction","ninja","NinjaDailyDeductions",Cols("ninja","NinjaDailyDeductions"),["CaptainId"],null,null,"RiderProfileId",false,true,false,false,[
            P("app","PlatformRiderAccounts",r=>r.S("PlatformRiderAccountId")),P("app","RiderClientAssignments",r=>r.S("RiderClientAssignmentId"))]));
        Add(new("ninja_evaluation_snapshot","ninja","EvaluationSnapshots",Cols("ninja","EvaluationSnapshots"),["Reason"],null,null,null,false,false,false,false,[]));
        Add(new("jahez_commission_policy","jahez_v2","CommissionPolicies",Cols("jahez_v2","CommissionPolicies"),[],null,null,null,false,false,false,false,[
            P("jahez_v2","AccountSessions",r=>r.S("HandoverId")),P("app","PlatformRiderAccounts",r=>"")]));
        Add(new("jahez_daily_order","jahez_v2","DailyOrders",Cols("jahez_v2","DailyOrders"),[],null,null,null,false,false,false,false,[
            P("jahez_v2","AccountSessions",r=>r.S("HandoverId")),P("app","PlatformRiderAccounts",r=>"")]));
        return map;
    }

    public static bool HasKind(string kind)=>kind=="sponsorship_period"||Specs.ContainsKey(kind);
    static KindSpec SpecFor(string kind)=>Specs.TryGetValue(kind,out var s)?s:throw new ImportFailure("live_ops_kind_unsupported:"+kind);
    public static string MainTable(string kind)=>kind=="sponsorship_period"?"":SpecFor(kind).Table;
    public static IReadOnlyList<string> AllowlistedColumns(string kind)=>kind=="sponsorship_period"?[]:SpecFor(kind).Columns;
    /// <summary>SQL text for tests: identifiers only from the allowlist; values are parameters.</summary>
    public static (string CountSql,string PageSql,IReadOnlyList<string> ParameterNames) PreviewSql(string kind,Scope scope,PageRequest page)
    {
        if(kind=="sponsorship_period")return ("SELECT CAST(0 AS bigint)","SELECT CAST(NULL AS uniqueidentifier) AS [Id] WHERE 1=0",[]);
        var spec=SpecFor(kind);var cmd=new SqlCommand();
        var where=BuildWhere(spec,scope,page.Query,cmd,out _);
        string from=FromSql(spec);string select=SelectSql(spec);
        string order=page.Sort=="-id"?"ORDER BY [Id] DESC":"ORDER BY [Id] ASC";
        string count=$"SELECT COUNT_BIG(*) FROM {from}{where}";
        string pageSql=$"SELECT {select} FROM {from}{where} {order} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        var names=cmd.Parameters.Cast<SqlParameter>().Select(p=>p.ParameterName).Append("@skip").Append("@take").ToList();
        return (count,pageSql,names);
    }

    public static PageRequest ParseRequest(IReadOnlyDictionary<string,string> query)
    {
        int Positive(string key,int fallback)
        {
            if(!query.TryGetValue(key,out var raw)||string.IsNullOrWhiteSpace(raw))return fallback;
            if(!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out int value)||value<1)
                throw new ImportFailure("invalid_page");
            return value;
        }
        int page=Positive("page",1),pageSize=Math.Min(Positive("pageSize",50),MaxPageSize);
        string sort=(query.TryGetValue("sort",out var s)?s:"").Trim();
        if(sort.Length>0&&sort is not ("id" or "-id"))throw new ImportFailure("invalid_live_sort");
        if(sort.Length==0)sort="id";
        string q="";
        if(query.TryGetValue("q",out var q1)&&!string.IsNullOrWhiteSpace(q1))q=q1.Trim();
        else if(query.TryGetValue("search",out var q2)&&!string.IsNullOrWhiteSpace(q2))q=q2.Trim();
        int skip=checked((page-1)*pageSize);
        return new(page,pageSize,sort,q,skip,pageSize);
    }

    public static PageResult Page(DbConnection source,string kind,PageRequest request,Scope scope,IReadOnlyList<ISyncSponsor> sponsors,CancellationToken cancellationToken=default)
    {
        DateTimeOffset asOf=DateTimeOffset.UtcNow;
        if(kind=="sponsorship_period")
            return new(new([],asOf,asOf,"selected_empty","selected"),0,request,asOf);
        if(source is not SqlConnection)throw new ImportFailure("live_ops_requires_sql");
        var spec=SpecFor(kind);
        using var countCmd=source.CreateCommand();countCmd.CommandTimeout=CommandTimeoutSeconds;
        using var countCancel=SyncCancellation.Register(countCmd,cancellationToken);
        var where=BuildWhere(spec,scope,request.Query,countCmd,out var needsSponsors);
        countCmd.CommandText=$"SELECT COUNT_BIG(*) FROM {FromSql(spec)}{where}";
        long total=Convert.ToInt64(countCmd.ExecuteScalar()??0,CultureInfo.InvariantCulture);

        using var pageCmd=source.CreateCommand();pageCmd.CommandTimeout=CommandTimeoutSeconds;
        using var pageCancel=SyncCancellation.Register(pageCmd,cancellationToken);
        where=BuildWhere(spec,scope,request.Query,pageCmd,out _);
        Add(pageCmd,"@skip",request.Skip);Add(pageCmd,"@take",request.Take);
        bool aliased=UsesAlias(spec);
        string orderCol=aliased?"t.[Id]":"[Id]";
        string order=request.Sort=="-id"?$"ORDER BY {orderCol} DESC":$"ORDER BY {orderCol} ASC";
        pageCmd.CommandText=$"SELECT {SelectSql(spec)} FROM {FromSql(spec)}{where} {order} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        var pageRows=Read(pageCmd,cancellationToken);
        var tables=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal){[spec.Table]=pageRows};

        // Mapping parents always needed for ImportPlan projection.
        Ensure(tables,source,"app","Sponsors",cancellationToken);
        EnsureOptional(tables,source,"platform","CompanyProfile",cancellationToken);
        EnsureOptional(tables,source,"platform","GlobalCities",cancellationToken);
        EnsureOptional(tables,source,"app","OperatingCities",cancellationToken);
        LoadPageParents(source,spec,pageRows,tables,cancellationToken);
        LoadRiderMaps(source,spec,pageRows,tables,cancellationToken);
        if(spec.PlatformCodeJoin||spec.Kind is "platform_account" or "rider_work_timeline")
            EnsureOptional(tables,source,"platform","ClientPlatforms",cancellationToken);

        var snapshot=new SourceSnapshot(tables,asOf,DateTimeOffset.UtcNow,"paged_live_read","selected");
        // Touch sponsors so ImportPlan.Build can match commercial regs (already loaded).
        _=sponsors;
        return new(snapshot,total,request,snapshot.FinishedAt);
    }

    static bool UsesAlias(KindSpec spec)=>spec.ViaVehicleSponsor||spec.ViaPlatformAccountSponsor||spec.HousingByResidence||(spec.PlatformCodeJoin&&spec.Kind=="platform_account")||spec.Kind is "jahez_commission_policy" or "jahez_daily_order";
    static string FromSql(KindSpec spec)
    {
        string main=$"[{spec.Schema}].[{spec.Table}] t";
        if(spec.ViaVehicleSponsor)return main+" INNER JOIN [app].[Vehicles] v ON v.[Id]=t.[VehicleId]";
        if(spec.ViaPlatformAccountSponsor)return main+" INNER JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=t.[PlatformRiderAccountId]";
        if(spec.HousingByResidence)return main;
        if(spec.PlatformCodeJoin&&spec.Kind=="platform_account")return main+" INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=t.[ClientPlatformId]";
        if(spec.Kind is "jahez_commission_policy" or "jahez_daily_order")
            return main+" LEFT JOIN [jahez_v2].[AccountSessions] sess ON sess.[Id]=t.[HandoverId] LEFT JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=sess.[PlatformRiderAccountId]";
        return $"[{spec.Schema}].[{spec.Table}]";
    }
    static string SelectSql(KindSpec spec)=>UsesAlias(spec)
        ?string.Join(',',spec.Columns.Select(c=>$"t.[{c}]"))
        :string.Join(',',spec.Columns.Select(c=>$"[{c}]"));
    static string BuildWhere(KindSpec spec,Scope scope,string query,DbCommand cmd,out bool needsSponsors)
    {
        needsSponsors=false;var parts=new List<string>();string prefix=UsesAlias(spec)?"t.":"";
        if(spec.Columns.Contains("IsDeleted",StringComparer.Ordinal))parts.Add($"{prefix}[IsDeleted]=0");

        if(!scope.AllCompanies)
        {
            if(scope.SponsorCommercialRegs.Count==0&&spec.Kind is not ("city" or "ninja_evaluation_snapshot"))
                parts.Add("1=0");
            else if(spec.HousingByResidence)
            {
                if(scope.EmployeeIds.Count==0)parts.Add("1=0");
                else
                {
                    var emp=InList(cmd,"@he",scope.EmployeeIds);
                    parts.Add($"EXISTS (SELECT 1 FROM [app].[HousingResidencePeriods] hr INNER JOIN [app].[HousingRooms] hroom ON hroom.[Id]=hr.[RoomId] WHERE hroom.[HousingId]=t.[Id] AND hr.[EmployeeId] IN ({emp}))");
                }
            }
            else if(spec.CompanyColumn!=null)
            {
                needsSponsors=true;
                var regs=InList(cmd,"@cr",scope.SponsorCommercialRegs);
                parts.Add($"{prefix}[{spec.CompanyColumn}] IN (SELECT s.[Id] FROM [app].[Sponsors] s WHERE s.[CommercialRegistrationNumber] IN ({regs}))");
            }
            else if(spec.ViaVehicleSponsor)
            {
                needsSponsors=true;
                var regs=InList(cmd,"@cr",scope.SponsorCommercialRegs);
                parts.Add($"v.[SponsorId] IN (SELECT s.[Id] FROM [app].[Sponsors] s WHERE s.[CommercialRegistrationNumber] IN ({regs}))");
            }
            else if(spec.ViaPlatformAccountSponsor||spec.Kind is "jahez_commission_policy" or "jahez_daily_order")
            {
                needsSponsors=true;
                var regs=InList(cmd,"@cr",scope.SponsorCommercialRegs);
                parts.Add($"pa.[SponsorId] IN (SELECT s.[Id] FROM [app].[Sponsors] s WHERE s.[CommercialRegistrationNumber] IN ({regs}))");
            }
            else if(spec.RiderEmployeeColumn!=null||spec.RiderProfileColumn!=null)
            {
                var riderParts=new List<string>();
                if(spec.RiderEmployeeColumn!=null)
                {
                    if(scope.EmployeeIds.Count==0)riderParts.Add("1=0");
                    else{var emp=InList(cmd,"@e",scope.EmployeeIds);riderParts.Add($"{prefix}[{spec.RiderEmployeeColumn}] IN ({emp})");}
                }
                if(spec.RiderProfileColumn!=null)
                {
                    if(scope.RiderProfileIds.Count==0)riderParts.Add("1=0");
                    else{var rp=InList(cmd,"@rp",scope.RiderProfileIds);riderParts.Add($"{prefix}[{spec.RiderProfileColumn}] IN ({rp})");}
                }
                parts.Add("("+string.Join(" OR ",riderParts)+")");
            }
        }

        if(spec.PlatformCodeJoin&&spec.Kind=="platform_account"&&scope.PlatformCodes.Count>0&&!scope.AllCompanies)
        {
            var plats=InList(cmd,"@pc",scope.PlatformCodes);
            parts.Add($"cp.[Code] IN ({plats})");
        }

        if(query.Length>0&&spec.SearchColumns.Length>0)
        {
            // Caller passes TextSearch.Normalize(input). Columns use ERP CI collation; Arabic letter folding is input-only.
            string like=EscapeLike(query);
            Add(cmd,"@q","%"+like+"%");
            parts.Add("("+string.Join(" OR ",spec.SearchColumns.Select(c=>$"{prefix}[{c}] LIKE @q ESCAPE '\\'"))+")");
        }

        if(spec.Kind=="custody_handover")parts.Add($"{prefix}[EventType] IN (1,2,3,4)");

        if(spec.Kind=="platform_account"&&scope.RiderAccountEmployeeId is {Length:>0} empKey)
        {
            Add(cmd,"@rae",empKey);
            var profileIds=scope.RiderAccountProfileIds??[];
            if(profileIds.Count==0)parts.Add($"{prefix}[RegisteredEmployeeId]=@rae");
            else
            {
                var rp=InList(cmd,"@rap",profileIds);
                parts.Add($"({prefix}[RegisteredEmployeeId]=@rae OR EXISTS (SELECT 1 FROM [app].[RiderClientAssignments] rca WHERE rca.[PlatformRiderAccountId]={prefix}[Id] AND rca.[IsDeleted]=0 AND rca.[RiderProfileId] IN ({rp})))");
            }
        }

        return parts.Count==0?"":" WHERE "+string.Join(" AND ",parts);
    }

    static string InList(DbCommand cmd,string prefix,IReadOnlyList<string> values)
    {
        if(values.Count==0){Add(cmd,prefix+"0",Guid.Empty.ToString("D"));return prefix+"0";}
        var names=new List<string>();
        for(int i=0;i<values.Count;i++)
        {
            string name=prefix+i;Add(cmd,name,values[i]);names.Add(name);
        }
        return string.Join(',',names);
    }
    static string EscapeLike(string value)=>value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("%","\\%",StringComparison.Ordinal).Replace("_","\\_",StringComparison.Ordinal).Replace("[","\\[",StringComparison.Ordinal);
    static void Add(DbCommand cmd,string name,object value){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}

    static List<JsonObject> Read(DbCommand cmd,CancellationToken cancellationToken)
    {
        using var reader=cmd.ExecuteReader(CommandBehavior.SequentialAccess);var rows=new List<JsonObject>();
        while(reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();var row=new JsonObject();
            for(int i=0;i<reader.FieldCount;i++)
            {
                string field=reader.GetName(i);object? value=reader.IsDBNull(i)?null:reader.GetValue(i);
                row[field]=value!=null&&field is "IsDeleted" or "IsEmployee" or "IsRealRider" or "IsCurrent" or "Frozen"
                    ?JsonValue.Create(value is bool b?b:value is string s?s is "1" or "true" or "True":Convert.ToInt32(value)!=0)
                    :SourceReader.Value(value);
            }
            rows.Add(row);
        }
        return rows;
    }

    static void Ensure(Dictionary<string,List<JsonObject>> tables,DbConnection c,string schema,string name,CancellationToken ct)
    {
        if(tables.ContainsKey(name))return;
        var src=Table(schema,name);
        using var cmd=c.CreateCommand();cmd.CommandTimeout=CommandTimeoutSeconds;using var cancel=SyncCancellation.Register(cmd,ct);
        cmd.CommandText=$"SELECT {string.Join(',',src.Fields.Split(' ').Select(f=>$"[{f}]"))} FROM [{schema}].[{name}]";
        tables[name]=Read(cmd,ct);
    }
    static void EnsureOptional(Dictionary<string,List<JsonObject>> tables,DbConnection c,string schema,string name,CancellationToken ct)
    {
        if(tables.ContainsKey(name))return;
        try
        {
            var src=Table(schema,name);
            if(!SourceReader.OptionalAvailablePublic(c,src with{Optional=true},ct)){tables[name]=[];return;}
            using var cmd=c.CreateCommand();cmd.CommandTimeout=CommandTimeoutSeconds;using var cancel=SyncCancellation.Register(cmd,ct);
            cmd.CommandText=$"SELECT {string.Join(',',src.Fields.Split(' ').Select(f=>$"[{f}]"))} FROM [{schema}].[{name}]";
            tables[name]=Read(cmd,ct);
        }
        catch(ImportFailure){tables[name]=[];}
    }

    static void LoadByIds(DbConnection c,string schema,string table,IReadOnlyCollection<string> ids,Dictionary<string,List<JsonObject>> tables,CancellationToken ct)
    {
        if(ids.Count==0){tables.TryAdd(table,[]);return;}
        var src=Table(schema,table);var all=new List<JsonObject>();
        foreach(var batch in ids.Where(id=>id!="").Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ParentBatchSize))
        {
            using var cmd=c.CreateCommand();cmd.CommandTimeout=CommandTimeoutSeconds;using var cancel=SyncCancellation.Register(cmd,ct);
            var names=new List<string>();
            for(int i=0;i<batch.Length;i++){string n="@id"+i;Add(cmd,n,batch[i]);names.Add(n);}
            cmd.CommandText=$"SELECT {string.Join(',',src.Fields.Split(' ').Select(f=>$"[{f}]"))} FROM [{schema}].[{table}] WHERE [Id] IN ({string.Join(',',names)})";
            all.AddRange(Read(cmd,ct));
        }
        if(tables.TryGetValue(table,out var existing))tables[table]=existing.Concat(all).GroupBy(r=>r.S("Id")).Select(g=>g.First()).ToList();
        else tables[table]=all;
    }

    static void LoadPageParents(DbConnection source,KindSpec spec,List<JsonObject> pageRows,Dictionary<string,List<JsonObject>> tables,CancellationToken ct)
    {
        foreach(var parent in spec.Parents)
        {
            if(spec.Kind=="housing"&&parent.Table is "HousingFloors" or "HousingRooms" or "HousingExternalOccupants" or "HousingResidencePeriods")
            {
                var housingIds=pageRows.Select(r=>r.S("Id")).Where(id=>id!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(parent.Table=="HousingRooms")
                {
                    LoadByFilter(source,parent.Schema,parent.Table,"HousingId",housingIds,tables,ct);
                    var roomIds=(tables.GetValueOrDefault("HousingRooms")??[]).Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    LoadByFilter(source,"app","HousingExternalOccupants","RoomId",roomIds,tables,ct);
                    LoadByFilter(source,"app","HousingResidencePeriods","RoomId",roomIds,tables,ct);
                }
                else if(parent.Table=="HousingFloors")LoadByFilter(source,parent.Schema,parent.Table,"HousingId",housingIds,tables,ct);
                continue;
            }
            if(parent.Table=="VehicleRegistrations"||parent.Table=="VehicleInsurancePolicies")
            {
                var vehicleIds=pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByFilter(source,parent.Schema,parent.Table,"VehicleId",vehicleIds,tables,ct);continue;
            }
            if(parent.Table=="RealRiders")
            {
                var assignmentIds=pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByFilter(source,parent.Schema,parent.Table,"RiderVehicleAssignmentId",assignmentIds,tables,ct);continue;
            }
            if(parent.Table=="HrLegalCaseHearings")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"LegalCaseId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="RiderClientAssignments"&&spec.Kind=="platform_account")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"PlatformRiderAccountId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="PlatformAccountRegistrations")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"PlatformRiderAccountId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="RiderInventoryIssueLines")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"RiderInventoryIssueId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="RiderAssignmentEvents")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"RiderClientAssignmentId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="PlatformAccountDayUsages"&&spec.Kind=="rider_work_timeline")
            {
                LoadByFilter(source,parent.Schema,parent.Table,"RiderClientAssignmentId",pageRows.Select(r=>r.S("Id")).ToHashSet(StringComparer.OrdinalIgnoreCase),tables,ct);continue;
            }
            if(parent.Table=="Housing"&&spec.Kind=="housing_residence")
            {
                var roomIds=pageRows.Select(r=>r.S("RoomId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"app","HousingRooms",roomIds,tables,ct);
                var housingIds=(tables.GetValueOrDefault("HousingRooms")??[]).Select(r=>r.S("HousingId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"app","Housing",housingIds,tables,ct);continue;
            }
            if(parent.Table=="HousingRooms"&&spec.Kind=="housing_residence")continue;
            if(parent.Table=="Vehicles"&&spec.Kind=="custody_handover")
            {
                var assignmentIds=pageRows.Select(r=>r.S("RiderVehicleAssignmentId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"app","RiderVehicleAssignments",assignmentIds,tables,ct);
                var vehicleIds=(tables.GetValueOrDefault("RiderVehicleAssignments")??[]).Select(r=>r.S("VehicleId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"app","Vehicles",vehicleIds,tables,ct);
                LoadByFilter(source,"app","RealRiders","RiderVehicleAssignmentId",assignmentIds,tables,ct);continue;
            }
            if(spec.Kind=="custody_handover"&&parent.Table is "RiderVehicleAssignments" or "RealRiders")continue;
            if(parent.Table=="ClientPlatforms")
            {
                var ids=pageRows.Select(r=>r.S("ClientPlatformId")).Where(id=>id!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(ids.Count==0&&tables.TryGetValue("PlatformRiderAccounts",out var accounts))
                    ids=accounts.Select(a=>a.S("ClientPlatformId")).Where(id=>id!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,parent.Schema,parent.Table,ids,tables,ct);continue;
            }
            if(parent.Table=="PlatformRiderAccounts"&&spec.Kind is "jahez_commission_policy" or "jahez_daily_order")
            {
                var sessionIds=pageRows.Select(r=>r.S("HandoverId")).Where(id=>id!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"jahez_v2","AccountSessions",sessionIds,tables,ct);
                var accountIds=(tables.GetValueOrDefault("AccountSessions")??[]).Select(s=>s.S("PlatformRiderAccountId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                LoadByIds(source,"app","PlatformRiderAccounts",accountIds,tables,ct);continue;
            }
            if(parent.Table=="AccountSessions")continue;

            var keyIds=pageRows.Select(parent.IdFromRow).Where(id=>id!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(keyIds.Count==0)continue;
            LoadByIds(source,parent.Schema,parent.Table,keyIds,tables,ct);
        }
    }

    static void LoadByFilter(DbConnection c,string schema,string table,string column,IReadOnlyCollection<string> values,Dictionary<string,List<JsonObject>> tables,CancellationToken ct)
    {
        if(values.Count==0){tables.TryAdd(table,[]);return;}
        // column must be allowlisted parent join key from code, never from the request.
        if(!Regex.IsMatch(column,"^[A-Za-z][A-Za-z0-9_]*$"))throw new ImportFailure("live_ops_bad_parent_column");
        var src=Table(schema,table);var all=new List<JsonObject>();
        foreach(var batch in values.Where(v=>v!="").Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ParentBatchSize))
        {
            using var cmd=c.CreateCommand();cmd.CommandTimeout=CommandTimeoutSeconds;using var cancel=SyncCancellation.Register(cmd,ct);
            var names=new List<string>();
            for(int i=0;i<batch.Length;i++){string n="@p"+i;Add(cmd,n,batch[i]);names.Add(n);}
            cmd.CommandText=$"SELECT {string.Join(',',src.Fields.Split(' ').Select(f=>$"[{f}]"))} FROM [{schema}].[{table}] WHERE [{column}] IN ({string.Join(',',names)})";
            all.AddRange(Read(cmd,ct));
        }
        if(tables.TryGetValue(table,out var existing))tables[table]=existing.Concat(all).GroupBy(r=>r.S("Id")).Select(g=>g.First()).ToList();
        else tables[table]=all;
    }

    static void LoadRiderMaps(DbConnection source,KindSpec spec,List<JsonObject> pageRows,Dictionary<string,List<JsonObject>> tables,CancellationToken ct)
    {
        var employeeIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Take(JsonObject row)
        {
            foreach(string key in new[]{"EmployeeId","RegisteredEmployeeId","RiderProfileId"})
                if(row.S(key) is {Length:>0} id){if(key.Contains("Profile",StringComparison.Ordinal))profileIds.Add(id);else employeeIds.Add(id);}
        }
        foreach(var row in pageRows)Take(row);
        foreach(var list in tables.Values)foreach(var row in list)Take(row);
        if(profileIds.Count>0)
        {
            LoadByIds(source,"app","RiderProfiles",profileIds,tables,ct);
            foreach(var p in tables.GetValueOrDefault("RiderProfiles")??[])if(p.S("EmployeeId")!="")employeeIds.Add(p.S("EmployeeId"));
        }
        if(employeeIds.Count>0)LoadByIds(source,"app","Employees",employeeIds,tables,ct);
        // Ensure profiles for those employees (ImportPlan needs them).
        if(employeeIds.Count>0)LoadByFilter(source,"app","RiderProfiles","EmployeeId",employeeIds,tables,ct);
    }
}
