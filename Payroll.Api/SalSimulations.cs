using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-31 simulation APIs: journal-preview, discard, promote-diff, promote-real.
/// Simulation never posts; promote creates a live draft that still needs every normal approval.</summary>
public static class SalSimulations
{
    public const string RunPermission="salaries.simulation.run",PromotePermission="pay.manage";
    const byte StatusActive=1,StatusCancelled=4,RunModeSimulation=1,RunModeLive=0;

    public static JsonObject JournalPreview(DbConnection c,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,RunPermission);
        var run=LoadSim(c,null,actor,id);
        var riders=ListRiders(c,null,id);
        var lines=new JsonArray();
        decimal debit=0,credit=0;
        foreach(var rider in riders)
        {
            decimal net=J.Money(Convert.ToDecimal(rider["salary_net"]));
            if(net==0)continue;
            // Preview only — no post, no period_balance (N1).
            if(net>0)
            {
                lines.Add(new JsonObject{["side"]="credit",["accountHint"]="212103",["erpRiderId"]=rider["erp_rider_id"]!.ToString(),["amount"]=net,["labelAr"]="صافي راتب متوقع"});
                lines.Add(new JsonObject{["side"]="debit",["accountHint"]="526401",["erpRiderId"]=rider["erp_rider_id"]!.ToString(),["amount"]=net,["labelAr"]="مصروف راتب متوقع"});
                debit+=net;credit+=net;
            }
        }
        return WithActions(actor,run,new()
        {
            ["id"]=run.S("id"),["runMode"]=1,["companyId"]=run.S("companyId"),["app"]=run.S("app"),
            ["periodMonth"]=run.S("periodMonth"),["riderCount"]=riders.Count,
            ["totalDebit"]=J.Money(debit),["totalCredit"]=J.Money(credit),["lines"]=lines,
            ["posted"]=false,["noteAr"]="معاينة فقط — لا ترحيل ولا أرصدة في وضع المحاكاة."
        });
    }

    public static JsonObject Discard(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,RunPermission);
        var run=LoadSim(c,tx,actor,id);
        J.Need(run.N("status")!=StatusCancelled,"المحاكاة ملغاة مسبقاً.",409);
        // Discard is metadata only on a simulation row — never posts, moves debt, or marks bank sent.
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run")} SET status=@s,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@s",StatusCancelled),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O")),("@id",id));
        Store.Audit(c,tx,"sal.simulation_discarded",id,actor.Identity!.Name!);
        run["status"]=StatusCancelled;
        return WithActions(actor,run,run);
    }

    public static JsonObject PromoteDiff(DbConnection c,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,PromotePermission);
        var run=LoadSim(c,null,actor,id);
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        string company=run.S("companyId"),app=run.S("app"),month=run.S("periodMonth");
        var live=FindLive(c,null,company,app,month);
        var simRiders=ListRiders(c,null,id).ToDictionary(r=>r["erp_rider_id"]!.ToString()!,StringComparer.OrdinalIgnoreCase);
        var liveRiders=live==null?new Dictionary<string,Dictionary<string,object?>>(StringComparer.OrdinalIgnoreCase)
            :ListRiders(c,null,live.S("id")).ToDictionary(r=>r["erp_rider_id"]!.ToString()!,StringComparer.OrdinalIgnoreCase);
        var rows=new JsonArray();
        foreach(string key in simRiders.Keys.Union(liveRiders.Keys,StringComparer.OrdinalIgnoreCase))
        {
            simRiders.TryGetValue(key,out var s);liveRiders.TryGetValue(key,out var l);
            decimal simNet=s==null?0:J.Money(Convert.ToDecimal(s["salary_net"]));
            decimal liveNet=l==null?0:J.Money(Convert.ToDecimal(l["salary_net"]));
            string change=s==null?"live_only":l==null?"sim_only":simNet==liveNet?"same":"changed";
            rows.Add(new JsonObject{["erpRiderId"]=key,["simNet"]=simNet,["liveNet"]=liveNet,["change"]=change});
        }
        return WithActions(actor,run,new()
        {
            ["id"]=id,["liveRunId"]=live?.S("id")??"",["companyId"]=company,["app"]=app,["periodMonth"]=month,
            ["rows"]=rows,["summary"]=new JsonObject
            {
                ["simOnly"]=rows.Count(r=>r!.AsObject().S("change")=="sim_only"),
                ["liveOnly"]=rows.Count(r=>r!.AsObject().S("change")=="live_only"),
                ["changed"]=rows.Count(r=>r!.AsObject().S("change")=="changed"),
                ["same"]=rows.Count(r=>r!.AsObject().S("change")=="same")
            }
        });
    }

    public static JsonObject PromoteReal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,PromotePermission);
        var run=LoadSim(c,tx,actor,id);
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        J.Need(run.N("status")!=StatusCancelled,"لا ترقّى محاكاة ملغاة.",409);
        J.Need(FindLive(c,tx,run.S("companyId"),run.S("app"),run.S("periodMonth"))==null,
            "يوجد دورة حية لنفس التطبيق والشركة والشهر؛ أغلقها أو ألغها قبل الترقية.",409);
        string newId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        // Live draft only — still needs every normal approval path after this (U-W2-27 / pay.approve).
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","pay_run")}
            (id,period_month,app_code,company_id,stage,status,run_mode,payout_blocked,currency_code,capture_state,created_by,created_at)
            VALUES(@id,@pm,@app,@c,0,@st,@rm,@pb,'SAR',0,@by,@at)
            """,("@id",newId),("@pm",run.S("periodMonth")),("@app",run.S("app")),("@c",run.S("companyId")),
            ("@st",StatusActive),("@rm",RunModeLive),("@pb",run.B("payoutBlocked")?1:0),
            ("@by",actor.Identity!.Name!),("@at",now));
        foreach(var rider in ListRiders(c,tx,id))
        {
            string riderId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","pay_run_rider")}
                (id,pay_run_id,company_id,period_month,erp_rider_id,run_mode,gross,commission,salary_net,old_debt_instalment,debt_confirm_status,bank_eligible,keeta_ineligible,created_by,created_at)
                VALUES(@id,@run,@c,@pm,@erp,0,@g,@comm,@net,@debt,'pending',0,0,@by,@at)
                """,("@id",riderId),("@run",newId),("@c",run.S("companyId")),("@pm",run.S("periodMonth")),
                ("@erp",rider["erp_rider_id"]!.ToString()!),("@g",rider["gross"]!),("@comm",rider["commission"]!),
                ("@net",rider["salary_net"]!),("@debt",rider["old_debt_instalment"]!),("@by",actor.Identity!.Name!),("@at",now));
        }
        Store.Audit(c,tx,"sal.simulation_promoted",newId,actor.Identity!.Name!);
        return WithActions(actor,run,new()
        {
            ["id"]=newId,["sourceSimulationId"]=id,["runMode"]=0,["status"]=StatusActive,
            ["companyId"]=run.S("companyId"),["app"]=run.S("app"),["periodMonth"]=run.S("periodMonth"),
            ["noteAr"]="مسودة حية — ما زالت تحتاج كل الاعتمادات العادية قبل أي أثر."
        });
    }

    static JsonObject LoadSim(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,company_id,app_code,period_month,status,run_mode,payout_blocked
            FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",id));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"دورة المحاكاة غير موجودة.",404);
        J.Need(Convert.ToInt32(reader["run_mode"])==RunModeSimulation,"هذه الدورة ليست محاكاة.",409);
        var run=new JsonObject
        {
            ["id"]=reader["id"].ToString(),
            ["companyId"]=reader["company_id"].ToString(),
            ["app"]=reader["app_code"].ToString(),
            ["periodMonth"]=reader["period_month"].ToString()!.Length>=10?reader["period_month"].ToString()![..10]:reader["period_month"].ToString(),
            ["status"]=Convert.ToInt32(reader["status"]),
            ["runMode"]=1,
            ["payoutBlocked"]=Convert.ToInt32(reader["payout_blocked"])==1
        };
        reader.Close();
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        return run;
    }

    static JsonObject? FindLive(DbConnection c,DbTransaction? tx,string company,string app,string month)
    {
        object? id=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"sal","pay_run")}
            WHERE company_id=@c AND app_code=@app AND period_month=@pm AND run_mode=0 AND status<>@cancelled
            """,("@c",company),("@app",app),("@pm",month),("@cancelled",StatusCancelled));
        return id==null?null:new(){["id"]=id.ToString()};
    }

    static List<Dictionary<string,object?>> ListRiders(DbConnection c,DbTransaction? tx,string runId)
    {
        var rows=new List<Dictionary<string,object?>>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT erp_rider_id,gross,commission,salary_net,old_debt_instalment
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} WHERE pay_run_id=@id
            """,("@id",runId));
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            rows.Add(new()
            {
                ["erp_rider_id"]=reader["erp_rider_id"],
                ["gross"]=reader["gross"],
                ["commission"]=reader["commission"],
                ["salary_net"]=reader["salary_net"],
                ["old_debt_instalment"]=reader["old_debt_instalment"]
            });
        return rows;
    }

    static JsonObject WithActions(ClaimsPrincipal actor,JsonObject run,JsonObject body)
    {
        var actions=new JsonArray();
        bool cancelled=run.N("status")==StatusCancelled;
        if(!cancelled&&AccessPolicy.Has(actor,RunPermission))actions.Add("discard_simulation");
        if(!cancelled&&AccessPolicy.Has(actor,PromotePermission))actions.Add("promote_simulation");
        body["allowedActions"]=actions;
        return body;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        // Dual-store path: when sal.pay_run exists on the connection used by Store, prefer SQL; otherwise report clear 501 until money schema is wired into Store.Ensure.
        api.MapGet("/sal/simulations/{id}/journal-preview",(string id,HttpContext ctx)=>store.Read(c=>
        {
            EnsureSalSchema(c);
            return JournalPreview(c,ctx.User,id);
        })).WithTags("Payroll").WithSummary("Preview expected journals for a simulation pay run (no post)");
        api.MapPost("/sal/simulations/{id}/discard",(string id,HttpContext ctx)=>store.Write((c,tx)=>
        {
            EnsureSalSchema(c,tx);
            return Discard(c,tx,ctx.User,id);
        })).WithTags("Payroll").WithSummary("Discard a simulation pay run");
        api.MapGet("/sal/simulations/{id}/promote-diff",(string id,HttpContext ctx)=>store.Read(c=>
        {
            EnsureSalSchema(c);
            return PromoteDiff(c,ctx.User,id);
        })).WithTags("Payroll").WithSummary("Diff simulation riders against the live run for the same month");
        api.MapPost("/sal/simulations/{id}/promote-real",(string id,HttpContext ctx)=>store.Write((c,tx)=>
        {
            EnsureSalSchema(c,tx);
            return PromoteReal(c,tx,ctx.User,id);
        })).WithTags("Payroll").WithSummary("Promote a simulation into a live draft pay run (approvals still required)");
    }

    static void EnsureSalSchema(DbConnection c,DbTransaction? tx=null)
    {
        if(SchemaSql.TableExists(c,tx,"sal","pay_run"))return;
        SchemaRef.Ensure(c,tx);SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);
    }
}
