using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-13: distribution + bearer. TGA allocate; settlements/combine; Keeta rejection after free quota;
/// bearer = actual rider by dates (M-0700); Ninja order-share only (Ghazal clarification).</summary>
public static class SalDistribution
{
    public const string ManagePermission="pay.manage",ApprovePermission="pay.approve";
    public const decimal KeetaRejectionSar=50m;
    public const int KeetaFreeRejectionsPerDay=2;

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
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","platform_invoice_item")} SET bearer_status=2 WHERE id=@id",("@id",itemId));
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
        var invoice=LoadInvoice(c,tx,actor,invoiceId);
        MoneyWriteGuards.Demand((byte)invoice.N("runMode"),invoice.B("payoutBlocked"),MoneyEffectKind.Accounting);
        J.Need(string.Equals(invoice.S("app"),app,StringComparison.Ordinal),"التطبيق لا يطابق الفاتورة.",409);
        var workers=(input["workers"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(workers.Count>0,"أدخل المناديب العاملين على الحساب.",400);
        bool ninja=app=="ninja";
        if(!ninja)
            throw new ApiError("توزيع الحصة بنسبة الطلبات لنينجا فقط؛ لباقي التطبيقات عيّن متحمّلاً صريحاً لكل بند.",409);
        decimal due=LineNetTotal(c,tx,invoiceId);
        int orders=workers.Sum(w=>(int)w.N("orders"));
        J.Need(orders>0,"إجمالي الطلبات يجب أن يكون أكبر من صفر.",400);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"sal","distribution")} WHERE invoice_id=@id",("@id",invoiceId));
        var rows=new JsonArray();
        decimal kept=0;
        for(int i=0;i<workers.Count;i++)
        {
            var w=workers[i];
            string rider=w.Required("erpRiderId");
            int o=(int)w.N("orders");
            decimal share=i==workers.Count-1?J.Money(due-kept):J.Money(due*((decimal)o/orders));
            kept+=share;
            string id=SchemaSql.GuidText(SchemaSql.NewId());
            string account=w.S("erpPlatformAccountId",invoice.S("erpCrId"));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","distribution")}
                (id,period_month,invoice_id,erp_platform_account_id,erp_rider_id,orders,days,from_date,to_date,share_amount,method,status,created_by,created_at)
                VALUES(@id,@pm,@inv,@acc,@rider,@orders,@days,@from,@to,@share,'order_share','ready',@by,@at)
                """,("@id",id),("@pm",invoice.S("periodMonth")),("@inv",invoiceId),("@acc",account),("@rider",rider),
                ("@orders",o),("@days",(int)w.N("days")),("@from",NullDate(w.S("fromDate"))),("@to",NullDate(w.S("toDate"))),
                ("@share",share),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
            rows.Add(new JsonObject{["id"]=id,["erpRiderId"]=rider,["orders"]=o,["shareAmount"]=share,["fromDate"]=w.S("fromDate"),["toDate"]=w.S("toDate"),["status"]="ready"});
        }
        Store.Audit(c,tx,"sal.distribution_proposed",invoiceId,actor.Identity!.Name!);
        return new()
        {
            ["invoiceId"]=invoiceId,["app"]=app,["method"]="order_share",["distributedTotal"]=due,["rows"]=rows,
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
        string itemDate=input.S("itemDate").Trim();
        var allocations=(input["allocations"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(allocations.Count>0,"أدخل توزيع المتحمّلين.",400);
        if(itemDate=="")
        {
            // Undated shared user/ID → supervisor assignment queue (needs_decision).
            Store.Audit(c,tx,"sal.shared_bearer_needs_decision",itemId,actor.Identity!.Name!);
            return new()
            {
                ["itemId"]=itemId,["status"]="needs_decision",
                ["noteAr"]="بند بلا تاريخ — يحتاج تعيين المشرف للمتحمّل.",
                ["allowedActions"]=new JsonArray("assign_shared_bearer")
            };
        }
        string actual=input.S("actualRiderId");
        J.Need(actual!="","المندوب الفعلي بتاريخ البند مطلوب.",400);
        // Dated → force actual rider (M-0700), not account owner.
        foreach(var row in allocations.Where(a=>a.S("bearer")=="rider"))
            row["erpRiderId"]=actual;
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
            .WithTags("Payroll").WithSummary("Propose Ninja order-share distribution to actual riders");
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
        return inv;
    }

    static JsonObject LoadItem(DbConnection c,DbTransaction? tx,string itemId,string invoiceId)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT i.id,i.item_code,i.amount,i.period_month
            FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice_line")} l ON l.id=i.line_id
            WHERE i.id=@id AND l.invoice_id=@inv
            """,("@id",itemId),("@inv",invoiceId));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"بند الفاتورة غير موجود.",404);
        return new(){["id"]=reader["id"].ToString(),["itemCode"]=reader["item_code"].ToString(),["amount"]=Convert.ToDecimal(reader["amount"]),["periodMonth"]=ReadMonth(reader["period_month"])};
    }

    static decimal LineNetTotal(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        object? v=SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(net),0) FROM {SchemaSql.Q(c,"sal","platform_invoice_line")} WHERE invoice_id=@id",("@id",invoiceId));
        return J.Money(Convert.ToDecimal(v??0m));
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
