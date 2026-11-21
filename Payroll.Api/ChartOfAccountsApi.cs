using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-23: Chart of accounts New / Edit / Deactivate (no hard delete). Tables: fin.account (U-W1-08).
/// Permissions split view/create/edit/deactivate. Leaf vs parent via is_postable. Short audit on data changes (M-0606).
/// Direction / required-dimension columns are not on fin.account — omitted (reported).</summary>
public static class ChartOfAccountsApi
{
    public const string ViewPerm="accounting.chart.view",CreatePerm="accounting.chart.create",EditPerm="accounting.chart.edit",DeactivatePerm="accounting.chart.deactivate";
    public const string PendingKind="coa_deactivate_request",PostedUseKind="coa_posted_use";

    static string T(DbConnection c)=>SchemaSql.Q(c,"fin","account");
    static string ActorName(ClaimsPrincipal actor)=>actor.Identity?.Name??"";

    public static bool IsUsed(DbConnection c,DbTransaction? tx,string code)
    {
        if(Store.Get(c,"coa-use:"+code,PostedUseKind,tx)!=null)return true;
        string line=SchemaSql.Q(c,"fin","journal_line");
        if(!SchemaSql.TableExists(c,tx,"fin","journal_line"))return false;
        object? n=SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(1) FROM {line} jl
            INNER JOIN {T(c)} a ON a.id=jl.account_id
            WHERE a.code=@code
            """,("@code",code));
        return Convert.ToInt64(n??0L)>0;
    }

    static JsonObject Load(DbConnection c,DbTransaction? tx,string code)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,code,parent_id,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_by,updated_by
            FROM {T(c)} WHERE code=@code
            """,("@code",code));
        using var r=cmd.ExecuteReader();
        if(!r.Read())throw new ApiError("الحساب غير موجود.",404);
        string? parentId=r.IsDBNull(2)?null:r.GetValue(2).ToString();
        string parentCode="";
        if(!string.IsNullOrWhiteSpace(parentId))
        {
            object? pc=SchemaSql.Scalar(c,tx,$"SELECT code FROM {T(c)} WHERE id=@id",("@id",SchemaSql.GuidText(Guid.Parse(parentId))));
            parentCode=pc?.ToString()??"";
        }
        return new()
        {
            ["id"]=r.GetValue(0).ToString(),
            ["code"]=r.GetString(1),
            ["parentCode"]=parentCode,
            ["nameAr"]=r.GetString(3),
            ["accountType"]=Convert.ToInt32(r.GetValue(4)),
            ["nature"]=Convert.ToInt32(r.GetValue(5)),
            ["isPostable"]=Convert.ToInt32(r.GetValue(6))==1,
            ["status"]=Convert.ToInt32(r.GetValue(7)),
            ["isActive"]=Convert.ToInt32(r.GetValue(8))==1,
            ["companyScope"]=Convert.ToInt32(r.GetValue(9)),
            ["levelNo"]=Convert.ToInt32(r.GetValue(10)),
            ["createdBy"]=r.IsDBNull(11)?"":r.GetString(11),
            ["updatedBy"]=r.IsDBNull(12)?"":r.GetString(12)
        };
    }

    public static JsonObject WithActions(ClaimsPrincipal actor,JsonObject row,DbConnection c,DbTransaction? tx)
    {
        var actions=new JsonArray();
        bool active=row.B("isActive");
        if(AccessPolicy.Has(actor,CreatePerm))actions.Add((JsonNode)"create");
        if(AccessPolicy.Has(actor,EditPerm)&&active)actions.Add((JsonNode)"edit");
        if(AccessPolicy.Has(actor,DeactivatePerm)&&active)actions.Add((JsonNode)"deactivate");
        if(AccessPolicy.Has(actor,DeactivatePerm)&&!active)actions.Add((JsonNode)"reactivate");
        var pending=Store.Get(c,"coa-deact:"+row.S("code"),PendingKind,tx);
        if(pending!=null&&AccessPolicy.Has(actor,DeactivatePerm)&&!AccessPolicy.SamePerson(actor,pending.S("requestedBy")))
            actions.Add((JsonNode)"confirm_deactivate");
        var copy=row.DeepClone().AsObject();
        copy["allowedActions"]=actions;
        copy["deactivatePending"]=pending!=null;
        copy["used"]=IsUsed(c,tx,row.S("code"));
        return copy;
    }

    static (Guid Id,byte Nature,byte Type,int Level,bool Postable)? Parent(DbConnection c,DbTransaction? tx,string parentCode)
    {
        if(string.IsNullOrWhiteSpace(parentCode))return null;
        using var cmd=Store.Command(c,tx,$"SELECT id,nature,account_type,level_no,is_postable,is_active FROM {T(c)} WHERE code=@code",("@code",parentCode));
        using var r=cmd.ExecuteReader();
        if(!r.Read())throw new ApiError("الحساب الأب غير موجود.",400);
        if(Convert.ToInt32(r.GetValue(5))!=1)throw new ApiError("الحساب الأب موقوف.",400);
        return (Guid.Parse(r.GetValue(0).ToString()!),Convert.ToByte(r.GetValue(1)),Convert.ToByte(r.GetValue(2)),Convert.ToInt32(r.GetValue(3)),Convert.ToInt32(r.GetValue(4))==1);
    }

    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,CreatePerm);
        string parentCode=input.S("parentCode",input.S("parent"));
        string code=input.Required("code").Trim();
        string nameAr=input.Required("nameAr").Trim();
        bool isPostable=input.ContainsKey("isPostable")?input.B("isPostable"):input.ContainsKey("posting")?input.B("posting"):true;
        J.Need(code.Length is >=1 and <=16,"رمز الحساب من 1 إلى 16.",400);
        J.Need(nameAr.Length is >=1 and <=200,"اسم الحساب مطلوب.",400);
        J.Need(Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {T(c)} WHERE code=@code",("@code",code))??0)==0,"رمز الحساب مكرر.",409);

        var parent=Parent(c,tx,parentCode);
        byte nature=parent?.Nature??(input.S("nature") is "credit" or "2"?(byte)2:(byte)1);
        if(parent!=null&&input.ContainsKey("nature"))
        {
            byte requested=input.S("nature") is "credit" or "2"?(byte)2:(byte)1;
            J.Need(requested==parent.Value.Nature,"طبيعة الحساب تُقفل على الأب.",400);
        }
        byte type=parent?.Type??SchemaFinAccount.DeriveAccountType(code,nature);
        int level=(parent?.Level??0)+1;
        if(parent is { Postable:true })
        {
            J.Need(!IsUsed(c,tx,parentCode),"لا يُضاف فرع تحت حساب مرحّل عليه.",409);
            SchemaSql.Exec(c,tx,$"UPDATE {T(c)} SET is_postable=0,updated_by=@u,updated_at={SchemaSql.NowLiteral(c)} WHERE code=@code",
                ("@u",ActorName(actor)),("@code",parentCode));
        }

        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {T(c)}(id,code,parent_id,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_by,created_at)
            VALUES(@id,@code,@parent,@name,@type,@nature,@postable,1,1,0,@level,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(id)),("@code",code),
            ("@parent",parent==null?(object)DBNull.Value:SchemaSql.GuidText(parent.Value.Id)),
            ("@name",nameAr),("@type",type),("@nature",nature),("@postable",isPostable?1:0),
            ("@level",level),("@by",ActorName(actor)));
        Store.Audit(c,tx,"accounting.chart.create",code,ActorName(actor));
        return WithActions(actor,Load(c,tx,code),c,tx);
    }

    public static JsonObject Update(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string code,JsonObject input)
    {
        AccessPolicy.Demand(actor,EditPerm);
        var row=Load(c,tx,code);
        J.Need(row.B("isActive"),"الحساب الموقوف لا يُعدّل؛ فعّله أولاً.",409);
        if(input.ContainsKey("code")&&input.S("code")!=code)
        {
            J.Need(!IsUsed(c,tx,code),"لا يُغيّر رمز حساب عليه ترحيل.",400);
            throw new ApiError("تغيير رمز الحساب غير مسموح بعد الإنشاء في هذا المسار.",400);
        }
        string nameAr=input.ContainsKey("nameAr")?input.Required("nameAr").Trim():row.S("nameAr");
        J.Need(nameAr.Length is >=1 and <=200,"اسم الحساب مطلوب.",400);
        bool isPostable=input.ContainsKey("isPostable")?input.B("isPostable")
            :input.ContainsKey("posting")?input.B("posting")
            :row.B("isPostable");
        if(isPostable!=row.B("isPostable"))
        {
            bool hasChildren=Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {T(c)} WHERE parent_id=@id",("@id",row.S("id")))??0)>0;
            J.Need(!(isPostable&&hasChildren),"حساب له فروع لا يكون ترحيلياً.",400);
            if(!isPostable&&IsUsed(c,tx,code))throw new ApiError("لا يُلغى الترحيل عن حساب عليه قيود.",409);
        }
        SchemaSql.Exec(c,tx,$"""
            UPDATE {T(c)} SET name_ar=@name,is_postable=@postable,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)}
            WHERE code=@code
            """,("@name",nameAr),("@postable",isPostable?1:0),("@by",ActorName(actor)),("@code",code));
        Store.Audit(c,tx,"accounting.chart.edit",code,ActorName(actor));
        return WithActions(actor,Load(c,tx,code),c,tx);
    }

    public static JsonObject Deactivate(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string code)
    {
        AccessPolicy.Demand(actor,DeactivatePerm);
        var row=Load(c,tx,code);
        J.Need(row.B("isActive"),"الحساب موقوف مسبقاً.",409);
        long activeChildren=Convert.ToInt64(SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(1) FROM {T(c)} WHERE parent_id=@id AND is_active=1
            """,("@id",row.S("id")))??0);
        J.Need(activeChildren==0,"أوقف الفروع النشطة أولاً.",409);

        if(IsUsed(c,tx,code))
        {
            string pendingId="coa-deact:"+code;
            var pending=Store.Get(c,pendingId,PendingKind,tx);
            if(pending==null)
            {
                Store.Save(c,PendingKind,new(){["id"]=pendingId,["code"]=code,["requestedBy"]=ActorName(actor),["requestedAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
                Store.Audit(c,tx,"accounting.chart.deactivate_request",code,ActorName(actor));
                var view=WithActions(actor,row,c,tx);view["deactivatePending"]=true;return view;
            }
            J.Need(!AccessPolicy.SamePerson(actor,pending.S("requestedBy")),"إيقاف الحساب المستخدم يحتاج شخصاً ثانياً.",409);
            using var del=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind=@kind",("@id",pendingId),("@kind",PendingKind));
            del.ExecuteNonQuery();
        }

        SchemaSql.Exec(c,tx,$"""
            UPDATE {T(c)} SET status=2,is_active=0,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)}
            WHERE code=@code
            """,("@by",ActorName(actor)),("@code",code));
        Store.Audit(c,tx,"accounting.chart.deactivate",code,ActorName(actor));
        return WithActions(actor,Load(c,tx,code),c,tx);
    }

    public static JsonObject Reactivate(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string code)
    {
        AccessPolicy.Demand(actor,DeactivatePerm);
        var row=Load(c,tx,code);
        J.Need(!row.B("isActive"),"الحساب نشط مسبقاً.",409);
        if(row.S("parentCode")!="")
        {
            var parent=Load(c,tx,row.S("parentCode"));
            J.Need(parent.B("isActive"),"فعّل الحساب الأب أولاً.",409);
        }
        SchemaSql.Exec(c,tx,$"""
            UPDATE {T(c)} SET status=1,is_active=1,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)}
            WHERE code=@code
            """,("@by",ActorName(actor)),("@code",code));
        string pendingId="coa-deact:"+code;
        using(var del=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind=@kind",("@id",pendingId),("@kind",PendingKind)))del.ExecuteNonQuery();
        Store.Audit(c,tx,"accounting.chart.reactivate",code,ActorName(actor));
        return WithActions(actor,Load(c,tx,code),c,tx);
    }

    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,bool inactiveOnly)
    {
        AccessPolicy.Demand(actor,ViewPerm);
        using var cmd=Store.Command(c,null,$"""
            SELECT code FROM {T(c)} WHERE {(inactiveOnly?"is_active=0":"is_active=1")} ORDER BY code
            """);
        var items=new JsonArray();
        using var r=cmd.ExecuteReader();
        var codes=new List<string>();
        while(r.Read())codes.Add(r.GetString(0));
        foreach(string code in codes)items.Add(WithActions(actor,Load(c,null,code),c,null));
        return new(){["items"]=items,["allowedActions"]=AccessPolicy.Has(actor,CreatePerm)?new JsonArray("create"):new JsonArray()};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/coa",(bool? inactive,HttpContext ctx)=>
            store.Read(c=>List(c,ctx.User,inactive==true)))
            .WithTags("Accounting and reports").WithSummary("List chart of accounts tree rows with allowedActions");
        api.MapPost("/finance/coa",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Create(c,tx,ctx.User,input)))
            .WithTags("Accounting and reports").WithSummary("Create a CoA account under a parent; nature locked to parent");
        api.MapPut("/finance/coa/{code}",(string code,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Update(c,tx,ctx.User,code,input)))
            .WithTags("Accounting and reports").WithSummary("Edit CoA name/flags; code change blocked when posted");
        api.MapPost("/finance/coa/{code}/deactivate",(string code,HttpContext ctx)=>
            store.Write((c,tx)=>Deactivate(c,tx,ctx.User,code)))
            .WithTags("Accounting and reports").WithSummary("Deactivate CoA account; used accounts need a second person");
        api.MapPost("/finance/coa/{code}/reactivate",(string code,HttpContext ctx)=>
            store.Write((c,tx)=>Reactivate(c,tx,ctx.User,code)))
            .WithTags("Accounting and reports").WithSummary("Reactivate a stopped CoA account");
    }
}
