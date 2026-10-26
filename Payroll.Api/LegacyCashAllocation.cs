using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// The former TR posting account is now a control group. Close its own legacy balance once;
// never duplicate it by adding the same opening money to all of its new children.
public static class LegacyCashAllocation
{
    public const string Source="MAIN.TR";
    public const string Marker="legacy-cash-allocation:MAIN.TR";
    public static JsonObject Validate(DbConnection c,DbTransaction? tx,JsonObject input,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"treasury.opening");
        var owner=FinancialCore.ResolveSponsor("TR");FinancialScopes.DemandSponsor(actor,owner.Id);
        J.Need(Store.Get(c,Marker,"legacy_cash_allocation",tx)==null,"الخزنة التاريخية وُزعت بالفعل؛ لا تعاد المعاملة.");
        decimal book=AccountingPolicy.Balance(c,tx,Source);J.Need(book!=0,"لا يوجد رصيد مباشر بالخزنة التاريخية يحتاج توزيعًا.");
        var result=new JsonObject{["id"]=Marker,["sponsorId"]=owner.Id,["bookBalance"]=book,["sourceRef"]=input.Required("sourceRef"),["notes"]=input.Required("notes")};
        decimal verified=0,existing=0;
        foreach(var pair in new[]{("debtBalance","MAIN.TR.DEBT"),("settlementBalance","MAIN.TR.SET"),("feeBalance","MAIN.TR.FEE")})
        {
            FinancialScopes.DemandAccount(actor,pair.Item2);J.Need(input.ContainsKey(pair.Item1),"أدخل الأرصدة الفعلية المراجعة لكل غرض، حتى الصفر.");decimal amount=Debts.OptionalAmount(input,pair.Item1);result[pair.Item1]=amount;verified+=amount;
            decimal current=AccountingPolicy.Balance(c,tx,pair.Item2);result["current_"+pair.Item1]=current;existing+=current;
        }
        decimal difference=J.Money(verified-book-existing);string contra=input.S("differenceAccountCode");
        if(difference!=0)J.Need(contra=="3101"||difference>0&&contra=="4104"||difference<0&&contra=="5204","صنّف فرق المطابقة صراحة: افتتاح موثق أو زيادة/عجز نقدي مراجع؛ لا يرحل تلقائيًا إلى حقوق الملكية.");
        result["existingChildrenTotal"]=existing;result["currentTotal"]=J.Money(book+existing);result["verifiedTotal"]=verified;result["reconciliationDifference"]=difference;result["differenceAccountCode"]=contra;result["equityAdjustment"]=contra=="3101"?difference:0;return result;
    }
    public static JsonObject ClosingLine(string owner,decimal book)=>new(){["accountCode"]=Source,["accountNameAr"]=FinancialCore.ResolveAccount(Source).NameAr,["accountType"]="Asset",["naturalSide"]="Debit",["sponsorId"]=owner,["sponsorCode"]="TR",["debit"]=Math.Max(0,-book),["credit"]=Math.Max(0,book),["closingLegacyGroup"]=true,["note"]="إقفال الرصيد المباشر للحساب القديم فقط مع حفظ قيوده التاريخية"};
    public static void ValidateClosing(DbConnection c,DbTransaction tx,JsonObject line,string type)
    {
        J.Need(type=="legacy_cash_allocation"&&line.B("closingLegacyGroup")&&line.S("accountCode")==Source&&line.S("sponsorId")==FinancialCore.ResolveSponsor("TR").Id,"ترحيل مباشر على حساب تجميعي غير مسموح.");
        decimal book=AccountingPolicy.Balance(c,tx,Source);
        J.Need(book!=0&&line.N("debit")==Math.Max(0,-book)&&line.N("credit")==Math.Max(0,book),"لا يمكن إقفال أكثر من الرصيد التاريخي الفعلي أو إعادة استخدام الحساب الأب.");
    }
    public static JsonObject Post(DbConnection c,DbTransaction tx,JsonObject input,ClaimsPrincipal actor)
    {
        var row=Validate(c,tx,input,actor);string owner=row.S("sponsorId");var lines=new JsonArray(ClosingLine(owner,row.N("bookBalance")));
        foreach(var pair in new[]{("debtBalance","MAIN.TR.DEBT"),("settlementBalance","MAIN.TR.SET"),("feeBalance","MAIN.TR.FEE")}){decimal delta=row.N(pair.Item1)-row.N("current_"+pair.Item1);if(delta!=0)lines.Add(FinancialCore.CreateJournalLine(pair.Item2,owner,Math.Max(0,delta),Math.Max(0,-delta),"مطابقة الرصيد الفعلي حسب الغرض دون تكرار ما قُيد مسبقًا"));}
        decimal adjustment=row.N("reconciliationDifference");if(adjustment!=0)lines.Add(FinancialCore.CreateJournalLine(row.Required("differenceAccountCode"),owner,Math.Max(0,-adjustment),Math.Max(0,adjustment),"فرق مطابقة مصنف صراحة بمستند المراجع"));
        var journal=FinancialCore.PostJournal(c,tx,"legacy_cash_allocation","توزيع الرصيد التاريخي ومطابقته مع النقد الفعلي",DateTime.UtcNow.ToString("yyyy-MM"),row.S("sourceRef"),lines,actor.Identity!.Name!,"","",owner,owner);
        row["journalEntryId"]=journal.S("id");row["createdBy"]=actor.Identity.Name;row["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"legacy_cash_allocation",row,tx);Store.Audit(c,tx,"treasury.legacy_cash_allocated",Marker,actor.Identity.Name!);return row;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/legacy-cash-allocation/preview",(JsonObject input,HttpContext ctx)=>store.Read(c=>{var rows=new JsonArray(Validate(c,null,input,ctx.User));return new{rows,fingerprint=FinancialImports.Issue(c,"legacy-cash-allocation",rows,ctx.User.Identity!.Name!)};}));
        api.MapPost("/finance/legacy-cash-allocation/commit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{FinancialImports.Verify(c,tx,input,"legacy-cash-allocation",ctx.User.Identity!.Name!);J.Need(input["rows"] is JsonArray{Count:1},"معاينة توزيع واحدة مطلوبة.");return Post(c,tx,input["rows"]![0]!.AsObject(),ctx.User);}));
    }
}
