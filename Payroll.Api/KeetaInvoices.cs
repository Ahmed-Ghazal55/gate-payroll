using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    static readonly string[] AccountAliases=["المعرف","معرف سائق التوصيل"];
    static readonly string[] OrdersAliases=["الطلبات المسلمة"];
    static readonly string[] DueAliases=["اجمالي المبلغ المستحق"];
    static readonly string[] NetAliases=["الاجمالي","مبلغ الفاتورة"];
    static readonly string[] PartnerTaxAliases=["مبلغ الضريبة"];
    static string Header(string value)
    {
        string text=value.Trim().Normalize(NormalizationForm.FormKD);
        text=Regex.Replace(text,@"\p{Mn}+","");
        text=text.Replace('أ','ا').Replace('إ','ا').Replace('آ','ا');
        return Regex.Replace(text,@"\s+"," ");
    }
    static string Hash(string value)=>StorageTransfer.CutoverHash(JsonValue.Create(value));
    static void Scope(ClaimsPrincipal actor,string company)
    {AccessPolicy.Demand(actor,"pay.manage");FinancialScopes.DemandSponsor(actor,company);J.Need(FinancialScopes.Platform(actor,"Keeta"),"المنصة خارج النطاق.");}
    static string AccountKey(string raw)
    {
        string text=J.Digits(raw.Trim());
        if(decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n==decimal.Truncate(n))return n.ToString("0",CultureInfo.InvariantCulture);
        J.Need(text.Length is >0 and <=128&&!text.Any(char.IsControl),"معرف اليوزر غير صالح.");return text;
    }
    static bool RowHasAliases(IReadOnlyList<string> headers,params string[][] groups)
        =>groups.All(g=>g.Any(alias=>headers.Contains(Header(alias))));
    static int AliasIndex(IReadOnlyList<string> headers,IEnumerable<string> aliases)
    {
        var found=headers.Select((v,i)=>(v,i)).Where(x=>aliases.Any(a=>x.v==Header(a))).ToArray();
        J.Need(found.Length<=1,"عمود فاتورة مكرر.");
        return found.Length==0?-1:found[0].i;
    }
    static int FindHeaderRow(Imports.Sheet sheet)
    {
        int limit=Math.Min(sheet.Rows.Count,40);
        for(int i=0;i<limit;i++)
        {
            var headers=sheet.Rows[i].Select(Header).ToList();
            if(RowHasAliases(headers,AccountAliases,OrdersAliases,DueAliases))return i;
        }
        return -1;
    }
    static int FindPartnerHeaderRow(Imports.Sheet sheet)
    {
        int limit=Math.Min(sheet.Rows.Count,40);
        for(int i=0;i<limit;i++)
        {
            var headers=sheet.Rows[i].Select(Header).ToList();
            // Partner summary: tax + due, without rider account/orders (avoids rider and order-detail sheets).
            if(RowHasAliases(headers,PartnerTaxAliases,DueAliases)&&!RowHasAliases(headers,OrdersAliases)&&AliasIndex(headers,AccountAliases)<0)return i;
        }
        return -1;
    }
    static (decimal Tax,decimal PartnerDue) ReadPartnerTotals(IReadOnlyList<Imports.Sheet> sheets)
    {
        var hits=sheets.Select(s=>(Sheet:s,HeaderRow:FindPartnerHeaderRow(s))).Where(x=>x.HeaderRow>=0).ToList();
        J.Need(hits.Count==1,"حدد ورقة تفاصيل الشركاء لضريبة الفاتورة.");
        var sheet=hits[0].Sheet;int headerRow=hits[0].HeaderRow;
        var header=sheet.Rows[headerRow].Select(Header).ToList();
        int taxCol=AliasIndex(header,PartnerTaxAliases),dueCol=AliasIndex(header,DueAliases);
        J.Need(taxCol>=0&&dueCol>=0,"عمود ضريبة أو إجمالي الشريك مفقود.");
        var dataRows=new List<(decimal Tax,decimal Due)>();
        for(int i=headerRow+1;i<sheet.Rows.Count;i++)
        {
            var row=sheet.Rows[i];
            if(row.All(string.IsNullOrWhiteSpace))continue;
            string V(int index)=>index>=0&&index<row.Count?row[index].Trim():"";
            if(IsBlankToken(V(taxCol))&&IsBlankToken(V(dueCol)))continue;
            dataRows.Add((ParseMoneyCell(V(taxCol),i+1),ParseMoneyCell(V(dueCol),i+1)));
        }
        J.Need(dataRows.Count==1,"ورقة الشركاء يجب أن تحتوي صف بيانات واحد.");
        J.Need(dataRows[0].Tax>=0,"ضريبة الشريك لا تكون سالبة.");
        return dataRows[0];
    }
    static bool IsBlankToken(string value)
    {
        string text=value.Trim();
        return text==""||text=="-"||text=="—"||text=="–"||text=="."||text=="ـ";
    }
    static decimal ParseMoneyCell(string value,int rowNumber)
    {
        if(IsBlankToken(value))return 0;
        string text=J.Digits(value);
        text=Regex.Replace(text,@"[\u200e\u200f\u202a-\u202e\u00a0]","");
        text=Regex.Replace(text,@"ر\.?\s*س\.?","",RegexOptions.IgnoreCase);
        text=Regex.Replace(text,@"[^\d\-٫,.\+]","");
        text=text.Replace("٬","").Replace(",","").Replace("٫",".");
        if(IsBlankToken(text))return 0;
        // Currency labels can leave an extra trailing '.' after ر.س. — keep the first decimal token only.
        var match=Regex.Match(text,@"^[-+]?\d+(?:\.\d+)?");
        decimal parsed=0;
        J.Need(match.Success&&decimal.TryParse(match.Value,NumberStyles.Number|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out parsed)&&Math.Abs(parsed)<100000000&&Math.Abs(parsed-J.Money(parsed))<=0.000001m,"قيمة نقدية غير صالحة؛ صف "+rowNumber);
        return J.Money(parsed);
    }
    public static JsonObject Parse(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        string company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,period=J.Period(input.Required("period")),reference=input.Required("reference");Scope(actor,company);
        J.Need(bytes.Length is >0 and <=12*1024*1024&&reference.Length<=200,"ملف حتى12ميجابايت ومرجع حتى200حرف.");
        string sha=Convert.ToHexString(SHA256.HashData(bytes)),id="keeta-invoice:"+Hash(company+":"+period+":"+reference);
        var allInvoices=Store.List(c,"platform_invoice",tx);id=InvoiceDiscard.ReplacementId(allInvoices,id,company,period,reference,"Keeta");
        J.Need(!allInvoices.Where(InvoiceDiscard.Active).Any(i=>i.S("sourceSha256")==sha||i.S("sourceHash")==sha||i.S("id")==id),"الفاتورة أو بصمة الملف موجودة؛ لا تعيد تسجيلها.");
        var sheets=Imports.Read(bytes,"invoice.xlsx");
        var candidates=sheets.Select(s=>(Sheet:s,HeaderRow:FindHeaderRow(s))).Where(x=>x.HeaderRow>=0).ToList();
        if(input.S("sheetName")!="")candidates=candidates.Where(x=>x.Sheet.Name==input.S("sheetName")).ToList();
        J.Need(candidates.Count==1,"حدد ورقة الفاتورة الأصلية؛ لا تجمع التفصيلي والملخص.");
        var sheet=candidates[0].Sheet;int headerRow=candidates[0].HeaderRow;
        var header=sheet.Rows[headerRow].Select(Header).ToList();
        int IndexOfAlias(IEnumerable<string> aliases)
        {
            var found=header.Select((v,i)=>(v,i)).Where(x=>aliases.Any(a=>x.v==Header(a))).ToArray();
            J.Need(found.Length<=1,"عمود فاتورة مكرر.");
            return found.Length==0?-1:found[0].i;
        }
        int due=IndexOfAlias(DueAliases);J.Need(due>=0,"عمود فاتورة مفقود: اجمالي المبلغ المستحق");
        var prefix=header.Take(due+1).ToList();
        int Col(string name,bool required=false)
        {
            string want=Header(name);
            var found=prefix.Select((v,i)=>(v,i)).Where(x=>x.v==want).ToArray();
            J.Need(found.Length<=1&&(!required||found.Length==1),"عمود فاتورة مفقود أو مكرر: "+name);
            return found.Length==0?-1:found[0].i;
        }
        int ColAlias(string[] aliases,bool required=false)
        {
            var hits=prefix.Select((v,i)=>(v,i)).Where(x=>aliases.Any(a=>x.v==Header(a))).ToArray();
            J.Need(hits.Length<=1&&(!required||hits.Length==1),"عمود فاتورة مفقود أو مكرر: "+aliases[0]);
            return hits.Length==0?-1:hits[0].i;
        }
        int accountCol=ColAlias(AccountAliases,true),ordersCol=ColAlias(OrdersAliases,true),netCol=ColAlias(NetAliases);
        // October rider sheets: due is ex-VAT (no bare net). Line VAT stays 0; invoice VAT comes from partner tax.
        bool dueIsNetExVat=netCol<0;if(netCol<0)netCol=due;
        var selected=new HashSet<int>{accountCol,ordersCol,netCol,due};foreach(var d in DeductionColumns){int col=Col(d.Header);if(col>=0)selected.Add(col);}var cache=Imports.MissingFormulaCacheRows(bytes,sheet.Name,selected);
        var accounts=Store.List(c,"platform_account",tx);var lines=new JsonArray();var seen=new HashSet<string>();
        var companyRows=(input["companyRows"] as JsonArray??[]).Select(x=>x!.GetValue<int>()).ToHashSet();var usedCompanyRows=new HashSet<int>();
        for(int i=headerRow+1;i<sheet.Rows.Count;i++)
        {
            var row=sheet.Rows[i];string V(int index)=>index>=0&&index<row.Count?row[index].Trim():"";
            if(row.All(string.IsNullOrWhiteSpace))continue;string key=V(accountCol);
            if(IsBlankToken(key))key="";
            if(Header(key) is "الاجمالي" or "المجموع" || key is "Grand Total")continue;
            bool companyRow=companyRows.Contains(i+1);if(key==""&&!companyRow){J.Need(V(netCol)==""&&V(due)=="","حدد سطر تعديل الشركة أو الإجمالي صراحة؛ صف "+(i+1));continue;}
            J.Need(!cache.Contains(i+1),"formula_without_cached_value: row "+(i+1));
            decimal N(int col)=>ParseMoneyCell(V(col),i+1);
            decimal net=N(netCol),inclusive=dueIsNetExVat?net:N(due),vat=dueIsNetExVat?0:J.Money(inclusive-net),orders=N(ordersCol);string account="";
            J.Need(orders>=0&&orders==decimal.Truncate(orders),"طلبات صحيحة غير سالبة مطلوبة؛ صف "+(i+1));
            if(companyRow)
            {
                // Blank-account company line (positive invoiced or negative credit adjustment); components ignored.
                J.Need(key==""&&orders==0&&vat<=0,"تعديل الشركة بدون يوزر وبطلبات صفر؛ صف "+(i+1));
                usedCompanyRows.Add(i+1);
            }
            else
            {
                key=AccountKey(key);J.Need(seen.Add(key),"يوزر مكرر؛ لا تجمع التفصيلي والملخص.");
                var matches=accounts.Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="keeta"&&(a.S("externalAccountId")==key||a.S("code")==key||a.S("id")==key)).ToArray();
                J.Need(matches.Length==1,"اليوزر غير مربوط بشكل فريد بالشركة والمنصة؛ صف "+(i+1));account=matches[0].S("id");
                // Negative due on an account row stays inside the supplier total (rider credit line); no separate reverse path.
                if(!dueIsNetExVat)J.Need(net>=0&&vat>=0,"الإشعار الدائن يحتاج مسار عكس؛ صف "+(i+1));
                else J.Need(vat==0,"ضريبة السطر يجب أن تكون صفرًا في تخطيط الاستحقاق بدون صافي؛ صف "+(i+1));
            }
            var deductions=new JsonArray();
            if(companyRow)
            {
                // Negative company credit: supplier_adjustment amount = -net. Positive company: invoiced/baseGross = due, no deduction.
                if(net<0)deductions.Add(new JsonObject{["category"]="supplier_adjustment",["amount"]=-net,["level"]="unclassified"});
            }
            else
            {
                foreach(var field in DeductionColumns)
                {
                    int col=Col(field.Header);if(col<0)continue;decimal value=N(col),amount=field.Signed?Math.Max(0,-value):Math.Abs(value);if(amount==0)continue;
                    deductions.Add(new JsonObject{["category"]=field.Category,["amount"]=amount,["level"]="unclassified",["sourceHeader"]=field.Header,["sourceSignedAmount"]=value});
                }
            }
            decimal deduction=deductions.OfType<JsonObject>().Sum(d=>d.N("amount")),gross=J.Money(net+deduction);
            var sourceMetrics=new JsonObject();foreach(string name in new[]{"صالح","المركبة","أيام الاتصال-صالحة","ساعات الاتصال اليومي-صالحة","ساعات الاتصال اليومي خلال وقت الذروة-صالحة"}){int col=Col(name);if(col>=0)sourceMetrics[name]=V(col);}
            lines.Add(new JsonObject{["id"]="keeta-line:"+Hash(id+":"+(i+1)),["invoiceId"]=id,["companyId"]=company,["period"]=period,["platform"]="Keeta",["sourceRow"]=i+1,["lineType"]=companyRow?"company":"account",["platformAccountId"]=account,
                ["baseGross"]=gross,["incentives"]=0,["deductionAmount"]=deduction,["deductions"]=deductions,["orders"]=orders,["vat"]=vat,["invoiced"]=net,["invoiceInclusive"]=inclusive,["sourceMetrics"]=sourceMetrics,["status"]="invoiced",["reason"]="final supplier invoice; no expected amount substituted"});
        }
        J.Need(usedCompanyRows.SetEquals(companyRows),"صف تعديل شركة غير موجود أو مختلط بسطر إجمالي.");
        var lineList=lines.OfType<JsonObject>().ToList();
        J.Need(lineList.Count>0&&lineList.Any(l=>l.S("lineType")=="account"),"الفاتورة بلا سطور يوزرات.");
        decimal headerVat=lineList.Sum(l=>l.N("vat"));
        if(dueIsNetExVat)
        {
            var partner=ReadPartnerTotals(sheets);
            headerVat=partner.Tax;
            decimal riderDueSum=lineList.Sum(l=>l.N("invoiced"));
            J.Need(Math.Abs(riderDueSum+headerVat-partner.PartnerDue)<=0.01m,"مجموع استحقاق السائقين مع ضريبة الشريك لا يطابق إجمالي الشريك المستحق.");
        }
        else J.Need(headerVat>=0,"ضريبة الفاتورة سالبة.");
        string role=input.S("accountingRole","cycle_only");J.Need(role is "principal" or "cycle_only","حدد principal أو cycle_only.");
        return new(){["id"]=id,["platform"]="Keeta",["companyId"]=company,["period"]=period,["reference"]=reference,["sourceSha256"]=sha,["sourceSheet"]=sheet.Name,["accountingRole"]=role,["amountExclVat"]=lineList.Sum(l=>l.N("baseGross")),["deductionsTotal"]=lineList.Sum(l=>l.N("deductionAmount")),["vat"]=headerVat,["status"]="invoiced",["odooVoucherNo"]=input.S("odooVoucherNo"),["lines"]=lines};
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {var rows=new JsonArray(Parse(c,null,actor,input,bytes));return new(){["rows"]=rows,["fingerprint"]=FinancialImports.Issue(c,PreviewKind,rows,actor.Identity!.Name!)};}
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        FinancialImports.Verify(c,tx,input,PreviewKind,actor.Identity!.Name!);var parsed=Parse(c,tx,actor,input,bytes);
        J.Need(input["rows"] is JsonArray{Count:1}&&FinancialImports.Canonical(input["rows"]![0])==FinancialImports.Canonical(parsed),"الملف أو خيارات الورقة تغيرت؛ أعد المعاينة.");
        var lines=parsed["lines"]!.DeepClone().AsArray();parsed.Remove("lines");parsed["createdBy"]=actor.Identity!.Name;parsed["createdAt"]=DateTimeOffset.UtcNow.ToString("O");parsed["approvalStatus"]="pending";
        Store.Save(c,"platform_invoice",parsed,tx);
        Store.BufferedWrites(c,tx,()=>{foreach(var line in lines.OfType<JsonObject>())Store.Save(c,"platform_invoice_line",line,tx);});
        Store.Audit(c,tx,"platform.keeta.invoice_imported",parsed.S("id"),actor.Identity!.Name!);return parsed;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/platforms/keeta/invoices",async(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"pay.manage");J.Need(ctx.Request.HasFormContentType,"ارفع ملفxlsx كـmultipart.");var form=await ctx.Request.ReadFormAsync();J.Need(form.Files.Count==1,"ملف واحد لكل فاتورة.");var file=form.Files[0];J.Need(file.Length is >0 and <=12*1024*1024&&Path.GetExtension(file.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase),"ملفxlsx حتى12ميجابايت.");
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
