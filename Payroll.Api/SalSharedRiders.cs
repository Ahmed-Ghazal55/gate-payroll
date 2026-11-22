using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WC4D H11 (§40.5, M-0587): a rider who worked more than one app in the month is held by default.
/// Worked apps come from every live run of the month: riders already calculated AND the frozen work timelines
/// (the capture's assignment kind covers all apps of the rider), so the first app cannot approve before the others exist.</summary>
public static class SalSharedRiders
{
    public static Dictionary<string,HashSet<string>> WorkedApps(DbConnection c,DbTransaction tx,string periodMonth)
    {
        var map=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string rider,string app)
        {
            if(rider==""||app=="")return;
            if(!map.TryGetValue(rider,out var set))map[rider]=set=new(StringComparer.Ordinal);
            set.Add(PayPlatforms.For(app).App);
        }
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT r.erp_rider_id,pr.app_code FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.period_month=@pm AND pr.status<>4
                """;
            Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            while(r.Read())Add(r["erp_rider_id"].ToString()!,r["app_code"].ToString()!);
        }
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT pr.app_code,s.payload FROM {SchemaSql.Q(c,"fin","operation_snapshot_link")} l
                INNER JOIN {SchemaSql.Q(c,"sal","ops_snapshot")} s ON s.id=l.snapshot_id
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=l.operation_id
                WHERE l.operation_type='pay_run' AND l.snapshot_kind='assignment' AND pr.period_month=@pm AND pr.status<>4
                """;
            Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                if(JsonNode.Parse(r["payload"].ToString()!) is not JsonObject row)continue;
                string app=SalPayCalc.AssignmentApp(row);
                Add(SalPayCalc.AssignmentRider(row),app==""?r["app_code"].ToString()!:app);
            }
        }
        return map;
    }

    public static HashSet<string> AppsFor(Dictionary<string,HashSet<string>> worked,string erpRider,string runApp)
    {
        var apps=new HashSet<string>(worked.TryGetValue(erpRider,out var set)?set:[],StringComparer.Ordinal){PayPlatforms.For(runApp).App};
        return apps;
    }

    /// <summary>Open (or extend) the hold with one row per worked app; an app is "received" once its run has calculated the rider.</summary>
    public static void OpenHold(DbConnection c,DbTransaction tx,string erpRider,string periodMonth,IEnumerable<string> apps,string calculatingApp)
    {
        object? cleared=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'",
            ("@erp",erpRider),("@pm",periodMonth));
        if(cleared is not null and not DBNull)return; // exception release already decided by a second person (M-0587)
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'",
            ("@erp",erpRider),("@pm",periodMonth));
        string holdId;
        if(existing is null or DBNull)
        {
            holdId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold")}(id,erp_rider_id,period_month,status,created_at) VALUES(@id,@erp,@pm,'open',@at)",
                ("@id",holdId),("@erp",erpRider),("@pm",periodMonth),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        }
        else holdId=existing.ToString()!;
        foreach(string app in apps)
        {
            object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND app_code=@a",("@h",holdId),("@a",app));
            if(has is null or DBNull)
                SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold_app")}(id,hold_id,app_code,received) VALUES(@id,@h,@a,0)",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@h",holdId),("@a",app));
            object? calculated=SchemaSql.Scalar(c,tx,$"""
                SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.app_code=@a AND pr.status<>4
                """,("@erp",erpRider),("@pm",periodMonth),("@a",app));
            if(app==calculatingApp||calculated is not null and not DBNull)
                SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold_app")} SET received=1 WHERE hold_id=@h AND app_code=@a",("@h",holdId),("@a",app));
        }
    }

    /// <summary>Approval gate: a multi-app rider needs a cleared (second-person exception) hold; an open hold always blocks.</summary>
    public static void DemandApprovable(DbConnection c,DbTransaction tx,Dictionary<string,HashSet<string>> worked,string erpRider,string periodMonth,string runApp)
    {
        object? open=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'",
            ("@erp",erpRider),("@pm",periodMonth));
        J.Need(open is null or DBNull,"مندوب عمل مشترك معلّق — أكمل دمج التطبيقات قبل الاعتماد.",409);
        if(AppsFor(worked,erpRider,runApp).Count<=1)return;
        object? cleared=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'",
            ("@erp",erpRider),("@pm",periodMonth));
        J.Need(cleared is not null and not DBNull,"المندوب عمل على أكثر من تطبيق هذا الشهر؛ موقوف حتى تكتمل كل التطبيقات ويُدمج (§40.5).",409);
    }

    static void Param(DbCommand cmd,string name,object value){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}
