using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-26: GET/PUT /api/admin/settings against ref.setting (save_settings).</summary>
public static class AdminSettings
{
    public const string Permission="admin.settings.manage";
    static readonly string[] KnownKeys=["current_period","totp_enforced","reply_working_days","company_defaults","feature_flags"];

    static string T(DbConnection c)=>SchemaSql.Q(c,"ref","setting");

    public static JsonObject Get(DbConnection c,ClaimsPrincipal actor,DbTransaction? tx=null)
    {
        AccessPolicy.Demand(actor,Permission);
        var items=new JsonArray();
        using var cmd=Store.Command(c,tx,SchemaSql.IsSql(c)
            ?$"SELECT id,[key],value_json FROM {T(c)}"
            :$"SELECT id,key,value_json FROM {T(c)}");
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string key=r.GetString(1);
            JsonNode? value;
            try{value=JsonNode.Parse(r.GetString(2));}catch{value=JsonValue.Create(r.GetString(2));}
            items.Add(new JsonObject{["id"]=r.GetValue(0).ToString(),["key"]=key,["value"]=value?.DeepClone()});
        }
        return new(){["items"]=items,["allowedActions"]=new JsonArray("save_settings"),["knownKeys"]=new JsonArray(KnownKeys.Select(k=>(JsonNode?)k).ToArray())};
    }

    public static JsonObject Put(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,Permission);
        var source=input["items"] as JsonArray??input["settings"] as JsonArray;
        if(source==null&&!string.IsNullOrWhiteSpace(input.S("key")))
            source=new JsonArray(new JsonObject{["key"]=input.Required("key"),["value"]=input["value"]?.DeepClone()??new JsonObject()});
        J.Need(source!=null&&source.Count>0,"إعدادات مطلوبة.",400);
        foreach(var node in source!.OfType<JsonObject>())
        {
            string key=node.Required("key").Trim();
            J.Need(key.Length is >=1 and <=80,"مفتاح الإعداد غير صالح.",400);
            string json=(node["value"]??new JsonObject()).ToJsonString();
            object? existing=SchemaSql.Scalar(c,tx,
                SchemaSql.IsSql(c)?$"SELECT id FROM {T(c)} WHERE [key]=@key":$"SELECT id FROM {T(c)} WHERE key=@key",
                ("@key",key));
            if(existing==null)
            {
                Guid id=SchemaSql.NewId();
                SchemaSql.Exec(c,tx,SchemaSql.IsSql(c)
                    ?$"""
                    INSERT INTO {T(c)}(id,[key],value_json,created_by,created_at,updated_by,updated_at)
                    VALUES(@id,@key,@val,@by,{SchemaSql.NowLiteral(c)},@by,{SchemaSql.NowLiteral(c)})
                    """
                    :$"""
                    INSERT INTO {T(c)}(id,key,value_json,created_by,created_at,updated_by,updated_at)
                    VALUES(@id,@key,@val,@by,{SchemaSql.NowLiteral(c)},@by,{SchemaSql.NowLiteral(c)})
                    """,
                    ("@id",SchemaSql.GuidText(id)),("@key",key),("@val",json),("@by",actor.Identity!.Name!));
            }
            else
            {
                SchemaSql.Exec(c,tx,SchemaSql.IsSql(c)
                    ?$"""
                    UPDATE {T(c)} SET value_json=@val,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE [key]=@key
                    """
                    :$"""
                    UPDATE {T(c)} SET value_json=@val,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE key=@key
                    """,
                    ("@val",json),("@by",actor.Identity!.Name!),("@key",key));
            }
        }
        Store.Audit(c,tx,"admin.settings_save","ref.setting",actor.Identity!.Name!);
        return Get(c,actor,tx);
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/admin/settings",(HttpContext ctx)=>store.Read(c=>Get(c,ctx.User)))
            .WithTags("Administration").WithSummary("Read system settings from ref.setting");
        api.MapPut("/admin/settings",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Put(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Save system settings to ref.setting");
    }
}
