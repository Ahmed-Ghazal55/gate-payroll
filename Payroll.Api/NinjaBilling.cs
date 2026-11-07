using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Payroll;

// BR §26: what Ninja pays the company per captain. Formula shapes only; every rate and threshold comes from versioned terms.
// Reproduces the 43-column supplier invoice so an uploaded invoice can be checked captain by captain.
public sealed record NinjaCaptainFacts(string CaptainId,string Capability,int DaysFromFirstAttended,int Orders,int AttendedDays,int ShortDays,IReadOnlyDictionary<string,decimal> PassThrough);

public static class NinjaBilling
{
    // Platform-provided adjustments taken as-is from the invoice or the daily data (signed; deductions arrive negative).
    public static readonly (string Code,string Column)[] PassThrough=[
        ("deductionRefund","Deduction refund"),("gasolineSupport","Gasoline Support"),("weekend40","Weekend 40 Bonus"),("weekend42","Weekend 42 Bonus"),
        ("weekend43","Weekend 43 Bonus"),("bonus100","100 SAR BONUS"),("supplierPerformance","Supplier Performance Bonus"),("naqilBlocked","NAQIL BLOCKED"),
        ("penalty","Penalty"),("onDemand","on demand"),("stopsWithoutNotice","STOPS WITHOUT NOTICE DEDUCTION")];

    public static JsonObject Seed()=>JsonNode.Parse(typeof(NinjaBilling).Assembly.GetManifestResourceStream("Payroll.NinjaTerms.v1.json")!)!.AsObject();

    public static void Validate(JsonObject p)
    {
        PayValues.Fields(p,"monthDays","capabilities","attendance","shortDays","vatPercent","companyCostAccount");
        J.Need(p.S("monthDays") is "calendar" or "standard","أيام الشهر: calendar أو standard.");
        var caps=PayValues.Object(p["capabilities"]);J.Need(caps.Count is >0 and <=6,"حدد نوع مركبة واحد على الأقل.");
        foreach(var pair in caps)
        {
            PayValues.Name(pair.Key);var c=PayValues.Object(pair.Value);PayValues.Fields(c,"contractAmount","contractTarget","extraOrderRate","perOrderRate","levels");
            PayValues.Number(c,"contractAmount",1);PayValues.Number(c,"contractTarget",1);PayValues.Number(c,"extraOrderRate");PayValues.Number(c,"perOrderRate");
            var levels=PayValues.Array(c["levels"],10);J.Need(levels.Count>0,"مستويات الخصم تحت الهدف مطلوبة.");decimal last=0;
            foreach(var l in levels.OfType<JsonObject>()){PayValues.Fields(l,"maxBelow","missingOrderRate");decimal max=PayValues.Number(l,"maxBelow",0.0001m,1);J.Need(max>last,"المستويات تصاعدية بلا تداخل.");last=max;PayValues.Number(l,"missingOrderRate");}
        }
        var a=PayValues.Object(p["attendance"]);PayValues.Fields(a,"minRatio","deductionPercent");PayValues.Number(a,"minRatio",0,1);PayValues.Number(a,"deductionPercent",0,100);
        var s=PayValues.Object(p["shortDays"]);PayValues.Fields(s,"deductionPercentOfDailyAmount");PayValues.Number(s,"deductionPercentOfDailyAmount",0,100);
        PayValues.Number(p,"vatPercent",0,100);
    }

