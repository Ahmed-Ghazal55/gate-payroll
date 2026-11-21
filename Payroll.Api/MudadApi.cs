using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-04: Mudad upload/match + line CRUD. One Store.Audit row per file (M-0606).
/// Permissions: people.mudad.manage (upload/match/list), salaries.mudad.edit (line CRUD).</summary>
public static class MudadApi
{
    public const string ManagePermission="people.mudad.manage",EditPermission="salaries.mudad.edit";
    public const byte MatchUnmatched=1,MatchMatched=2,MatchNoPerson=3,MatchBad=4;
    public const byte StatusLoaded=1,StatusReplaced=2,StatusRejected=3;

    public static JsonObject Upload(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        string companyId=input.Required("companyId");
        string periodMonth=MonthDay(input.Required("periodMonth"));
        string fileKind=input.Required("fileKind");
        J.Need(fileKind is "salaries" or "bank_accounts","نوع ملف مدد: salaries أو bank_accounts.",400);
        string fileName=input.Required("fileName").Trim();
        J.Need(fileName.Length is >=1 and <=260,"اسم الملف مطلوب.",400);
        byte[] hash=ParseHash(input.Required("fileHashHex"));
        var lines=(input["lines"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(lines.Count>0,"أدخل صفوف مدد.",400);
        DemandCompany(c,tx,companyId);

        string uploadId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string by=actor.Identity!.Name!;
        try
        {
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"hr","mudad_upload")}
                (id,company_id,period_month,file_kind,file_name,file_hash,row_count,status,uploaded_by,uploaded_at,created_by,created_at)
                VALUES(@id,@c,@pm,@kind,@name,@hash,@rows,@st,@by,@at,@by,@at)
                """,("@id",uploadId),("@c",companyId),("@pm",periodMonth),("@kind",fileKind),("@name",fileName),
                ("@hash",hash),("@rows",lines.Count),("@st",StatusLoaded),("@by",by),("@at",now));
        }
        catch(Exception ex) when(IsUniqueViolation(ex))
        {throw new ApiError("نفس ملف مدد مرفوع مسبقاً لهذه الشركة والشهر.",409);}

        var outLines=new JsonArray();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in lines)
        {
            string iqama=J.Iqama(row.Required("iqama"));
            J.Need(seen.Add(iqama),"رقم هوية مكرر في نفس الملف: "+iqama,400);
            string official=row.Required("officialName").Trim();
            J.Need(official.Length is >=1 and <=200,"اسم الموظف مطلوب.",400);
            string lineId=SchemaSql.GuidText(SchemaSql.NewId());
            decimal? basic=MoneyOrNull(row,"basicSalary"),gross=MoneyOrNull(row,"grossSalary");
            string? iban=NullIfEmpty(row.S("iban"));
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"hr","mudad_line")}
                (id,upload_id,period_month,company_id,iqama,official_name,iban,basic_salary,housing_allowance,transport_allowance,gross_salary,
                 header_variant,match_status,created_by,created_at)
                VALUES(@id,@up,@pm,@c,@iq,@name,@iban,@basic,@house,@trans,@gross,@var,@ms,@by,@at)
                """,("@id",lineId),("@up",uploadId),("@pm",periodMonth),("@c",companyId),("@iq",iqama),("@name",official),
                ("@iban",(object?)iban??DBNull.Value),("@basic",(object?)basic??DBNull.Value),
                ("@house",(object?)MoneyOrNull(row,"housingAllowance")??DBNull.Value),
                ("@trans",(object?)MoneyOrNull(row,"transportAllowance")??DBNull.Value),
                ("@gross",(object?)gross??DBNull.Value),("@var",row.S("headerVariant","base11")),
                ("@ms",MatchUnmatched),("@by",by),("@at",now));
            outLines.Add(LineView(lineId,iqama,official,basic,gross,MatchUnmatched,iban));
        }
        Store.Audit(c,tx,"mudad.upload",uploadId,by);
        return new()
        {
            ["id"]=uploadId,["companyId"]=companyId,["periodMonth"]=periodMonth,["fileKind"]=fileKind,
            ["fileName"]=fileName,["rowCount"]=lines.Count,["status"]=StatusLoaded,["lines"]=outLines,
            ["allowedActions"]=new JsonArray("match_mudad","edit_mudad_lines")
        };
    }

    public static JsonObject Match(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string uploadId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        var upload=LoadUpload(c,tx,uploadId);
        var matches=(input["matches"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(matches.Count>0,"أدخل مطابقات.",400);
        int matched=0;
        foreach(var m in matches)
        {
            string lineId=m.Required("lineId");
            string? erp=NullIfEmpty(m.S("erpEmployeeId"));
            byte status=erp==null?MatchNoPerson:MatchMatched;
            int n=Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"hr","mudad_line")}
                SET erp_employee_id=@erp,match_status=@ms,updated_by=@by,updated_at=@at
                WHERE id=@id AND upload_id=@up
                """,("@erp",(object?)erp??DBNull.Value),("@ms",status),("@by",actor.Identity!.Name!),
                ("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),
                ("@id",lineId),("@up",uploadId));
            J.Need(n==1,"صف مدد غير موجود في هذه الدفعة.",404);
            if(status==MatchMatched)matched++;
        }
        Store.Audit(c,tx,"mudad.match",uploadId,actor.Identity!.Name!);
        return new(){["id"]=uploadId,["matchedCount"]=matched,["companyId"]=upload.S("companyId"),["periodMonth"]=upload.S("periodMonth")};
    }

    public static JsonObject AddLine(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string uploadId,JsonObject input)
    {
        AccessPolicy.Demand(actor,EditPermission);
        Ensure(c,tx);
        var upload=LoadUpload(c,tx,uploadId);
        string iqama=J.Iqama(input.Required("iqama"));
        string official=input.Required("officialName").Trim();
        string lineId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        decimal? basic=MoneyOrNull(input,"basicSalary"),gross=MoneyOrNull(input,"grossSalary");
        try
        {
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"hr","mudad_line")}
                (id,upload_id,period_month,company_id,iqama,official_name,basic_salary,gross_salary,header_variant,match_status,created_by,created_at)
                VALUES(@id,@up,@pm,@c,@iq,@name,@basic,@gross,'base11',@ms,@by,@at)
                """,("@id",lineId),("@up",uploadId),("@pm",upload.S("periodMonth")),("@c",upload.S("companyId")),
                ("@iq",iqama),("@name",official),("@basic",(object?)basic??DBNull.Value),
                ("@gross",(object?)gross??DBNull.Value),("@ms",MatchUnmatched),("@by",actor.Identity!.Name!),("@at",now));
        }
        catch(Exception ex) when(IsUniqueViolation(ex))
        {throw new ApiError("رقم الهوية موجود في الدفعة.",409);}
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"hr","mudad_upload")} SET row_count=row_count+1,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@by",actor.Identity!.Name!),("@at",now),("@id",uploadId));
        Store.Audit(c,tx,"mudad.line_add",lineId,actor.Identity!.Name!);
        return LineView(lineId,iqama,official,basic,gross,MatchUnmatched,null);
    }

    public static JsonObject UpdateLine(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string uploadId,string lineId,JsonObject input)
    {
        AccessPolicy.Demand(actor,EditPermission);
        Ensure(c,tx);
        LoadUpload(c,tx,uploadId);
        decimal? basic=MoneyOrNull(input,"basicSalary"),gross=MoneyOrNull(input,"grossSalary");
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        int n=Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"hr","mudad_line")}
            SET basic_salary=COALESCE(@basic,basic_salary),gross_salary=COALESCE(@gross,gross_salary),
                official_name=COALESCE(@name,official_name),updated_by=@by,updated_at=@at
            WHERE id=@id AND upload_id=@up
            """,("@basic",(object?)basic??DBNull.Value),("@gross",(object?)gross??DBNull.Value),
            ("@name",(object?)NullIfEmpty(input.S("officialName"))??DBNull.Value),
            ("@by",actor.Identity!.Name!),("@at",now),("@id",lineId),("@up",uploadId));
        J.Need(n==1,"صف مدد غير موجود.",404);
        Store.Audit(c,tx,"mudad.line_edit",lineId,actor.Identity!.Name!);
        return LoadLine(c,tx,lineId);
    }

    public static JsonObject DeleteLine(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string uploadId,string lineId)
    {
        AccessPolicy.Demand(actor,EditPermission);
        Ensure(c,tx);
        LoadUpload(c,tx,uploadId);
        // Soft-cancel: money retain forbids hard delete; cancelled lines stay for audit (M-0632).
        int n=Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"hr","mudad_line")}
            SET match_status=@bad,basic_salary=0,gross_salary=0,updated_by=@by,updated_at=@at
            WHERE id=@id AND upload_id=@up AND match_status<>@bad
            """,("@bad",MatchBad),("@by",actor.Identity!.Name!),
            ("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),
            ("@id",lineId),("@up",uploadId));
        J.Need(n==1,"صف مدد غير موجود أو ملغى مسبقاً.",404);
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"hr","mudad_upload")} SET row_count=CASE WHEN row_count>0 THEN row_count-1 ELSE 0 END,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),("@id",uploadId));
        Store.Audit(c,tx,"mudad.line_delete",lineId,actor.Identity!.Name!);
        return new(){["id"]=lineId,["uploadId"]=uploadId,["status"]="cancelled"};
    }

    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,string? companyId,string? periodMonth)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c);
        using var cmd=c.CreateCommand();
        cmd.CommandText=$"""
            SELECT id,company_id,period_month,file_kind,file_name,row_count,status,uploaded_by,uploaded_at
            FROM {SchemaSql.Q(c,"hr","mudad_upload")}
            WHERE (@c IS NULL OR company_id=@c) AND (@pm IS NULL OR period_month=@pm)
            ORDER BY period_month DESC, uploaded_at DESC
            """;
        Add(cmd,"@c",(object?)companyId??DBNull.Value);
        Add(cmd,"@pm",(object?)(string.IsNullOrWhiteSpace(periodMonth)?null:MonthDay(periodMonth))??DBNull.Value);
        var rows=new JsonArray();
        using var r=cmd.ExecuteReader();
        while(r.Read())
            rows.Add(new JsonObject
            {
                ["id"]=r["id"].ToString(),["companyId"]=r["company_id"].ToString(),
                ["periodMonth"]=ReadMonth(r["period_month"]),
                ["fileKind"]=r["file_kind"].ToString(),["fileName"]=r["file_name"].ToString(),
                ["rowCount"]=Convert.ToInt32(r["row_count"]),["status"]=Convert.ToInt32(r["status"]),
                ["uploadedBy"]=r["uploaded_by"].ToString(),["uploadedAt"]=r["uploaded_at"].ToString()
            });
        return new(){["rows"]=rows};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/mudad",(string? companyId,string? periodMonth,HttpContext ctx)=>store.Read(c=>List(c,ctx.User,companyId,periodMonth)))
            .WithTags("Workforce").WithSummary("List Mudad uploads by company/month");
        api.MapPost("/mudad/uploads",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Upload(c,tx,ctx.User,input);
        })).WithTags("Workforce").WithSummary("Upload a Mudad salaries or bank-accounts batch");
        api.MapPost("/mudad/uploads/{id}/match",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Match(c,tx,ctx.User,id,input);
        })).WithTags("Workforce").WithSummary("Match Mudad lines to ERP employees");
        api.MapPost("/mudad/uploads/{id}/lines",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return AddLine(c,tx,ctx.User,id,input);
        })).WithTags("Payroll").WithSummary("Add a Mudad line (edit_mudad_lines)");
        api.MapPut("/mudad/uploads/{id}/lines/{lineId}",(string id,string lineId,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return UpdateLine(c,tx,ctx.User,id,lineId,input);
        })).WithTags("Payroll").WithSummary("Update a Mudad line");
        api.MapDelete("/mudad/uploads/{id}/lines/{lineId}",(string id,string lineId,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return DeleteLine(c,tx,ctx.User,id,lineId);
        })).WithTags("Payroll").WithSummary("Cancel/remove a Mudad line");
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);
        SchemaHrMudad.Ensure(c,tx);
        if(SchemaSql.TableExists(c,tx,"sal","pay_run_rider"))SchemaSalMudad.Ensure(c,tx);
    }

    static JsonObject LoadUpload(DbConnection c,DbTransaction tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT id,company_id,period_month,file_kind,status FROM {SchemaSql.Q(c,"hr","mudad_upload")} WHERE id=@id";
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"دفعة مدد غير موجودة.",404);
        return new()
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,
            ["periodMonth"]=ReadMonth(r["period_month"]),
            ["fileKind"]=r["file_kind"].ToString()!,["status"]=Convert.ToInt32(r["status"])
        };
    }

    static JsonObject LoadLine(DbConnection c,DbTransaction tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,iqama,official_name,basic_salary,gross_salary,match_status,iban
            FROM {SchemaSql.Q(c,"hr","mudad_line")} WHERE id=@id
            """;
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"صف مدد غير موجود.",404);
        return LineView(
            r["id"].ToString()!,r["iqama"].ToString()!,r["official_name"].ToString()!,
            r["basic_salary"] is DBNull?null:Convert.ToDecimal(r["basic_salary"]),
            r["gross_salary"] is DBNull?null:Convert.ToDecimal(r["gross_salary"]),
            Convert.ToByte(r["match_status"]),
            r["iban"] is DBNull?null:r["iban"].ToString());
    }

    static JsonObject LineView(string id,string iqama,string name,decimal? basic,decimal? gross,byte match,string? iban)=>new()
    {
        ["id"]=id,["iqama"]=iqama,["officialName"]=name,
        ["basicSalary"]=basic is {} b?b:null,["grossSalary"]=gross is {} g?g:null,
        ["matchStatus"]=match,["iban"]=iban??""
    };

    static void DemandCompany(DbConnection c,DbTransaction tx,string companyId)
    {
        object? found=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId));
        J.Need(found is not null,"الشركة غير موجودة.",404);
    }

    static string MonthDay(string raw)
    {
        raw=raw.Trim();
        if(DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
        {
            J.Need(day.Day==1,"شهر مدد يجب أن يكون أول يوم في الشهر.",400);
            return day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        }
        J.Need(DateOnly.TryParseExact(raw+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var month),"شهر مدد بصيغة yyyy-MM أو yyyy-MM-dd.",400);
        return month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    }

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>MonthDay(s),
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };

    static byte[] ParseHash(string hex)
    {
        hex=hex.Trim();
        J.Need(hex.Length==64&&hex.All(Uri.IsHexDigit),"بصمة الملف يجب أن تكون 64 خانة ست عشرية.",400);
        var bytes=new byte[32];
        for(int i=0;i<32;i++)bytes[i]=Convert.ToByte(hex.Substring(i*2,2),16);
        return bytes;
    }

    static decimal? MoneyOrNull(JsonObject row,string key)
    {
        if(!row.ContainsKey(key)||row[key] is null)return null;
        return J.Money(row.N(key));
    }

    static string? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
    static bool IsUniqueViolation(Exception ex)
    {
        string m=ex.GetBaseException().Message;
        return m.Contains("UNIQUE",StringComparison.OrdinalIgnoreCase)
            ||m.Contains("UQ_hr_mudad",StringComparison.OrdinalIgnoreCase)
            ||m.Contains("duplicate",StringComparison.OrdinalIgnoreCase)
            ||m.Contains("Money row replacement",StringComparison.OrdinalIgnoreCase);
    }

    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (name,value) in values)Add(cmd,name,value);
        return cmd.ExecuteNonQuery();
    }

    static void Add(DbCommand cmd,string name,object value)
    {
        var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);
    }
}
