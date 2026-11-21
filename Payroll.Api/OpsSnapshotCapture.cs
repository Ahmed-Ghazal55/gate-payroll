using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-12: capture used ERP fields into sal.ops_snapshot (replacing PayRunInput writes).
/// Freeze only after every required kind has a frozen link or reasoned empty; no replace after freeze.</summary>
public static class OpsSnapshotCapture
{
    public const string CapturePermission="pay.manage";
    const byte ManifestCaptured=1,ManifestEmpty=2,ManifestMissing=3;
    public const byte CaptureOpen=0,CaptureFrozen=1;

    /// <summary>Used-field allowlists per catalogue kind (fields-used only).</summary>
    public static readonly IReadOnlyDictionary<string,string[]> UsedFields=new Dictionary<string,string[]>(StringComparer.Ordinal)
    {
        ["ninja_daily"]=["id","reportDate","platformRiderAccountId","riderProfileId","riderClientAssignmentId","captainId","externalAccountId","accountCode","orders","hours","ownerUnifiedNumber"],
        ["ninja_daily_deduction"]=["id","reportDate","platformRiderAccountId","actualRiderProfileId","riderClientAssignmentId","captainId","externalAccountId","accountCode","collectedAmount","deductionsAmount","ownerUnifiedNumber"],
        ["platform_account"]=["id","externalAccountId","code","sponsorId","registeredEmployeeId","platformCode"],
        ["assignment"]=["id","platformRiderAccountId","actualRiderProfileId","effectiveFrom","effectiveTo","assignmentPaymentModel","assignmentStatus"],
        ["housing"]=["id","erpRiderId","housingId","effectiveFrom","effectiveTo","roomId","month","monthOnly"],
        ["fuel"]=["id","erpRiderId","liters","totalInclVat","amount","month","fuelCardId","plateNumber","effectiveFrom","effectiveTo","monthOnly"],
        ["vehicle"]=["id","assetNumber","plateNumberAr","vehicleType","sponsorId","actualRiderProfileId","erpRiderId","effectiveFrom","effectiveTo"],
        ["scheme"]=["id","erpRiderId","schemeCode","source"],
        ["eligibility"]=["id","erpRiderId","eligible","source"],
        ["keeta_invoice_row"]=["id","erpPlatformAccountId","gross","deductions","orders"],
        ["hs_ops"]=["id","erpPlatformAccountId","orders","workDate"],
        ["hs_wallet"]=["id","erpRiderId","balance","asOf","fundingType","amount","eventType","offsetsTopup"],
        ["platform_ops"]=["id","erpPlatformAccountId","orders","workDate"],
        ["chefz_settlement"]=["id","erpRiderId","RiderAmount","GrossEarnings","CompanyAmount"],
        ["jahez_day"]=["id","erpRiderId","platformRiderAccountId","net","commission","orders","workDate"]
    };