    public static int MonthDays(string period,JsonObject p,JsonObject? globalValues=null)
    {
        J.Period(period);
        if(p.S("monthDays")=="standard")return PayEvaluator.DaysInPayrollMonth(period,globalValues??throw new ApiError("أيام الشهر القياسية تحتاج الإعداد العام."));
        var first=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);return first.AddMonths(1).AddDays(-1).Day;
    }

    public static string Capability(string raw)
    {
        string v=raw.Trim().ToLowerInvariant();
        return v.EndsWith("car",StringComparison.Ordinal)?"car":v.EndsWith("bike",StringComparison.Ordinal)?"bike":v;
    }

    // Full precision inside, like the supplier sheet; callers round to 2 digits for display and posting.
    public static JsonObject Evaluate(NinjaCaptainFacts f,JsonObject p,int monthDays)
    {
        var caps=PayValues.Object(p["capabilities"]);string cap=Capability(f.Capability);
        var c=caps[cap] as JsonObject??throw new ApiError("نوع المركبة غير معرف في عقد نينجا: "+f.Capability);
        J.Need(f.DaysFromFirstAttended is >=0&&f.DaysFromFirstAttended<=monthDays&&f.Orders>=0&&f.AttendedDays>=0&&f.ShortDays>=0,"وقائع الكابتن غير صالحة.");
        decimal amount=c.N("contractAmount"),target=c.N("contractTarget"),days=f.DaysFromFirstAttended,orders=f.Orders;
        decimal adjustedTarget=decimal.Floor(target*days/monthDays),adjustedAmount=amount*days/monthDays,perDay=amount/monthDays;
        decimal below=adjustedTarget>0&&orders<adjustedTarget?(adjustedTarget-orders)/adjustedTarget:0;
        string level;decimal targetDeduction=0,bonus=0,targetTotal;
        if(orders>=adjustedTarget){level="above_target";bonus=(orders-adjustedTarget)*c.N("extraOrderRate");targetTotal=adjustedAmount+bonus;}
        else
        {
            var match=c["levels"]!.AsArray().OfType<JsonObject>().FirstOrDefault(l=>below<=l.N("maxBelow"));
            if(match!=null){level="below_"+match.N("maxBelow").ToString("0.##",CultureInfo.InvariantCulture);targetDeduction=(adjustedTarget-orders)*match.N("missingOrderRate");targetTotal=adjustedAmount-targetDeduction;}
            else{level="per_order";targetTotal=orders*c.N("perOrderRate");targetDeduction=adjustedAmount-targetTotal;}
        }
        var att=p["attendance"]!.AsObject();decimal ratio=days>0?f.AttendedDays/days:0;
        decimal attendanceDeduction=ratio<att.N("minRatio")?targetTotal*att.N("deductionPercent")/100:0;
        decimal shortDeduction=perDay*p["shortDays"]!.AsObject().N("deductionPercentOfDailyAmount")/100*f.ShortDays;
        decimal pass=f.PassThrough.Values.Sum();
        decimal final=targetTotal-attendanceDeduction-shortDeduction+pass,vat=final*p.N("vatPercent")/100;
        var passOut=new JsonObject();foreach(var (code,_) in PassThrough)passOut[code]=f.PassThrough.GetValueOrDefault(code);
        return new(){["captainId"]=f.CaptainId,["capability"]=cap,["days"]=days,["orders"]=orders,["adjustedTarget"]=adjustedTarget,["adjustedAmount"]=adjustedAmount,["amountPerDay"]=perDay,
            ["belowTargetRatio"]=below,["level"]=level,["targetDeduction"]=targetDeduction,["targetBonus"]=bonus,["targetTotal"]=targetTotal,["attendanceRatio"]=ratio,
            ["attendanceDeduction"]=attendanceDeduction,["shortDayDeduction"]=shortDeduction,["passThrough"]=passOut,["finalAmount"]=final,["vat"]=vat,["totalCost"]=final+vat};
    }

    // ---- Invoice parsing (the supplier's 43-column sheet, as delivered) ----
    static readonly Dictionary<string,string> Columns=new(StringComparer.OrdinalIgnoreCase){
        ["supplier_id"]="supplierId",["supplier_name"]="supplierName",["CAPTAIN ACTIVE STATUS"]="status",["capabilty"]="capability",["captain_id"]="captainId",["Zone Name"]="zone",
        ["First Attended Date"]="firstAttended",["Total Delivered Orders"]="orders",["Contract Amount"]="contractAmount",["Contract Target"]="contractTarget",
        ["Count of First Attended Date to End of Month"]="days",["Adjusted Target"]="adjustedTarget",["Adjusted Amount"]="adjustedAmount",["Below Target Percentage"]="belowTargetRatio",
        ["Target Level Type"]="levelType",["Target Deductions Amount"]="targetDeduction",["Target Bonus Amount"]="targetBonus",["Target Total Amount"]="targetTotal",
        ["Count of Attended Days"]="attendedDays",["Attendance Deduction"]="attendanceDeduction",["Count of Days Less than 8 Hr"]="shortDays",["8 Hour Deductions"]="shortDayDeduction",
        ["Final Amount"]="finalAmount",["VAT"]="vat",["TOTAL COST"]="totalCost"};
    static readonly string[] Compared=["adjustedTarget","adjustedAmount","targetTotal","attendanceDeduction","shortDayDeduction","finalAmount","vat","totalCost"];

    static decimal Num(string v)=>decimal.TryParse(v.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;
    static int Int(string v)=>(int)decimal.Round(Num(v));

    public static JsonObject Reconcile(List<Imports.Sheet> sheets,string period,JsonObject p,string fileHash)
    {
        var sheet=sheets.FirstOrDefault(s=>s.Rows.Count>0&&s.Rows[0].Any(h=>h.Trim().Equals("captain_id",StringComparison.OrdinalIgnoreCase)))??throw new ApiError("لم يتم العثور على عمود captain_id في الفاتورة.");
        var header=sheet.Rows[0].Select(h=>h.Replace("\n"," ").Trim()).ToList();
        int Col(string name)=>header.FindIndex(h=>h.Equals(name,StringComparison.OrdinalIgnoreCase));
        var missing=Columns.Keys.Concat(PassThrough.Select(x=>x.Column)).Where(k=>Col(k)<0).ToList();J.Need(missing.Count==0,"أعمدة ناقصة في فاتورة نينجا: "+string.Join(", ",missing));
        string Cell(List<string> row,string name){int i=Col(name);return i<row.Count?row[i]:"";}
        int monthDays=MonthDays(period,p);var lines=new JsonArray();var adjustments=new JsonArray();var totals=new Dictionary<string,(decimal Invoice,decimal Engine)>();
        int matched=0,different=0;string supplier="";
        for(int r=1;r<sheet.Rows.Count;r++)
        {
            var row=sheet.Rows[r];string captain=Cell(row,"captain_id").Trim();if(captain=="")continue;supplier=supplier==""?Cell(row,"supplier_id").Trim():supplier;
            decimal invoiceFinal=Num(Cell(row,"Final Amount"));
            if(Num(Cell(row,"Contract Amount"))==0)
            {   // Invoice-level adjustment (e.g. "DEDUCTIONS") with no captain contract: kept as a line, never recalculated.
                adjustments.Add(new JsonObject{["row"]=r+1,["captainId"]=captain,["levelType"]=Cell(row,"Target Level Type").Trim(),["finalAmount"]=Math.Round(invoiceFinal,2),["totalCost"]=Math.Round(Num(Cell(row,"TOTAL COST")),2)});
                Add(totals,"finalAmount",invoiceFinal,invoiceFinal);Add(totals,"totalCost",Num(Cell(row,"TOTAL COST")),Num(Cell(row,"TOTAL COST")));continue;
            }
            var pass=PassThrough.ToDictionary(x=>x.Code,x=>Num(Cell(row,x.Column)));
            var facts=new NinjaCaptainFacts(captain,Cell(row,"capabilty"),Int(Cell(row,"Count of First Attended Date to End of Month")),Int(Cell(row,"Total Delivered Orders")),Int(Cell(row,"Count of Attended Days")),Int(Cell(row,"Count of Days Less than 8 Hr")),pass);
            JsonObject engine;var diffs=new JsonArray();
            try{engine=Evaluate(facts,p,monthDays);}
            catch(ApiError e){different++;lines.Add(new JsonObject{["row"]=r+1,["captainId"]=captain,["status"]="error",["reason"]=e.Message});continue;}
            foreach(string key in Compared)
            {
                string column=Columns.First(x=>x.Value==key).Key;decimal inv=Num(Cell(row,column)),eng=engine.N(key);Add(totals,key,inv,eng);
                if(Math.Abs(inv-eng)>0.01m)diffs.Add(new JsonObject{["field"]=key,["invoice"]=Math.Round(inv,2),["engine"]=Math.Round(eng,2),["difference"]=Math.Round(inv-eng,2)});
            }
            var reasons=new JsonArray();
            if(diffs.Count>0)
            {
                if(!Cell(row,"capabilty").Trim().StartsWith("capability_",StringComparison.OrdinalIgnoreCase))reasons.Add("capability_label_edited:"+Cell(row,"capabilty").Trim());
                if(Num(Cell(row,"Contract Amount"))!=PayValues.Object(p["capabilities"])[engine.S("capability")]!.AsObject().N("contractAmount"))reasons.Add("contract_amount_differs_from_terms");
                if(reasons.Count==0)reasons.Add("unexplained");
            }
            if(diffs.Count==0)matched++;else different++;
            var invoiced=new JsonObject();foreach(var column in Columns)invoiced[column.Value]=column.Value is "supplierId" or "supplierName" or "status" or "capability" or "captainId" or "zone" or "firstAttended" or "levelType"?JsonValue.Create(Cell(row,column.Key)):JsonValue.Create(Num(Cell(row,column.Key)));
            var passInvoiced=new JsonObject();foreach(var item in PassThrough)passInvoiced[item.Code]=pass[item.Code];
            lines.Add(new JsonObject{["invoiceValues"]=invoiced,["invoicePassThrough"]=passInvoiced,["row"]=r+1,["captainId"]=captain,["capability"]=engine.S("capability"),["level"]=engine.S("level"),["status"]=diffs.Count==0?"match":"difference",
                ["engineFinal"]=Math.Round(engine.N("finalAmount"),2),["invoiceFinal"]=Math.Round(invoiceFinal,2),["differences"]=diffs,["reasons"]=reasons});
        }
        var sum=new JsonObject();foreach(var (k,v) in totals)sum[k]=new JsonObject{["invoice"]=Math.Round(v.Invoice,2),["engine"]=Math.Round(v.Engine,2),["difference"]=Math.Round(v.Invoice-v.Engine,2)};
        return new(){["supplierId"]=supplier,["period"]=period,["fileSha256"]=fileHash,["monthDays"]=monthDays,["captains"]=matched+different,["matched"]=matched,["different"]=different,
            ["adjustments"]=adjustments,["totals"]=sum,["lines"]=lines};
    }
    static void Add(Dictionary<string,(decimal,decimal)> t,string k,decimal inv,decimal eng){var cur=t.GetValueOrDefault(k);t[k]=(cur.Item1+inv,cur.Item2+eng);}

    // Terms: the approved Ninja ninja_target terms for the month; otherwise the seed, clearly marked unapproved.
    public static (JsonObject Parameters,string Source) Terms(System.Data.Common.DbConnection c,string period,System.Data.Common.DbTransaction? tx=null)
    {
        var approved=Store.List(c,"pay_terms",tx).Where(t=>t.S("app")=="ninja"&&t.S("period")==period&&t.S("status")=="approved"&&t["data"]?["billingModel"]?.ToString()=="ninja_target").OrderByDescending(t=>t.S("approvedAt")).FirstOrDefault();
        return approved!=null?(approved["data"]!["parameters"]!.AsObject(),"approved:"+approved.S("id")):(Seed()["parameters"]!.AsObject(),"seed_unapproved");
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/platforms/ninja/terms/seed",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,"pay.view");return Seed();}).WithTags("Platforms").WithSummary("Read the Ninja contract seed terms (draft source for a ninja_target pay_terms version)");
        api.MapPost("/platforms/ninja/invoices/reconcile",(JsonObject input,HttpContext ctx)=>store.Read(c=>
        {
            AccessPolicy.Demand(ctx.User,"pay.view");string period=J.Period(input.Required("period"));var files=input["files"] as JsonArray??throw new ApiError("ملفات الفواتير مطلوبة.");
            J.Need(files.Count is >0 and <=5,"ارفع من فاتورة واحدة إلى خمس.");var (terms,source)=Terms(c,period);var results=new JsonArray();
            foreach(var item in files.OfType<JsonObject>())
            {
                J.Need(Path.GetExtension(item.S("fileName")).Equals(".xlsx",StringComparison.OrdinalIgnoreCase),"فاتورة نينجا ملف xlsx.");
                byte[] bytes=FinancialImports.FileBytes(item);var r=Reconcile(Imports.Read(bytes,"invoice.xlsx"),period,terms,Convert.ToHexString(SHA256.HashData(bytes)));r["fileName"]=Path.GetFileName(item.S("fileName"));results.Add(r);
            }
            return new JsonObject{["period"]=period,["termsSource"]=source,["invoices"]=results};
        })).WithTags("Platforms").WithSummary("Recalculate uploaded Ninja supplier invoices captain by captain against the contract terms (read-only)");
    }
}
