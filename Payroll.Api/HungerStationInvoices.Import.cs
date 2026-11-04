using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class HungerStationInvoices
{
    const string ImportKind="hs-invoice";
    sealed record SourceFile(byte[] Bytes,string Hash);
    static void Scope(ClaimsPrincipal actor,string permission)
    {AccessPolicy.Demand(actor,permission);if(!AccessPolicy.Administrator(actor)&&!FinancialScopes.Platform(actor,"HungerStation"))throw new ApiError("منصة هنقر خارج النطاق المالي.",403);}
    public static JsonObject GetInvoice(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var invoice=Store.Get(c,id,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);if(invoice.S("platform")!="HungerStation")throw new ApiError("الفاتورة غير موجودة.",404);FinancialScopes.DemandSponsor(actor,invoice.S("companyId"));return invoice;
    }
    static JsonObject GetLine(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {var line=Store.Get(c,id,"platform_invoice_line",tx)??throw new ApiError("سطر الفاتورة غير موجود.",404);GetInvoice(c,tx,actor,line.S("invoiceId"));return line;}
    static List<SourceFile> Files(JsonObject input)
    {
        var files=input["files"] as JsonArray??throw new ApiError("ملفات المصنف مطلوبة.");J.Need(files.Count is >0 and <=3,"ارفع من مصنف واحد إلى ثلاثة مصنفات.");var result=new List<SourceFile>();
        foreach(var item in files)
        {
            var row=item as JsonObject??throw new ApiError("ملف غير صالح.");J.Need(Path.GetExtension(row.S("fileName")).Equals(".xlsx",StringComparison.OrdinalIgnoreCase),"فواتير هنقر تحتاج مصنف xlsx.");
            byte[] bytes=FinancialImports.FileBytes(row);result.Add(new(bytes,Convert.ToHexString(SHA256.HashData(bytes))));
        }
        J.Need(result.Sum(f=>(long)f.Bytes.Length)<=8*1024*1024,"إجمالي الملفات لا يتجاوز 8 ميجابايت؛ قسّم الدفعة.");J.Need(result.Select(f=>f.Hash).Distinct().Count()==result.Count,"الملف مكرر في الدفعة.");return result;
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");J.Period(input.Required("period"));var files=Files(input);var errors=new List<RowIssue>();var rows=new JsonArray();int index=0;
        foreach(var file in files)
        {
            index++;List<Imports.Sheet> sheets;
            try {sheets=Imports.Read(file.Bytes,"invoice.xlsx");}
            catch(Exception e) when(e is InvalidDataException or System.Xml.XmlException or FormatException or ApiError){errors.Add(new(index,"workbook",0,"invalid_workbook"));continue;}
            var invoice=Parse(c,actor,sheets,input,file.Hash,index,errors);if(invoice!=null){PrepareRevision(c,actor,input,invoice);rows.Add(invoice);}
        }
        if(rows.OfType<JsonObject>().GroupBy(r=>r.S("id")).Any(g=>g.Count()>1))errors.Add(new(0,"workbook",0,"duplicate_invoice_key"));
        int fresh=0,unchanged=0;
        foreach(var row in rows.OfType<JsonObject>())
        {
            var previous=Store.Get(c,row.S("id"),"platform_invoice");
            if(previous==null)fresh++;else if(previous.S("importFingerprint")==Hash(FinancialImports.Canonical(row)))unchanged++;else errors.Add(new(0,"workbook",0,"invoice_already_imported_different_facts"));
        }
        return new(){["rows"]=rows,["canCommit"]=errors.Count==0,["fingerprint"]=errors.Count==0?FinancialImports.Issue(c,ImportKind,rows,actor.Identity!.Name!):"",["errors"]=JsonSerializer.SerializeToNode(errors,new JsonSerializerOptions(JsonSerializerDefaults.Web)),["counts"]=new JsonObject{["newInvoices"]=fresh,["unchangedInvoices"]=unchanged,["lines"]=rows.OfType<JsonObject>().Sum(r=>r["lines"]!.AsArray().Count),["errors"]=errors.Count,["flags"]=rows.OfType<JsonObject>().Sum(r=>r["flags"]!.AsArray().Count)}};
    }
    static void Evidence(Store store,List<SourceFile> files)
    {
        string directory=Path.Combine(store.DirectoryPath,"platform-imports","hs");
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        foreach(var source in files)
        {
            string path=Path.Combine(directory,source.Hash+".xlsx");
            if(File.Exists(path)){J.Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))==source.Hash,"ملف الإثبات المحفوظ لا يطابق بصمته.");continue;}
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using var file=new FileStream(path,options);file.Write(source.Bytes);
        }
    }
    public static JsonObject Commit(Store store,DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");FinancialImports.Verify(c,tx,input,ImportKind,actor.Identity!.Name!);var files=Files(input);var rows=input["rows"]!.AsArray().OfType<JsonObject>().ToList();
        J.Need(rows.All(r=>r.S("period")==J.Period(input.Required("period"))),"شهر التأكيد لا يطابق المعاينة.");
        J.Need(rows.Count==files.Count&&rows.All(r=>files.Any(f=>f.Hash==r.S("sourceHash"))),"الملفات لا تطابق المعاينة الموقعة.");
        foreach(var row in rows){FinancialScopes.DemandSponsor(actor,row.S("companyId"));var previous=Store.Get(c,row.S("id"),"platform_invoice",tx);if(previous!=null&&previous.S("importFingerprint")!=Hash(FinancialImports.Canonical(row)))throw new ApiError("الفاتورة مسجلة بوقائع مختلفة؛ راجع التصحيح والعكس.",409);}
        Evidence(store,files);int imported=0,unchanged=0,disputes=0;
        foreach(var row in rows)
        {
            if(Store.Get(c,row.S("id"),"platform_invoice",tx)!=null){unchanged++;continue;}
            var invoice=row.Clone();string fingerprint=Hash(FinancialImports.Canonical(row));var lines=invoice["lines"]!.AsArray().OfType<JsonObject>().Select(l=>l.Clone()).ToList();invoice.Remove("lines");
            invoice["lineIds"]=new JsonArray(lines.Select(l=>(JsonNode?)JsonValue.Create(l.S("id"))).ToArray());invoice["importFingerprint"]=fingerprint;invoice["createdBy"]=actor.Identity!.Name;invoice["createdAt"]=DateTimeOffset.UtcNow.ToString("O");invoice["approvalStatus"]="pending";
            invoice["status"]=lines.Any(l=>l.S("lineType")=="company"&&l.N("carDeduction")!=0)?"disputed":lines.Any(l=>l.N("tgaAmount")!=0)?"pending_allocation":"pending_review";
            Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_imported",invoice.S("id"),actor.Identity!.Name!);imported++;
            foreach(var line in lines)
            {
                line["invoiceId"]=invoice.S("id");line["companyId"]=invoice.S("companyId");line["period"]=invoice.S("period");line["status"]=line.N("tgaAmount")!=0?"pending_allocation":"pending_review";
                Store.Save(c,"platform_invoice_line",line,tx);Store.Audit(c,tx,"platform.hs.invoice_line_imported",line.S("id"),actor.Identity!.Name!);
                if(line.S("lineType")=="company"&&line.N("carDeduction")!=0)
                {
                    var dispute=new JsonObject{["id"]="hs-dispute:"+Hash(line.S("id")),["invoiceId"]=invoice.S("id"),["lineId"]=line.S("id"),["companyId"]=invoice.S("companyId"),["period"]=invoice.S("period"),["category"]="CAR",["sourceAmount"]=line.N("carDeduction"),["amount"]=Math.Abs(line.N("carDeduction")),["status"]="open",["createdBy"]=actor.Identity!.Name};
                    Store.Save(c,"platform_dispute",dispute,tx);Store.Audit(c,tx,"platform.hs.dispute_created",dispute.S("id"),actor.Identity!.Name!);disputes++;
                    Notifications.Raise(c,tx,"notice:"+dispute.S("id"),"system","فاتورة هنقر تحتاج اعتراضًا رسميًا؛ راجع شاشة المنصة.","platforms.hs.view",actor.Identity!.Name!,eventKey:dispute.S("id"),companyId:invoice.S("companyId"),platform:"HungerStation");
                }
            }
        }
        return new(){["importedInvoices"]=imported,["unchangedInvoices"]=unchanged,["disputes"]=disputes};
    }
    public static void MapImports(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/platforms/hs/invoices/import/preview",(JsonObject input,HttpContext ctx)=>store.Read(c=>Preview(c,ctx.User,input))).WithTags("Platforms").WithSummary("Preview signed HS workbooks with VAT, row totals and period validation");
        api.MapPost("/platforms/hs/invoices/import/commit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Commit(store,c,tx,ctx.User,input))).WithTags("Platforms").WithSummary("Store signed HS invoice facts and disputes without posting");
    }
}
