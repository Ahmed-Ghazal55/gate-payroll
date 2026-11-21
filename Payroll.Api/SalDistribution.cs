using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-13: distribution + bearer. TGA allocate; settlements/combine; Keeta rejection after free quota;
/// entitlement per platform account from frozen assignments; Ninja order-share only; TGA never reduces distributable.</summary>
public static class SalDistribution
{
    public const string ManagePermission="pay.manage",ApprovePermission="pay.approve";
    public const decimal KeetaRejectionSar=50m;
    public const int KeetaFreeRejectionsPerDay=2;
    public const byte BearerPending=1,BearerReady=2,BearerNeedsDecision=3;

    public static JsonObject AllocateTga(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        string invoiceId=input.Required("invoiceId"),itemId=input.Required("itemId");
        var invoice=LoadInvoice(c,tx,actor,invoiceId);
        MoneyWriteGuards.Demand((byte)invoice.N("runMode"),invoice.B("payoutBlocked"),MoneyEffectKind.Accounting);
        var item=LoadItem(c,tx,itemId,invoiceId);
        J.Need(string.Equals(item.S("itemCode"),"tga",StringComparison.OrdinalIgnoreCase),"البند ليس مخالفة هيئة نقل.",409);
        decimal target=J.Money(item.N("amount"));
        var raw=(input["allocations"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(raw.Count>0,"أدخل توزيع المتحمّلين.",400);
        var allocated=ApplyResidual(raw,target);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"sal","item_bearer")} WHERE item_id=@id",("@id",itemId));
        var bearers=new JsonArray();
        int lineNo=1;
        foreach(var row in allocated)
        {
            string bearer=row.S("bearer");
            J.Need(bearer is "company" or "rider","المتحمّل شركة أو مندوب فقط.",400);
            string? rider=bearer=="rider"?row.Required("erpRiderId"):null;
            if(bearer=="rider")J.Need(!string.IsNullOrWhiteSpace(rider),"مندوب المتحمّل مطلوب.",400);
            string id=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","item_bearer")}
                (id,period_month,item_id,line_no,bearer,erp_rider_id,amount,from_date,to_date,created_by,created_at)
                VALUES(@id,@pm,@item,@line,@bearer,@rider,@amount,@from,@to,@by,@at)
                """,("@id",id),("@pm",invoice.S("periodMonth")),("@item",itemId),("@line",lineNo),
                ("@bearer",bearer),("@rider",(object?)rider??DBNull.Value),("@amount",row.N("amount")),
                ("@from",NullDate(row.S("fromDate"))),("@to",NullDate(row.S("toDate"))),
                ("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
            bearers.Add(new JsonObject{["id"]=id,["lineNo"]=lineNo,["bearer"]=bearer,["erpRiderId"]=rider??"",["amount"]=row.N("amount"),["fromDate"]=row.S("fromDate"),["toDate"]=row.S("toDate")});
            lineNo++;
        }
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","platform_invoice_item")} SET bearer_status=@st WHERE id=@id",("@st",BearerReady),("@id",itemId));
        Store.Audit(c,tx,"sal.tga_allocated",itemId,actor.Identity!.Name!);
        return new()
        {
            ["invoiceId"]=invoiceId,["itemId"]=itemId,["allocatedTotal"]=target,["bearers"]=bearers,
            ["allowedActions"]=new JsonArray("allocate_tga")
        };
    }

    public static JsonObject Propose(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        string invoiceId=input.Required("invoiceId"),app=PayPlatforms.App(input.S("app",input.S("appCode")));
        string payRunId=input.Required("payRunId");
        var invoice=LoadInvoice(c,tx,actor,invoiceId);
        MoneyWriteGuards.Demand((byte)invoice.N("runMode"),invoice.B("payoutBlocked"),MoneyEffectKind.Accounting);
        J.Need(string.Equals(invoice.S("app"),app,StringComparison.Ordinal),"التطبيق لا يطابق الفاتورة.",409);
        DemandCaptureFrozen(c,tx,payRunId,invoice);
        DemandNoPendingBearers(c,tx,invoiceId);
        bool ninja=app=="ninja";
        if(!ninja)
            throw new ApiError("توزيع الحصة بنسبة الطلبات لنينجا فقط؛ لباقي التطبيقات عيّن متحمّلاً صريحاً لكل بند.",409);

        var lines=AggregateLinesByAccount(LoadLines(c,tx,invoiceId));
        J.Need(lines.Count>0,"لا بنود فاتورة للتوزيع.",409);
        var assignments=LoadFrozenKind(c,tx,payRunId,"assignment");
        var daily=LoadFrozenKind(c,tx,payRunId,"ninja_daily");
        // Caller-supplied workers/orders/dates are ignored for identity; reject when they conflict with frozen facts.
        RejectConflictingWorkers(input,assignments,daily);

        decimal entitlement=EntitlementBeforeTga(c,tx,invoiceId);
        var riderPenalties=RiderNonTgaBearers(c,tx,invoiceId);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"sal","distribution")} WHERE invoice_id=@id",("@id",invoiceId));
        var rows=new JsonArray();
        decimal distributed=0;
        foreach(var line in lines)
        {
            string account=line.S("erpPlatformAccountId");
            J.Need(account!="","معرّف حساب المنصة مطلوب على بند الفاتورة؛ لا يُستبدل بالسجل التجاري.",400);
            decimal lineShare=LineEntitlement(c,tx,line);
            var workers=WorkersForAccount(account,assignments,daily);
            J.Need(workers.Count>0,"لا مندوب فعلي على الحساب من التعيينات المجمّدة: "+account,409);
            int orders=workers.Sum(w=>w.Orders);
            J.Need(orders>0,"إجمالي الطلبات يجب أن يكون أكبر من صفر للحساب "+account,400);
            decimal kept=0;
            for(int i=0;i<workers.Count;i++)
            {
                var w=workers[i];
                decimal raw=i==workers.Count-1?J.Money(lineShare-kept):J.Money(lineShare*((decimal)w.Orders/orders));
                kept+=raw;
                decimal penalty=riderPenalties.GetValueOrDefault(w.RiderId);
                if(penalty>0){riderPenalties[w.RiderId]=0;raw=J.Money(raw-penalty);J.Need(raw>=0,"حصة المندوب لا تغطي بنود التحميل المعينة.",409);}
                distributed+=raw;
                string id=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"sal","distribution")}
                    (id,period_month,invoice_id,erp_platform_account_id,erp_rider_id,orders,days,from_date,to_date,share_amount,method,status,created_by,created_at)
                    VALUES(@id,@pm,@inv,@acc,@rider,@orders,@days,@from,@to,@share,'order_share','ready',@by,@at)
                    """,("@id",id),("@pm",invoice.S("periodMonth")),("@inv",invoiceId),("@acc",account),("@rider",w.RiderId),
                    ("@orders",w.Orders),("@days",w.Days),("@from",NullDate(w.From)),("@to",NullDate(w.To)),
                    ("@share",raw),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
                rows.Add(new JsonObject{["id"]=id,["erpPlatformAccountId"]=account,["erpRiderId"]=w.RiderId,["orders"]=w.Orders,["shareAmount"]=raw,["fromDate"]=w.From,["toDate"]=w.To,["status"]="ready"});
            }
        }
        Store.Audit(c,tx,"sal.distribution_proposed",invoiceId,actor.Identity!.Name!);
        return new()
        {
            ["invoiceId"]=invoiceId,["app"]=app,["method"]="order_share",
            ["entitlementBeforeTga"]=entitlement,["distributedTotal"]=J.Money(distributed),["rows"]=rows,
            ["allowedActions"]=new JsonArray("propose_distribution","edit_bearer")
        };
    }

    public static JsonObject SplitKeetaRejections(JsonObject input)
    {
        string account=input.Required("erpPlatformAccountId");
        var days=(input["days"] as JsonArray??[]).OfType<JsonObject>().ToList();
        var workers=(input["workers"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(days.Count>0&&workers.Count>0,"أدخل أيام الرفض والمناديب العاملين على المعرف.",400);
        decimal total=0;
        var charges=new Dictionary<string,decimal>(StringComparer.Ordinal);
        foreach(var day in days)
        {
            string date=day.Required("date");
            int rejected=(int)day.N("rejected");
            int billable=Math.Max(0,rejected-KeetaFreeRejectionsPerDay);
            if(billable==0)continue;
            decimal charge=billable*KeetaRejectionSar;
            var onDay=workers.Where(w=>OverlapsDate(w.S("fromDate"),w.S("toDate"),date)).Select(w=>w.S("erpRiderId")).Where(id=>id!="").Distinct(StringComparer.Ordinal).ToList();
            J.Need(onDay.Count>0,"لا مندوب فعلي بتاريخ الرفض "+date+" على المعرف.",409);
            decimal each=J.Money(charge/onDay.Count),kept=0;
            for(int i=0;i<onDay.Count;i++)
            {
                decimal part=i==onDay.Count-1?J.Money(charge-kept):each;
                kept+=part;
                charges[onDay[i]]=charges.GetValueOrDefault(onDay[i])+part;
            }
            total+=charge;
        }
        var bearers=new JsonArray(charges.Select(kv=>(JsonNode?)new JsonObject{["bearer"]="rider",["erpRiderId"]=kv.Key,["amount"]=kv.Value,["erpPlatformAccountId"]=account}).ToArray());
        return new(){["erpPlatformAccountId"]=account,["chargeTotal"]=total,["freeQuotaPerDay"]=KeetaFreeRejectionsPerDay,["rateSar"]=KeetaRejectionSar,["bearers"]=bearers};
    }

    public static JsonObject CombineSettlements(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        string app=PayPlatforms.App(input.Required("app"));
        string from=input.Required("periodFrom"),to=input.Required("periodTo");
        var ids=(input["invoiceIds"] as JsonArray??[]).Select(n=>n?.ToString()??"").Where(id=>id!="").Distinct(StringComparer.Ordinal).ToList();
        J.Need(ids.Count>=2,"دمج التسوية يحتاج فاتورتين على الأقل.",400);
        decimal gross=0,deductions=0,net=0;
        byte runMode=0;bool blocked=false;
        foreach(string id in ids)
        {
            var inv=LoadInvoice(c,tx,actor,id);
            J.Need(string.Equals(inv.S("app"),app,StringComparison.Ordinal),"كل الفواتير يجب أن تكون لنفس التطبيق.",409);
            MoneyWriteGuards.Demand((byte)inv.N("runMode"),inv.B("payoutBlocked"),MoneyEffectKind.Accounting);
            runMode=(byte)Math.Max(runMode,(byte)inv.N("runMode"));
            blocked=blocked||inv.B("payoutBlocked");
            gross+=inv.N("gross");deductions+=inv.N("deductions");net+=inv.N("net");
        }
        string settlementId=SchemaSql.GuidText(SchemaSql.NewId());
        string month=DateOnly.Parse(from[..Math.Min(10,from.Length)],CultureInfo.InvariantCulture).ToString("yyyy-MM-01",CultureInfo.InvariantCulture);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","app_settlement")}
            (id,app_code,period_from,period_to,period_month,status,gross,deductions,net,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@app,@from,@to,@pm,1,@g,@d,@n,@rm,@pb,@by,@at)
            """,("@id",settlementId),("@app",app),("@from",from),("@to",to),("@pm",month),
            ("@g",gross),("@d",deductions),("@n",net),("@rm",runMode),("@pb",blocked?1:0),
            ("@by",actor.Identity!.Name!),("@at",now));
        foreach(string invoiceId in ids)
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","app_settlement_invoice")}(id,settlement_id,invoice_id,period_month) VALUES(@id,@s,@inv,@pm)",
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",settlementId),("@inv",invoiceId),("@pm",month));
        Store.Audit(c,tx,"sal.settlement_combined",settlementId,actor.Identity!.Name!);
        return new()
        {
            ["id"]=settlementId,["app"]=app,["periodFrom"]=from,["periodTo"]=to,["invoiceCount"]=ids.Count,
            ["gross"]=gross,["deductions"]=deductions,["net"]=net,
            ["allowedActions"]=new JsonArray("combine_settlement")
        };
    }

    public static JsonObject AssignSharedBearer(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        string itemId=input.Required("itemId");
        var item=LoadItemScoped(c,tx,actor,itemId);
        MoneyWriteGuards.Demand((byte)item.N("runMode"),item.B("payoutBlocked"),MoneyEffectKind.Accounting);
        string itemDate=input.S("itemDate").Trim();
        var allocations=(input["allocations"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(allocations.Count>0,"أدخل توزيع المتحمّلين.",400);

        if(itemDate=="")
        {
            // Undated: supervisor must assign; persist needs_decision until every undated item is ready.
            bool hasRider=allocations.Any(a=>a.S("bearer")=="rider"&&a.S("erpRiderId")!="");
            if(!hasRider)
            {
                SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","platform_invoice_item")} SET bearer_status=@st WHERE id=@id",("@st",BearerNeedsDecision),("@id",itemId));
                Store.Audit(c,tx,"sal.shared_bearer_needs_decision",itemId,actor.Identity!.Name!);
                return new()
                {
                    ["itemId"]=itemId,["status"]="needs_decision",
                    ["noteAr"]="بند بلا تاريخ — يحتاج تعيين المشرف للمتحمّل.",
                    ["allowedActions"]=new JsonArray("assign_shared_bearer")
                };
            }
            foreach(var row in allocations.Where(a=>a.S("bearer")=="rider"))
                DemandRiderInFrozenAssignment(c,tx,item,input.S("payRunId"),row.S("erpRiderId"),"");
            PersistBearers(c,tx,actor,item,allocations);
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","platform_invoice_item")} SET bearer_status=@st WHERE id=@id",("@st",BearerReady),("@id",itemId));
            Store.Audit(c,tx,"sal.shared_bearer_assigned",itemId,actor.Identity!.Name!);
            return new()
            {
                ["itemId"]=itemId,["status"]="ready",["itemDate"]="",
                ["allocations"]=new JsonArray(allocations.Select(a=>(JsonNode?)a.DeepClone()).ToArray()),
                ["allowedActions"]=new JsonArray("edit_bearer","approve")
            };
        }

        string actual=input.S("actualRiderId");
        J.Need(actual!="","المندوب الفعلي بتاريخ البند مطلوب.",400);
        DemandRiderInFrozenAssignment(c,tx,item,input.S("payRunId"),actual,itemDate);
        foreach(var row in allocations.Where(a=>a.S("bearer")=="rider"))
            row["erpRiderId"]=actual;
        PersistBearers(c,tx,actor,item,allocations);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","platform_invoice_item")} SET bearer_status=@st WHERE id=@id",("@st",BearerReady),("@id",itemId));
        Store.Audit(c,tx,"sal.shared_bearer_assigned",itemId,actor.Identity!.Name!);
        return new()
        {
            ["itemId"]=itemId,["status"]="ready",["actualRiderId"]=actual,["itemDate"]=itemDate,
            ["allocations"]=new JsonArray(allocations.Select(a=>(JsonNode?)a.DeepClone()).ToArray()),
            ["allowedActions"]=new JsonArray("edit_bearer","approve")
        };
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/tga/allocate",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{Ensure(c,tx);return AllocateTga(c,tx,ctx.User,input);}))
            .WithTags("Payroll").WithSummary("Allocate TGA item bearers (sum=item, residual last)");
        api.MapPost("/sal/settlements/combine",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{Ensure(c,tx);return CombineSettlements(c,tx,ctx.User,input);}))
            .WithTags("Payroll").WithSummary("Combine CR invoices into an app settlement");
        api.MapPost("/sal/distributions/propose",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{Ensure(c,tx);return Propose(c,tx,ctx.User,input);}))
            .WithTags("Payroll").WithSummary("Propose Ninja order-share distribution from frozen account assignments");
        api.MapPost("/sal/item-bearers/assign-shared",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{Ensure(c,tx);return AssignSharedBearer(c,tx,ctx.User,input);}))
            .WithTags("Payroll").WithSummary("Assign shared-user bearer: dated actual rider or undated supervisor queue");
        api.MapPost("/sal/keeta/rejection-split",(JsonObject input,HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,ManagePermission);
            return SplitKeetaRejections(input);
        }).WithTags("Payroll").WithSummary("Split Keeta rejection charges after free daily quota on account ID");
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        if(SchemaSql.TableExists(c,tx,"sal","distribution"))return;
        SchemaRef.Ensure(c,tx);SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);SchemaFinPeriod.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        SchemaSalInvoice.Ensure(c,tx);SchemaSalDistribution.Ensure(c,tx);
    }

    static void PersistBearers(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject item,List<JsonObject> allocations)
    {
        string itemId=item.S("id");
        decimal target=J.Money(item.N("amount"));
        var allocated=ApplyResidual(allocations,target);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"sal","item_bearer")} WHERE item_id=@id",("@id",itemId));
        int lineNo=1;
        foreach(var row in allocated)
        {
            string bearer=row.S("bearer");
            J.Need(bearer is "company" or "rider","المتحمّل شركة أو مندوب فقط.",400);
            string? rider=bearer=="rider"?row.Required("erpRiderId"):null;
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","item_bearer")}
                (id,period_month,item_id,line_no,bearer,erp_rider_id,amount,from_date,to_date,created_by,created_at)
                VALUES(@id,@pm,@item,@line,@bearer,@rider,@amount,@from,@to,@by,@at)
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",item.S("periodMonth")),("@item",itemId),("@line",lineNo),
                ("@bearer",bearer),("@rider",(object?)rider??DBNull.Value),("@amount",row.N("amount")),
                ("@from",NullDate(row.S("fromDate"))),("@to",NullDate(row.S("toDate"))),
                ("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
            lineNo++;
        }
    }

    static List<JsonObject> ApplyResidual(List<JsonObject> raw,decimal target)
    {
        decimal sum=raw.Sum(r=>J.Money(r.N("amount")));
        var rows=raw.Select(r=>r.DeepClone()!.AsObject()).ToList();
        if(sum==target)return rows;
        J.Need(sum<=target,"مجموع التوزيع يتجاوز قيمة البند.",409);
        decimal residual=J.Money(target-sum);
        rows[^1]["amount"]=J.Money(rows[^1].N("amount")+residual);
        return rows;
    }

    static JsonObject LoadInvoice(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,app_code,company_id,period_month,erp_cr_id,gross,deductions,net,run_mode,payout_blocked,status
            FROM {SchemaSql.Q(c,"sal","platform_invoice")} WHERE id=@id
            """,("@id",id));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"الفاتورة غير موجودة.",404);
        var inv=new JsonObject
        {
            ["id"]=reader["id"].ToString(),["app"]=reader["app_code"].ToString(),["companyId"]=reader["company_id"].ToString(),
            ["periodMonth"]=ReadMonth(reader["period_month"]),["erpCrId"]=reader["erp_cr_id"].ToString(),
            ["gross"]=Convert.ToDecimal(reader["gross"]),["deductions"]=Convert.ToDecimal(reader["deductions"]),["net"]=Convert.ToDecimal(reader["net"]),
            ["runMode"]=Convert.ToInt32(reader["run_mode"]),["payoutBlocked"]=Convert.ToInt32(reader["payout_blocked"])==1,
            ["status"]=Convert.ToInt32(reader["status"])
        };
        reader.Close();
        FinancialScopes.DemandSponsor(actor,inv.S("companyId"));
        var platform=PayPlatforms.For(inv.S("app"));
        J.Need(FinancialScopes.Platform(actor,platform.Platform),"المنصة خارج نطاق المستخدم.",403);
        return inv;
    }

    static JsonObject LoadItem(DbConnection c,DbTransaction? tx,string itemId,string invoiceId)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT i.id,i.item_code,i.amount,i.period_month,i.bearer_status
            FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            WHERE i.id=@id AND l.invoice_id=@inv
            """,("@id",itemId),("@inv",invoiceId));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"بند الفاتورة غير موجود.",404);
        return new(){["id"]=reader["id"].ToString(),["itemCode"]=reader["item_code"].ToString(),["amount"]=Convert.ToDecimal(reader["amount"]),["periodMonth"]=ReadMonth(reader["period_month"]),["bearerStatus"]=Convert.ToInt32(reader["bearer_status"])};
    }

    static JsonObject LoadItemScoped(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string itemId)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT i.id,i.item_code,i.amount,i.period_month,i.bearer_status,
                   inv.id AS invoice_id,inv.app_code,inv.company_id,inv.run_mode,inv.payout_blocked,
                   l.erp_platform_account_id
            FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice")} inv ON inv.id=l.invoice_id
            WHERE i.id=@id
            """,("@id",itemId));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"بند الفاتورة غير موجود.",404);
        var item=new JsonObject
        {
            ["id"]=reader["id"].ToString(),["itemCode"]=reader["item_code"].ToString(),
            ["amount"]=Convert.ToDecimal(reader["amount"]),["periodMonth"]=ReadMonth(reader["period_month"]),
            ["bearerStatus"]=Convert.ToInt32(reader["bearer_status"]),["invoiceId"]=reader["invoice_id"].ToString(),
            ["app"]=reader["app_code"].ToString(),["companyId"]=reader["company_id"].ToString(),
            ["runMode"]=Convert.ToInt32(reader["run_mode"]),["payoutBlocked"]=Convert.ToInt32(reader["payout_blocked"])==1,
            ["erpPlatformAccountId"]=reader["erp_platform_account_id"] is DBNull?"":reader["erp_platform_account_id"].ToString()!
        };
        reader.Close();
        FinancialScopes.DemandSponsor(actor,item.S("companyId"));
        var platform=PayPlatforms.For(item.S("app"));
        J.Need(FinancialScopes.Platform(actor,platform.Platform),"المنصة خارج نطاق المستخدم.",403);
        return item;
    }

    static List<JsonObject> LoadLines(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        var lines=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,erp_platform_account_id,gross,deductions,net,orders
            FROM {SchemaSql.Q(c,"sal","platform_invoice_line")} WHERE invoice_id=@id ORDER BY source_row
            """,("@id",invoiceId));
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            lines.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["erpPlatformAccountId"]=reader["erp_platform_account_id"] is DBNull?"":reader["erp_platform_account_id"].ToString(),
                ["gross"]=Convert.ToDecimal(reader["gross"]),["deductions"]=Convert.ToDecimal(reader["deductions"]),
                ["net"]=Convert.ToDecimal(reader["net"]),["orders"]=Convert.ToInt32(reader["orders"])
            });
        return lines;
    }

    /// <summary>Distributable pool: line net + every platform-deducted item (TGA and ordinary). TGA never reduces the pool; rider-borne non-TGA amounts are subtracted from that rider after the split.</summary>
    static decimal EntitlementBeforeTga(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        object? net=SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(net),0) FROM {SchemaSql.Q(c,"sal","platform_invoice_line")} WHERE invoice_id=@id",("@id",invoiceId));
        object? deducted=SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(i.amount),0) FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            WHERE l.invoice_id=@id AND i.platform_deducted=1
            """,("@id",invoiceId));
        return J.Money(Convert.ToDecimal(net??0m)+Convert.ToDecimal(deducted??0m));
    }

    static decimal LineEntitlement(DbConnection c,DbTransaction? tx,JsonObject line)
    {
        // Aggregated account lines carry summed net and may span several source line ids.
        string idsCsv=line.S("lineIds",line.S("id"));
        var ids=idsCsv.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        decimal addBack=0;
        foreach(string id in ids)
        {
            object? deducted=SchemaSql.Scalar(c,tx,$"""
                SELECT COALESCE(SUM(amount),0) FROM {SchemaSql.Q(c,"sal","platform_invoice_item")}
                WHERE line_id=@id AND platform_deducted=1
                """,("@id",id));
            addBack+=Convert.ToDecimal(deducted??0m);
        }
        return J.Money(line.N("net")+addBack);
    }

    static Dictionary<string,decimal> RiderNonTgaBearers(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        var map=new Dictionary<string,decimal>(StringComparer.Ordinal);
        using var cmd=Store.Command(c,tx,$"""
            SELECT b.erp_rider_id, SUM(b.amount) AS amt
            FROM {SchemaSql.Q(c,"sal","item_bearer")} b
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_item")} i ON i.id=b.item_id
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            WHERE l.invoice_id=@id AND b.bearer='rider' AND LOWER(i.item_code)<>'tga'
            GROUP BY b.erp_rider_id
            """,("@id",invoiceId));
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            string rider=reader["erp_rider_id"] is DBNull?"":reader["erp_rider_id"].ToString()!;
            if(rider=="")continue;
            map[rider]=J.Money(Convert.ToDecimal(reader["amt"]));
        }
        return map;
    }

    static List<JsonObject> AggregateLinesByAccount(List<JsonObject> lines)
    {
        return lines.GroupBy(l=>l.S("erpPlatformAccountId"),StringComparer.Ordinal)
            .Select(g=>
            {
                var first=g.First().DeepClone()!.AsObject();
                first["gross"]=g.Sum(x=>x.N("gross"));
                first["deductions"]=g.Sum(x=>x.N("deductions"));
                first["net"]=g.Sum(x=>x.N("net"));
                first["orders"]=g.Sum(x=>(int)x.N("orders"));
                first["lineIds"]=string.Join(",",g.Select(x=>x.S("id")));
                return first;
            }).ToList();
    }

    static void DemandCaptureFrozen(DbConnection c,DbTransaction? tx,string payRunId,JsonObject invoice)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT company_id,app_code,period_month,capture_state FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",payRunId));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"دورة الرواتب غير موجودة.",404);
        J.Need(Convert.ToInt32(reader["capture_state"])>0,"التقاط المدخلات غير مجمّد.",409);
        J.Need(string.Equals(reader["company_id"].ToString(),invoice.S("companyId"),StringComparison.Ordinal),"دورة التقاط لا تطابق شركة الفاتورة.",409);
        J.Need(string.Equals(reader["app_code"].ToString(),invoice.S("app"),StringComparison.Ordinal),"دورة التقاط لا تطابق تطبيق الفاتورة.",409);
        string runMonth=ReadMonth(reader["period_month"]);
        J.Need(string.Equals(runMonth,invoice.S("periodMonth"),StringComparison.Ordinal),"دورة التقاط لا تطابق شهر الفاتورة.",409);
    }

    static void DemandNoPendingBearers(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        object? pending=SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(1) FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            WHERE l.invoice_id=@id AND i.bearer_status IN (@pending,@needs)
            """,("@id",invoiceId),("@pending",BearerPending),("@needs",BearerNeedsDecision));
        J.Need(Convert.ToInt32(pending??0)==0,"بنود معلّقة أو بلا تعيين متحمّل تمنع التوزيع.",409);
    }

    static void DemandRiderInFrozenAssignment(DbConnection c,DbTransaction? tx,JsonObject item,string payRunId,string riderId,string itemDate)
    {
        J.Need(riderId!="","مندوب المتحمّل مطلوب.",400);
        string runId=payRunId;
        if(runId=="")
            runId=FindFrozenPayRun(c,tx,item.S("companyId"),item.S("app"),item.S("periodMonth"));
        if(runId=="")return; // no freeze yet — dated forge check waits for a frozen run when provided
        // Codex RWC4: match the item's platform account and the supplied run's company/app/month — not any rider/date in the run.
        DemandRunMatchesItem(c,tx,runId,item);
        string account=item.S("erpPlatformAccountId");
        var assignments=LoadFrozenKind(c,tx,runId,"assignment");
        J.Need(assignments.Count>0,"لا تعيينات مجمّدة للتحقق من المندوب.",409);
        bool ok=assignments.Any(a=>
        {
            string assigned=a.S("actualRiderProfileId",a.S("riderProfileId"));
            if(!string.Equals(assigned,riderId,StringComparison.Ordinal))return false;
            if(account!=""&&!string.Equals(a.S("platformRiderAccountId"),account,StringComparison.Ordinal))return false;
            if(itemDate=="")return true;
            return OverlapsDate(a.S("effectiveFrom"),a.S("effectiveTo"),itemDate);
        });
        J.Need(ok,"المندوب ليس ضمن التعيينات المجمّدة للحساب/التاريخ.",409);
    }

    static void DemandRunMatchesItem(DbConnection c,DbTransaction? tx,string payRunId,JsonObject item)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT company_id,app_code,period_month FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",payRunId));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"دورة الرواتب غير موجودة.",404);
        J.Need(string.Equals(reader["company_id"].ToString(),item.S("companyId"),StringComparison.Ordinal),"دورة التقاط لا تطابق شركة البند.",409);
        J.Need(string.Equals(reader["app_code"].ToString(),item.S("app"),StringComparison.Ordinal),"دورة التقاط لا تطابق تطبيق البند.",409);
        string runMonth=ReadMonth(reader["period_month"]);
        J.Need(string.Equals(runMonth,item.S("periodMonth"),StringComparison.Ordinal),"دورة التقاط لا تطابق شهر البند.",409);
    }

    static string FindFrozenPayRun(DbConnection c,DbTransaction? tx,string companyId,string app,string periodMonth)
    {
        if(companyId==""||app==""||periodMonth=="")return "";
        using var cmd=Store.Command(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"sal","pay_run")}
            WHERE company_id=@c AND app_code=@app AND period_month=@pm AND capture_state>0
            ORDER BY created_at DESC
            """,("@c",companyId),("@app",app),("@pm",periodMonth.Length>=10?periodMonth[..10]:periodMonth));
        return cmd.ExecuteScalar()?.ToString()??"";
    }

    static List<JsonObject> LoadFrozenKind(DbConnection c,DbTransaction? tx,string payRunId,string kind)
    {
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT s.payload FROM {SchemaSql.Q(c,"fin","operation_snapshot_link")} l
            INNER JOIN {SchemaSql.Q(c,"sal","ops_snapshot")} s ON s.id=l.snapshot_id
            WHERE l.operation_type='pay_run' AND l.operation_id=@run AND l.snapshot_kind=@kind
            """,("@run",payRunId),("@kind",kind));
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            var payload=JsonNode.Parse(reader.GetString(0)) as JsonObject??new JsonObject();
            rows.Add(payload);
        }
        return rows;
    }

    sealed record Worker(string RiderId,int Orders,int Days,string From,string To);

    static List<Worker> WorkersForAccount(string account,List<JsonObject> assignments,List<JsonObject> daily)
    {
        var accountAssignments=assignments
            .Where(a=>string.Equals(a.S("platformRiderAccountId"),account,StringComparison.Ordinal))
            .ToList();
        var riders=accountAssignments
            .Select(a=>a.S("actualRiderProfileId",a.S("riderProfileId")))
            .Where(id=>id!="")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var list=new List<Worker>();
        foreach(string rider in riders)
        {
            var riderAssignments=accountAssignments.Where(a=>string.Equals(a.S("actualRiderProfileId",a.S("riderProfileId")),rider,StringComparison.Ordinal)).ToList();
            // Only orders inside a dated assignment window for this rider on this account.
            var days=daily.Where(d=>string.Equals(d.S("platformRiderAccountId"),account,StringComparison.Ordinal)
                &&string.Equals(d.S("riderProfileId",d.S("actualRiderProfileId")),rider,StringComparison.Ordinal)
                &&riderAssignments.Any(a=>OverlapsDate(a.S("effectiveFrom"),a.S("effectiveTo"),d.S("reportDate")))).ToList();
            int orders=days.Sum(d=>(int)d.N("orders"));
            if(orders<=0)continue;
            var from=riderAssignments.Select(a=>a.S("effectiveFrom")).Where(d=>d!="").DefaultIfEmpty("").Min()!;
            var to=riderAssignments.Select(a=>a.S("effectiveTo")).Where(d=>d!="").DefaultIfEmpty("").Max()!;
            list.Add(new(rider,orders,days.Count,from,to));
        }
        // Never invent workers from daily alone — forged / unassigned riders are rejected.
        return list;
    }

    static void RejectConflictingWorkers(JsonObject input,List<JsonObject> assignments,List<JsonObject> daily)
    {
        var supplied=(input["workers"] as JsonArray??[]).OfType<JsonObject>().ToList();
        if(supplied.Count==0)return;
        var frozenRiders=assignments.Select(a=>a.S("actualRiderProfileId",a.S("riderProfileId")))
            .Concat(daily.Select(d=>d.S("riderProfileId",d.S("actualRiderProfileId"))))
            .Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        foreach(var w in supplied)
        {
            string rider=w.S("erpRiderId");
            if(rider!=""&&frozenRiders.Count>0&&!frozenRiders.Contains(rider))
                throw new ApiError("المندوب المُرسل لا يطابق التعيينات المجمّدة.",409);
        }
    }

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>s.Length>=10?s[..10]:s,
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };

    static object NullDate(string value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value.Length>=10?value[..10]:value;

    static bool OverlapsDate(string from,string to,string day)
    {
        if(from=="")return false;
        string start=from.Length>=10?from[..10]:from;
        string end=string.IsNullOrWhiteSpace(to)?day:(to.Length>=10?to[..10]:to);
        string d=day.Length>=10?day[..10]:day;
        return string.CompareOrdinal(start,d)<=0&&string.CompareOrdinal(d,end)<=0;
    }
}
