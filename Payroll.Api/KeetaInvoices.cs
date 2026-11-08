using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.IO.Compression;
namespace Payroll;

public static class KeetaInvoices
{
    const string PreviewKind="keeta-invoice";
    public static readonly PlatformInvoiceWorkflow Workflow=new("keeta");
    public static readonly (string Header,string Category,bool Signed)[] DeductionColumns=[
        ("الخصم","penalty",false),("خصومات أخرى","other_deduction",false),("غرامة مُخالفة","penalty_fine",false),
        ("تعويض عن تلف الطعام","food_damage",false),("تعويض طعام","food_compensation",false),("رسوم خدمة التسجيل","registration_fee",false),
        ("خصم TGA (بدون ضريبة القيمة المضافة).","tga",false),("خصم رسوم الخدمة","service_fee",false),
        ("خصم مكافأة التوصيل على الموعد","timeliness_deduction",false),("خصم إعانة التاجر","merchant_subsidy_deduction",false),
        ("تعديل آخر","other_adjustment",true),("تعديلات أخرى","other_adjustments",true),("خصومات المعدات","equipment_deduction",false)
    ];
    static string Header(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Trim(),@"\s+"," ");
    static string Hash(string value)=>StorageTransfer.CutoverHash(JsonValue.Create(value));
    static void Scope(ClaimsPrincipal actor,string company)
    {AccessPolicy.Demand(actor,"pay.manage");FinancialScopes.DemandSponsor(actor,company);J.Need(FinancialScopes.Platform(actor,"Keeta"),"المنصة خارج النطاق.");}
    static string AccountKey(string raw)
    {
        string text=J.Digits(raw.Trim());
        if(decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n==decimal.Truncate(n))return n.ToString("0",CultureInfo.InvariantCulture);
        J.Need(text.Length is >0 and <=128&&!text.Any(char.IsControl),"معرف اليوزر غير صالح.");return text;
    }
    internal static HashSet<int> MissingCache(byte[] bytes,string name,HashSet<int> selected)
    {
        using var zip=new ZipArchive(new MemoryStream(bytes));XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XElement Xml(string path){using var stream=(zip.GetEntry(path)??throw new ApiError("مصنف غير صالح.")).Open();return XElement.Load(stream);}
        var sheet=Xml("xl/workbook.xml").Descendants(ns+"sheet").Single(s=>(string?)s.Attribute("name")==name);
        string id=(string)sheet.Attribute(rel+"id")!;string target=(string)Xml("xl/_rels/workbook.xml.rels").Elements().Single(x=>(string?)x.Attribute("Id")==id).Attribute("Target")!;
        int Column(XElement cell){int column=0;foreach(char ch in ((string?)cell.Attribute("r")??"A1").TakeWhile(char.IsAsciiLetter))column=column*26+char.ToUpperInvariant(ch)-'A'+1;return column-1;}
        return Xml(target.StartsWith('/')?target[1..]:"xl/"+target).Descendants(ns+"row").Where(r=>r.Elements(ns+"c").Any(c=>selected.Contains(Column(c))&&c.Element(ns+"f")!=null&&string.IsNullOrEmpty(c.Element(ns+"v")?.Value))).Select(r=>int.Parse((string)r.Attribute("r")!,CultureInfo.InvariantCulture)).ToHashSet();
    }
    public static JsonObject Parse(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        string company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,period=J.Period(input.Required("period")),reference=input.Required("reference");Scope(actor,company);
        J.Need(bytes.Length is >0 and <=8*1024*1024&&reference.Length<=200,"ملف حتى8ميجابايت ومرجع حتى200حرف.");
        string sha=Convert.ToHexString(SHA256.HashData(bytes)),id="keeta-invoice:"+Hash(company+":"+period+":"+reference);
        var allInvoices=Store.List(c,"platform_invoice",tx);id=InvoiceDiscard.ReplacementId(allInvoices,id,company,period,reference,"Keeta");
        J.Need(!allInvoices.Where(InvoiceDiscard.Active).Any(i=>i.S("sourceSha256")==sha||i.S("sourceHash")==sha||i.S("id")==id),"الفاتورة أو بصمة الملف موجودة؛ لا تعيد تسجيلها.");
        var candidates=Imports.Read(bytes,"invoice.xlsx").Where(s=>s.Rows.Count>0&&s.Rows[0].Select(Header).Contains("المعرف")&&s.Rows[0].Select(Header).Contains("الطلبات المُسلمة")&&s.Rows[0].Select(Header).Contains("إجمالي المبلغ المستحق")).ToList();
        if(input.S("sheetName")!="")candidates=candidates.Where(s=>s.Name==input.S("sheetName")).ToList();
        J.Need(candidates.Count==1,"حدد ورقة الفاتورة الأصلية؛ لا تجمع التفصيلي والملخص.");var sheet=candidates.Single();
        var header=sheet.Rows[0].Select(Header).ToList();int due=header.IndexOf("إجمالي المبلغ المستحق");var prefix=header.Take(due+1).ToList();
        int Col(string name,bool required=false){var found=prefix.Select((v,i)=>(v,i)).Where(x=>x.v==name).ToArray();J.Need(found.Length<=1&&(!required||found.Length==1),"عمود فاتورة مفقود أو مكرر: "+name);return found.Length==0?-1:found[0].i;}
        int accountCol=Col("المعرف",true),ordersCol=Col("الطلبات المُسلمة",true),netCol=Col("الإجمالي");
        if(netCol<0){var possible=prefix.Select((v,i)=>(v,i)).Where(x=>x.v=="الاجمالي").ToArray();J.Need(possible.Length==1,"يلزم حقل صريح للإجمالي قبل الضريبة؛ لا نفترض الضريبة من ملف الرواتب.");netCol=possible[0].i;}
        var selected=new HashSet<int>{accountCol,ordersCol,netCol,due};foreach(var d in DeductionColumns){int col=Col(d.Header);if(col>=0)selected.Add(col);}var cache=MissingCache(bytes,sheet.Name,selected);
        var accounts=Store.List(c,"platform_account",tx);var lines=new JsonArray();var seen=new HashSet<string>();
        var companyRows=(input["companyRows"] as JsonArray??[]).Select(x=>x!.GetValue<int>()).ToHashSet();var usedCompanyRows=new HashSet<int>();
        for(int i=1;i<sheet.Rows.Count;i++)
        {
            var row=sheet.Rows[i];string V(int index)=>index>=0&&index<row.Count?row[index].Trim():"";
            if(row.All(string.IsNullOrWhiteSpace))continue;string key=V(accountCol);
            if(key is "الاجمالي" or "الإجمالي" or "المجموع" or "Grand Total")continue;
            bool companyRow=companyRows.Contains(i+1);if(key==""&&!companyRow){J.Need(V(netCol)==""&&V(due)=="","حدد سطر تعديل الشركة أو الإجمالي صراحة؛ صف "+(i+1));continue;}
            J.Need(!cache.Contains(i+1),"formula_without_cached_value: row "+(i+1));
            decimal N(int col)
            {string value=V(col);if(value=="")return 0;J.Need(decimal.TryParse(J.Digits(value).Replace("٬","").Replace("٫","."),NumberStyles.Number|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out var n)&&Math.Abs(n)<100000000&&Math.Abs(n-J.Money(n))<=0.000001m,"قيمة نقدية غير صالحة؛ صف "+(i+1));return J.Money(n);}
            decimal net=N(netCol),inclusive=N(due),vat=J.Money(inclusive-net),orders=N(ordersCol);string account="";
            J.Need(orders>=0&&orders==decimal.Truncate(orders),"طلبات صحيحة غير سالبة مطلوبة؛ صف "+(i+1));
            if(companyRow){J.Need(key==""&&net<0&&orders==0&&vat<=0,"تعديل الشركة السالب بدون يوزر؛ التعديل الموجب يحتاج مراجعة مستقلة.");usedCompanyRows.Add(i+1);}
            else
            {
                key=AccountKey(key);J.Need(seen.Add(key),"يوزر مكرر؛ لا تجمع التفصيلي والملخص.");
                var matches=accounts.Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="keeta"&&(a.S("externalAccountId")==key||a.S("code")==key||a.S("id")==key)).ToArray();
                J.Need(matches.Length==1,"اليوزر غير مربوط بشكل فريد بالشركة والمنصة؛ صف "+(i+1));account=matches[0].S("id");
                J.Need(net>=0&&vat>=0,"الإشعار الدائن يحتاج مسار عكس؛ صف "+(i+1));
            }
            var deductions=new JsonArray();
            foreach(var field in DeductionColumns)
            {
                int col=Col(field.Header);if(col<0)continue;decimal value=N(col),amount=field.Signed?Math.Max(0,-value):Math.Abs(value);if(amount==0)continue;
                deductions.Add(new JsonObject{["category"]=field.Category,["amount"]=amount,["level"]="unclassified",["sourceHeader"]=field.Header,["sourceSignedAmount"]=value});
            }
            if(companyRow)
            {J.Need(deductions.Count==0,"تعديل الشركة الإجمالي لا يجمع مع مكوناته.");deductions.Add(new JsonObject{["category"]="supplier_adjustment",["amount"]=-net,["level"]="unclassified"});}
            decimal deduction=deductions.OfType<JsonObject>().Sum(d=>d.N("amount")),gross=J.Money(net+deduction);
            var sourceMetrics=new JsonObject();foreach(string name in new[]{"صالح","المركبة","أيام الاتصال-صالحة","ساعات الاتصال اليومي-صالحة","ساعات الاتصال اليومي خلال وقت الذروة-صالحة"}){int col=Col(name);if(col>=0)sourceMetrics[name]=V(col);}
            lines.Add(new JsonObject{["id"]="keeta-line:"+Hash(id+":"+(i+1)),["invoiceId"]=id,["companyId"]=company,["period"]=period,["platform"]="Keeta",["sourceRow"]=i+1,["lineType"]=companyRow?"company":"account",["platformAccountId"]=account,
                ["baseGross"]=gross,["incentives"]=0,["deductionAmount"]=deduction,["deductions"]=deductions,["orders"]=orders,["vat"]=vat,["invoiced"]=net,["invoiceInclusive"]=inclusive,["sourceMetrics"]=sourceMetrics,["status"]="invoiced",["reason"]="final supplier invoice; no expected amount substituted"});
        }
        J.Need(usedCompanyRows.SetEquals(companyRows),"صف تعديل شركة غير موجود أو مختلط بسطر إجمالي.");
        J.Need(lines.Count>0&&lines.OfType<JsonObject>().Any(l=>l.S("lineType")=="account")&&lines.OfType<JsonObject>().Sum(l=>l.N("vat"))>=0,"الفاتورة بلا سطور يوزرات أو ضريبتها سالبة.");
        string role=input.S("accountingRole","cycle_only");J.Need(role is "principal" or "cycle_only","حدد principal أو cycle_only.");
        return new(){["id"]=id,["platform"]="Keeta",["companyId"]=company,["period"]=period,["reference"]=reference,["sourceSha256"]=sha,["sourceSheet"]=sheet.Name,["accountingRole"]=role,["amountExclVat"]=lines.OfType<JsonObject>().Sum(l=>l.N("baseGross")),["deductionsTotal"]=lines.OfType<JsonObject>().Sum(l=>l.N("deductionAmount")),["vat"]=lines.OfType<JsonObject>().Sum(l=>l.N("vat")),["status"]="invoiced",["odooVoucherNo"]=input.S("odooVoucherNo"),["lines"]=lines};
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {var rows=new JsonArray(Parse(c,null,actor,input,bytes));return new(){["rows"]=rows,["fingerprint"]=FinancialImports.Issue(c,PreviewKind,rows,actor.Identity!.Name!)};}
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        FinancialImports.Verify(c,tx,input,PreviewKind,actor.Identity!.Name!);var parsed=Parse(c,tx,actor,input,bytes);
        J.Need(input["rows"] is JsonArray{Count:1}&&FinancialImports.Canonical(input["rows"]![0])==FinancialImports.Canonical(parsed),"الملف أو خيارات الورقة تغيرت؛ أعد المعاينة.");
        var lines=parsed["lines"]!.DeepClone().AsArray();parsed.Remove("lines");parsed["createdBy"]=actor.Identity!.Name;parsed["createdAt"]=DateTimeOffset.UtcNow.ToString("O");parsed["approvalStatus"]="pending";
        Store.Save(c,"platform_invoice",parsed,tx);foreach(var line in lines.OfType<JsonObject>())Store.Save(c,"platform_invoice_line",line,tx);Store.Audit(c,tx,"platform.keeta.invoice_imported",parsed.S("id"),actor.Identity!.Name!);return parsed;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/platforms/keeta/invoices",async(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"pay.manage");J.Need(ctx.Request.HasFormContentType,"ارفع ملفxlsx كـmultipart.");var form=await ctx.Request.ReadFormAsync();J.Need(form.Files.Count==1,"ملف واحد لكل فاتورة.");var file=form.Files[0];J.Need(file.Length is >0 and <=8*1024*1024&&Path.GetExtension(file.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase),"ملفxlsx حتى8ميجابايت.");
            using var buffer=new MemoryStream();await file.CopyToAsync(buffer,ctx.RequestAborted);byte[] bytes=buffer.ToArray();var input=new JsonObject{["companyId"]=form["companyId"].ToString(),["period"]=form["period"].ToString(),["reference"]=form["reference"].ToString(),["sheetName"]=form["sheetName"].ToString(),["accountingRole"]=form["accountingRole"].ToString()==""?"cycle_only":form["accountingRole"].ToString(),["odooVoucherNo"]=form["odooVoucherNo"].ToString(),["companyRows"]=form["companyRows"].ToString()==""?new JsonArray():JsonNode.Parse(form["companyRows"].ToString())};
            if(ctx.Request.Query["commit"]!="1")return store.Read(c=>Preview(c,ctx.User,input,bytes));
            input["confirm"]=form["confirm"]=="true";input["fingerprint"]=form["fingerprint"].ToString();input["rows"]=JsonNode.Parse(form["rows"].ToString());return store.Write((c,tx)=>Commit(c,tx,ctx.User,input,bytes));
        }).DisableAntiforgery().WithTags("Platforms").WithSummary("Preview or confirm a signed Keeta supplier XLSX invoice without recalculating final entitlements");
        api.MapPost("/platforms/keeta/invoices/{id}/discard",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Workflow.View(c,tx,ctx.User,InvoiceDiscard.Discard(c,tx,ctx.User,"keeta",id,input)))).WithTags("Platforms").WithSummary("Discard a pending unused Keeta invoice, retaining source rows and audit");
        api.MapGet("/platforms/keeta/invoices/{id}",(string id,HttpContext ctx)=>store.Read(c=>Workflow.View(c,null,ctx.User,Workflow.Get(c,null,ctx.User,id)))).WithTags("Platforms").WithSummary("Read scoped Keeta invoice and available actions");
        api.MapGet("/platforms/keeta/invoices",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,"pay.view");return store.Read(c=>ApiPagination.Apply(Store.List(c,"platform_invoice").Where(i=>InvoiceDiscard.Active(i)&&i.S("platform")=="Keeta"&&FinancialScopes.Sponsor(ctx.User,i.S("companyId"))&&FinancialScopes.Platform(ctx.User,"Keeta" )).Select(i=>Workflow.View(c,null,ctx.User,i)),ctx.Request.Query,true));}).WithTags("Platforms").WithSummary("List scoped Keeta invoices");
        api.MapGet("/platforms/keeta/invoices/{id}/lines",(string id,HttpContext ctx)=>store.Read(c=>{Workflow.Get(c,null,ctx.User,id);return Workflow.Lines(c,null,id);})).WithTags("Platforms").WithSummary("Read scoped Keeta invoice lines and source classifications");
        api.MapPost("/platforms/keeta/invoices/{id}/classify",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Workflow.Classify(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Classify Keeta deductions before independent approval");
        api.MapPost("/platforms/keeta/invoices/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Workflow.Approve(c,tx,ctx.User,id))).WithTags("Platforms").WithSummary("Independently approve final Keeta invoice facts");
        api.MapPost("/platforms/keeta/invoices/{id}/dispute",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Workflow.Dispute(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Open an explicit Keeta invoice dispute");
        api.MapPost("/platforms/keeta/invoices/{id}/resolve",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Workflow.Dispute(c,tx,ctx.User,id,input,true))).WithTags("Platforms").WithSummary("Independently resolve a Keeta invoice dispute");
        api.MapPost("/platforms/keeta/invoices/{id}/post",(string id,HttpContext ctx)=>store.Write((c,tx)=>Workflow.Post(c,tx,ctx.User,id))).WithTags("Platforms").WithSummary("Post a principal Keeta invoice with dispute, period and duplicate-cycle guards");
    }
}