    public static JsonObject Capture(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,IOpsLiveFactsReader facts,IReadOnlyDictionary<string,string>? zeroKindReasons=null)
    {
        AccessPolicy.Demand(actor,CapturePermission);
        var run=LoadRun(c,tx,actor,payRunId);
        J.Need(run.N("captureState")==CaptureOpen,"لا يمكن إعادة التقاط المدخلات بعد التجميد؛ ألغِ الدورة وأنشئ أخرى.",409);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);
        SchemaOperationCapture.Ensure(c,tx);
        string company=run.S("companyId"),app=run.S("app"),month=run.S("periodMonth");
        var required=RequiredKinds(c,tx,app);
        J.Need(required.Count>0,"لا كتالوج أنواع مطلوب لهذا التطبيق.",409);
        zeroKindReasons??=new Dictionary<string,string>(StringComparer.Ordinal);
        // Recapture on an open run replaces prior links/manifest (snapshots stay append-only evidence).
        ClearOpenCaptureLinks(c,tx,payRunId);
        int snapshots=0;
        foreach(string kind in required)
        {
            var rows=facts.RowsFor(kind,company,app,month).ToList();
            if(rows.Count==0)
            {
                // Empty only freezes with a written reason AND a confirmed live read (never StoreOps fallback).
                J.Need(facts.ConfirmsLiveRead,"مصدر التقاط غير حي لنوع مطلوب: "+kind+"؛ سجّل قارئاً حياً أو حقن اختبار.",409);
                string reason=zeroKindReasons.TryGetValue(kind,out var r)?r.Trim():"";
                J.Need(reason!="","لا صفوف لنوع مطلوب قبل التقاط المدخلات: "+kind+"؛ سجّل السبب أو صحّح المصدر.",409);
                UpsertManifest(c,tx,payRunId,kind,month,ManifestEmpty,null,reason);
                continue;
            }
            string? firstLink=null;
            foreach(var row in rows)
            {
                string link=AppendSnapshot(c,tx,actor,payRunId,company,app,month,kind,row);
                firstLink??=link;
                snapshots++;
            }
            UpsertManifest(c,tx,payRunId,kind,month,ManifestCaptured,firstLink,null);
        }
        Store.Audit(c,tx,"sal.ops_captured",payRunId,actor.Identity!.Name!);
        return WithActions(run,new()
        {
            ["id"]=payRunId,["companyId"]=company,["app"]=app,["periodMonth"]=month,
            ["captureState"]=CaptureOpen,["snapshotCount"]=snapshots,["requiredKinds"]=required.Count,
            ["noteAr"]="التقاط مفتوح — جمّد بعد اكتمال كل الأنواع المطلوبة."
        });
    }

    public static JsonObject Freeze(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId)
    {
        AccessPolicy.Demand(actor,CapturePermission);
        var run=LoadRun(c,tx,actor,payRunId);
        J.Need(run.N("captureState")==CaptureOpen,"الدورة مجمّدة مسبقاً.",409);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        // Capture gate trigger enforces exhaustive manifest + matching links before capture_state>0.
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","pay_run")}
            SET capture_state=@cs,stage=CASE WHEN stage<2 THEN 2 ELSE stage END,calc_started_at=@at,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@cs",CaptureFrozen),("@at",now),("@by",actor.Identity!.Name!),("@id",payRunId));
        Store.Audit(c,tx,"sal.ops_capture_frozen",payRunId,actor.Identity!.Name!);
        run["captureState"]=CaptureFrozen;
        return WithActions(run,new()
        {
            ["id"]=payRunId,["companyId"]=run.S("companyId"),["app"]=run.S("app"),["periodMonth"]=run.S("periodMonth"),
            ["captureState"]=CaptureFrozen,["frozenAt"]=now,["noteAr"]="التقاط مجمّد — الحساب مسموح."
        });
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/pay-runs/{id}/capture-ops",(string id,JsonObject? input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);
            var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
            if(input?["zeroKindReasons"] is JsonObject z)
                foreach(var kv in z)if(kv.Value!=null)reasons[kv.Key]=kv.Value.ToString()??"";
            var facts=ctx.RequestServices.GetRequiredService<IOpsLiveFactsReader>();
            return Capture(c,tx,ctx.User,id,facts,reasons);
        })).WithTags("Payroll").WithSummary("Capture used ERP fields into sal.ops_snapshot for a pay run");
        api.MapPost("/sal/pay-runs/{id}/freeze-capture",(string id,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);
            return Freeze(c,tx,ctx.User,id);
        })).WithTags("Payroll").WithSummary("Freeze ops snapshot capture after every required kind is linked");
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        if(SchemaSql.TableExists(c,tx,"sal","ops_snapshot"))return;
        SchemaRef.Ensure(c,tx);SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);SchemaFinPeriod.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);SchemaOperationCapture.Ensure(c,tx);
    }

    static JsonObject LoadRun(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,company_id,app_code,period_month,status,run_mode,payout_blocked,capture_state
            FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",id));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"دورة الرواتب غير موجودة.",404);
        var run=new JsonObject
        {
            ["id"]=reader["id"].ToString(),
            ["companyId"]=reader["company_id"].ToString(),
            ["app"]=reader["app_code"].ToString(),
            ["periodMonth"]=ReadMonth(reader["period_month"]),
            ["status"]=Convert.ToInt32(reader["status"]),
            ["runMode"]=Convert.ToInt32(reader["run_mode"]),
            ["payoutBlocked"]=Convert.ToInt32(reader["payout_blocked"])==1,
            ["captureState"]=Convert.ToInt32(reader["capture_state"])
        };
        reader.Close();
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        var platform=PayPlatforms.For(run.S("app"));
        J.Need(FinancialScopes.Platform(actor,platform.Platform),"المنصة خارج نطاق المستخدم.",403);
        return run;
    }

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>s.Length>=10?s[..10]:s,
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };

    static List<string> RequiredKinds(DbConnection c,DbTransaction? tx,string app)
    {
        var kinds=new List<string>();
        using var cmd=Store.Command(c,tx,$"SELECT required_kind FROM {SchemaSql.Q(c,"fin","operation_required_kind")} WHERE operation_type='pay_run' AND app_code=@app ORDER BY required_kind",("@app",app));
        using var reader=cmd.ExecuteReader();
        while(reader.Read())kinds.Add(reader.GetString(0));
        return kinds;
    }

    static void ClearOpenCaptureLinks(DbConnection c,DbTransaction tx,string payRunId)
    {
        // Manifest first (FK to link), then links. Snapshots remain; new capture appends fresh rows.
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"fin","operation_capture_manifest")} WHERE operation_type='pay_run' AND operation_id=@run",("@run",payRunId));
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"fin","operation_snapshot_link")} WHERE operation_type='pay_run' AND operation_id=@run",("@run",payRunId));
    }

    static string AppendSnapshot(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,string company,string app,string month,string kind,JsonObject row)
    {
        var used=ProjectUsed(kind,row);
        string sourceKey=used.S("id");
        J.Need(sourceKey!="","صف التشغيل بلا معرّف.",400);
        // SQL Server erp_row_id is uniqueidentifier — keep textual keys (pay-assignment:…) in payload.sourceKey.
        string erpRow=StableErpRowId(sourceKey);
        if(!string.Equals(sourceKey,erpRow,StringComparison.Ordinal))
            used["sourceKey"]=sourceKey;
        byte[] hash=Fingerprint(used);
        string snapId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string payload=used.ToJsonString(new JsonSerializerOptions{WriteIndented=false});
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","ops_snapshot")}
            (id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,payload,generation)
            VALUES(@id,@pm,@kind,@erp,@c,@app,@at,@by,@hash,@payload,'new')
            """,("@id",snapId),("@pm",month),("@kind",kind),("@erp",erpRow),("@c",company),("@app",app),
            ("@at",now),("@by",actor.Identity!.Name!),("@hash",hash),("@payload",payload));
        string linkId=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","operation_snapshot_link")}
            (id,operation_type,operation_id,snapshot_id,snapshot_kind,erp_row_id,source_fingerprint,period_month)
            VALUES(@id,'pay_run',@run,@snap,@kind,@erp,@hash,@pm)
            """,("@id",linkId),("@run",payRunId),("@snap",snapId),("@kind",kind),("@erp",erpRow),("@hash",hash),("@pm",month));
        return linkId;
    }

    /// <summary>Map any source id to a GUID column value; preserve the original via payload.sourceKey when hashed.</summary>
    public static string StableErpRowId(string sourceKey)
    {
        if(Guid.TryParse(sourceKey,out var g))return SchemaSql.GuidText(g);
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes("ops-erp-row:"+sourceKey));
        Span<byte> guidBytes=stackalloc byte[16];
        hash.AsSpan(0,16).CopyTo(guidBytes);
        return SchemaSql.GuidText(new Guid(guidBytes));
    }

    static void UpsertManifest(DbConnection c,DbTransaction tx,string payRunId,string kind,string month,byte status,string? linkId,string? emptyReason)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","operation_capture_manifest")} WHERE operation_type='pay_run' AND operation_id=@run AND required_kind=@kind",
            ("@run",payRunId),("@kind",kind));
        if(existing!=null)
        {
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"fin","operation_capture_manifest")}
                SET status=@st,empty_reason=@reason,snapshot_link_id=@link,period_month=@pm
                WHERE id=@id
                """,("@st",status),("@reason",(object?)emptyReason??DBNull.Value),("@link",(object?)linkId??DBNull.Value),("@pm",month),("@id",existing.ToString()!));
            return;
        }
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","operation_capture_manifest")}
            (id,operation_type,operation_id,required_kind,status,empty_reason,snapshot_link_id,period_month)
            VALUES(@id,'pay_run',@run,@kind,@st,@reason,@link,@pm)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@run",payRunId),("@kind",kind),("@st",status),
            ("@reason",(object?)emptyReason??DBNull.Value),("@link",(object?)linkId??DBNull.Value),("@pm",month));
    }

    public static JsonObject ProjectUsed(string kind,JsonObject row)
    {
        var fields=UsedFields.TryGetValue(kind,out var list)?list:["id"];
        var projected=new JsonObject();
        foreach(string field in fields)
        {
            if(!row.TryGetPropertyValue(field,out var node)||node is null)continue;
            projected[field]=node.DeepClone();
        }
        if(!projected.ContainsKey("id")&&row.TryGetPropertyValue("id",out var id)&&id!=null)projected["id"]=id.DeepClone();
        return projected;
    }

    public static byte[] Fingerprint(JsonObject used)
    {
        string canonical=used.ToJsonString(new JsonSerializerOptions{WriteIndented=false});
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    static JsonObject WithActions(JsonObject run,JsonObject body)
    {
        var actions=new JsonArray();
        if(run.N("captureState")==CaptureOpen)
        {
            actions.Add("capture_ops");
            actions.Add("freeze_capture");
        }
        body["allowedActions"]=actions;
        return body;
    }
}

/// <summary>Live/fake facts for ops snapshot capture. Tests inject FakeOpsLiveFactsReader.</summary>
public interface IOpsLiveFactsReader
{
    /// <summary>True when RowsFor queried a real/fake live source (0 rows still count). False for empty Store fallback.</summary>
    bool ConfirmsLiveRead{get;}
    IEnumerable<JsonObject> RowsFor(string kind,string companyId,string app,string periodMonth);
}

public sealed class FakeOpsLiveFactsReader:IOpsLiveFactsReader
{
    public bool ConfirmsLiveRead=>true;
    public Dictionary<string,List<JsonObject>> Rows{get;}=new(StringComparer.Ordinal);
    public IEnumerable<JsonObject> RowsFor(string kind,string companyId,string app,string periodMonth)
        =>Rows.TryGetValue(kind,out var list)?list:Enumerable.Empty<JsonObject>();
}

/// <summary>Unavailable empty fallback — never confirms a live read. Capture refuses empty kinds from this reader.</summary>
public sealed class StoreOpsLiveFactsReader:IOpsLiveFactsReader
{
    public bool ConfirmsLiveRead=>false;
    public IEnumerable<JsonObject> RowsFor(string kind,string companyId,string app,string periodMonth)
    {
        _=kind;_=companyId;_=app;_=periodMonth;
        return [];
    }
}
