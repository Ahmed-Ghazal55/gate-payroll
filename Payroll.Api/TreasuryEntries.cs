using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static class TreasuryEntries
{
    public static JsonObject LegacyDebtOpening(DbConnection c,DbTransaction? tx,string id,JsonObject input)
    {
        var debt=Store.Get(c,id,"debt",tx)??throw new ApiError("الدين غير موجود.",404);
        J.Need(debt.N("accountingVersion")<AccountingEvents.Version&&debt.S("journalEntryId")==""&&debt.S("openingReconciliationId")=="","هذا الدين مثبت بالفعل؛ لا تعاد تسوية افتتاحه.");
        var sponsor=FinancialCore.FindSponsor(debt.S("creditorSponsorId"))??FinancialCore.ResolveSponsor(input.Required("sponsorId"));
        J.Need(input.ContainsKey("expectedRemainingBalance")&&input.N("expectedRemainingBalance")==debt.N("remainingBalance"),"تغير الرصيد؛ أعد المعاينة.");
        decimal current=AccountingEvents.DebtBalance(c,tx,debt,sponsor.Id);
        foreach(var other in FinancialCore.Sponsors.Where(s=>s.Id!=sponsor.Id))J.Need(AccountingEvents.DebtBalance(c,tx,debt,other.Id)==0,"توجد ذمة على سجل آخر؛ راجع دليل الملكية بدل فتح رصيد مكرر.");
        string platform=debt.S("platform");if(platform=="")platform=input.S("platform");if(debt.S("category")=="PlatformTransferredDebt")J.Need(new[]{"Jahez","Keeta","HungerStation","Ninja","Other"}.Contains(platform),"حدد المنصة من مستند الدين التاريخي.");
        return new JsonObject{["id"]=id,["sponsorId"]=sponsor.Id,["category"]=debt.S("category"),["platform"]=platform,["iqama"]=debt.S("iqama"),["accountCode"]=AccountingEvents.ReceivableCode(debt),["expectedRemainingBalance"]=debt.N("remainingBalance"),["currentJournalBalance"]=current,["adjustment"]=J.Money(debt.N("remainingBalance")-current),["sourceRef"]=input.Required("sourceRef"),["notes"]=input.Required("notes")};
    }
    private static readonly ExcelColumn[] OpeningColumns=[new("accountCode",Identity:true),new("sponsorCode"),new("iqama",Identity:true),new("verifiedBalance",Numeric:true),new("sourceRef"),new("notes")];
    public static JsonArray ValidateOpening(DbConnection c,DbTransaction? tx,JsonArray rows)
    {
        J.Need(rows.Count is >0 and <=1000,"عدد صفوف تسوية الافتتاح غير صالح.");var seen=new HashSet<string>();var result=new JsonArray();
        foreach(var node in rows)
        {
            var row=node!.AsObject();string code=row.Required("accountCode");var account=FinancialCore.ResolveAccount(code);var sponsor=FinancialCore.ResolveSponsor(row.Required("sponsorCode"));
            J.Need(account.IsVault||code is "1211" or "2110" or "1240" or "1241" or "2140" or "2141","الافتتاح العام للصناديق والرواتب وحسابات المنصات الرقابية؛ افتتاح الدين من مساره المخصص.");
            J.Need(account.DefaultSponsorId==null||account.DefaultSponsorId==sponsor.Id,"ملكية حساب الافتتاح لا تطابق السجل.");
            string iqama=code is "1211" or "2110"?J.Iqama(row.Required("iqama")):"";decimal target=Debts.OptionalAmount(row,"verifiedBalance");
            J.Need(row.ContainsKey("verifiedBalance")&&target>=0,"أدخل الرصيد الطبيعي الفعلي المراجع.");
            string id="opening:"+sponsor.Code+":"+code+":"+iqama;
            J.Need(seen.Add(id)&&Store.Get(c,id,"opening_reconciliation",tx)==null,"الحساب مراجع افتتاحيًا بالفعل أو مكرر في الدفعة.");
            decimal debitNet=code is "1211" or "2110"?AccountingEvents.Balance(c,tx,code,iqama,sponsor.Id):Store.List(c,"journal",tx).SelectMany(j=>j["lines"]!.AsArray()).OfType<JsonObject>().Where(l=>l.S("accountCode")==code&&l.S("sponsorId")==sponsor.Id).Sum(l=>l.N("debit")-l.N("credit"));
            decimal current=account.NaturalSide=="Credit"?-debitNet:debitNet;
            if(iqama!="")
            {
                J.Need(Store.Get(c,iqama,"rider",tx)!=null,"المندوب غير موجود.");
                J.Need(!Store.List(c,"run",tx).Any(r=>r.S("iqama")==iqama&&r.N("accountingVersion")==AccountingEvents.Version),"لا تعاود فتح رصيد راتب بعد بدء الترحيل الجديد.");
                decimal legacy=Store.List(c,"ledger",tx).Where(l=>l.S("iqama")==iqama).Sum(l=>l.N("amount"));
                J.Need(target==(code=="2110"?Math.Max(0,legacy):Math.Max(0,-legacy)),"الافتتاح لا يطابق صافي دفتر الراتب القديم؛ لا يُنقل رصيد بالتخمين.");
                foreach(var other in FinancialCore.Sponsors.Where(s=>s.Id!=sponsor.Id))J.Need(AccountingEvents.Balance(c,tx,"2110",iqama,other.Id)==0&&AccountingEvents.Balance(c,tx,"1211",iqama,other.Id)==0,"يوجد افتتاح/رصيد راتب على سجل آخر.");
            }
            string reference=row.Required("sourceRef");J.Need(!Store.List(c,"journal",tx).Any(j=>j.S("sourceRef")==reference),"مرجع الافتتاح مستخدم.");
            result.Add(new JsonObject{["id"]=id,["accountCode"]=code,["sponsorCode"]=sponsor.Code,["sponsorId"]=sponsor.Id,["iqama"]=iqama,["verifiedBalance"]=target,["currentBalance"]=current,["adjustment"]=J.Money(target-current),["sourceRef"]=reference,["notes"]=row.Required("notes")});
        }
        return result;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/debts/{id}/reconcile-opening/preview",(string id,JsonObject input,HttpContext ctx)=>store.Read(c=>
        {
            var row=LegacyDebtOpening(c,null,id,input);FinancialScopes.DemandSponsor(ctx.User,row.S("sponsorId"));J.Need(FinancialScopes.Category(ctx.User,row.S("category")),"الفئة خارج نطاقك.");var rows=new JsonArray(row);return new{rows,fingerprint=FinancialImports.Issue(c,"legacy-debt-opening",rows,ctx.User.Identity!.Name!)};
        }));
        api.MapPost("/debts/{id}/reconcile-opening/commit",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            FinancialImports.Verify(c,tx,input,"legacy-debt-opening",ctx.User.Identity!.Name!);J.Need(input["rows"] is JsonArray {Count:1}&&input["rows"]![0].S("id")==id,"معاينة الافتتاح لا تخص الدين المطلوب.");
            var row=LegacyDebtOpening(c,tx,id,input["rows"]![0]!.AsObject());FinancialScopes.DemandSponsor(ctx.User,row.S("sponsorId"));J.Need(FinancialScopes.Category(ctx.User,row.S("category")),"الفئة خارج نطاقك.");
            var debt=Store.Get(c,id,"debt",tx)!;string sponsor=row.S("sponsorId");decimal delta=row.N("adjustment");
            if(delta!=0){var journal=FinancialCore.PostJournal(c,tx,"legacy_debt_opening","مطابقة افتتاح دين قديم بمستند",DateTime.UtcNow.ToString("yyyy-MM"),row.S("sourceRef"),new JsonArray(
                FinancialCore.CreateJournalLine(row.S("accountCode"),sponsor,Math.Max(0,delta),Math.Max(0,-delta),row.S("notes"),row.S("iqama"),id),FinancialCore.CreateJournalLine("3101",sponsor,Math.Max(0,-delta),Math.Max(0,delta),"مقابل افتتاح الدين المراجع",row.S("iqama"),id)),ctx.User.Identity!.Name!,row.S("iqama"),id,sponsor,sponsor);debt["journalEntryId"]=journal.S("id");row["journalEntryId"]=journal.S("id");}
            row["id"]="debt-opening:"+id;row["createdBy"]=ctx.User.Identity!.Name!;row["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"opening_reconciliation",row,tx);
            debt["openingReconciliationId"]=row.S("id");debt["platform"]=row.S("platform");debt["receivableAccountCode"]=row.S("accountCode");debt["creditorSponsorId"]=sponsor;debt["creditorSponsorNameAr"]=FinancialCore.ResolveSponsor(sponsor).ShortNameAr;Store.Save(c,"debt",debt,tx);Store.Audit(c,tx,"debt.opening_reconciled",id,ctx.User.Identity!.Name!);return new{debt,reconciliation=row};
        }));
        api.MapGet("/finance/opening/template",()=>Results.File(ExcelFiles.Write(OpeningColumns,[]),ExcelFiles.Mime,"treasury-opening-v1.xlsx"));
        api.MapPost("/finance/opening/preview",(JsonObject input,HttpContext ctx)=>store.Read(c=>
        {
            JsonArray rows;
            if(input["rows"] is JsonArray supplied)rows=supplied;
            else
            {
                var sheets=Imports.Read(FinancialImports.FileBytes(input),input.Required("filename"));
                J.Need(sheets.Count==1&&sheets[0].Rows.Count>1&&sheets[0].Rows[0].SequenceEqual(OpeningColumns.Select(x=>x.Header)),"استخدم قالب الافتتاح v1 دون تغيير الأعمدة.");
                rows=new JsonArray();int number=1;
                foreach(var cells in sheets[0].Rows.Skip(1)){number++;if(cells.All(string.IsNullOrWhiteSpace))continue;J.Need(cells.Count==OpeningColumns.Length,$"عدد أعمدة الصف {number} غير صحيح.");var row=new JsonObject();for(int i=0;i<OpeningColumns.Length;i++)row[OpeningColumns[i].Header]=cells[i];rows.Add(row);}
            }
            var validated=ValidateOpening(c,null,rows);foreach(var row in validated.OfType<JsonObject>()){FinancialScopes.DemandSponsor(ctx.User,row.S("sponsorId"));if(FinancialCore.ResolveAccount(row.S("accountCode")).IsVault)FinancialScopes.DemandAccount(ctx.User,row.S("accountCode"));}
            return new{rows=validated,fingerprint=FinancialImports.Issue(c,"treasury-opening",validated,ctx.User.Identity!.Name!),message="هذه تسوية افتتاح يدوية؛ راجع كل رصيد مع مستنده، ولا تمثل إيرادًا جديدًا."};
        }));
        api.MapPost("/finance/opening/commit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            FinancialImports.Verify(c,tx,input,"treasury-opening",ctx.User.Identity!.Name!);var rows=ValidateOpening(c,tx,input["rows"]!.AsArray());
            foreach(var row in rows.OfType<JsonObject>())
            {
                string code=row.S("accountCode"),sponsor=row.S("sponsorId"),iqama=row.S("iqama");FinancialScopes.DemandSponsor(ctx.User,sponsor);if(FinancialCore.ResolveAccount(code).IsVault)FinancialScopes.DemandAccount(ctx.User,code);
                decimal adjustment=row.N("adjustment");bool debit=FinancialCore.ResolveAccount(code).NaturalSide=="Debit";decimal delta=debit?adjustment:-adjustment;
                if(delta!=0){var j=FinancialCore.PostJournal(c,tx,"reviewed_opening","تسوية افتتاح بمستند: "+row.S("notes"),DateTime.UtcNow.ToString("yyyy-MM"),row.S("sourceRef"),new JsonArray(
                    FinancialCore.CreateJournalLine(code,sponsor,Math.Max(0,delta),Math.Max(0,-delta),row.S("notes"),iqama),FinancialCore.CreateJournalLine("3101",sponsor,Math.Max(0,-delta),Math.Max(0,delta),"مقابل تسوية افتتاح معتمدة",iqama)),ctx.User.Identity!.Name!,iqama,"",sponsor,sponsor);row["journalEntryId"]=j.S("id");}
                row["createdBy"]=ctx.User.Identity!.Name!;row["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"opening_reconciliation",row,tx);
            }
            Store.Audit(c,tx,"treasury.opening_reconciled","rows:"+rows.Count,ctx.User.Identity!.Name!);return new{ok=true,importedCount=rows.Count};
        }));
        api.MapPost("/finance/liability-payment",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            string debtId=input.Required("debtId");var debt=Store.Get(c,debtId,"debt",tx)??throw new ApiError("الالتزام غير موجود.",404);FinancialScopes.DemandDebt(ctx.User,debt);
            J.Need(debt.S("entryMode")=="Accrual"&&debt.S("recognitionAccountCode")=="2130"&&debt.S("journalEntryId")!="","السداد لفاتورة/التزام مثبت ومعتمد فقط.");
            string owner=debt.S("originSponsorId"),vault=input.Required("paymentVaultCode");FinancialScopes.DemandAccount(ctx.User,vault);
            var account=FinancialCore.ValidateVaultForOperation(vault,debt.S("category"),"disbursement");J.Need(account.DefaultSponsorId==owner,"سداد المورد من حساب نفس السجل؛ التحويل بين السجلات له مستند مستقل.");
            decimal amount=Debts.Amount(input,"amount"),remaining=-Store.List(c,"journal",tx).SelectMany(j=>j["lines"]!.AsArray()).OfType<JsonObject>().Where(l=>l.S("accountCode")=="2130"&&l.S("debtId")==debtId&&l.S("sponsorId")==owner).Sum(l=>l.N("debit")-l.N("credit"));
            J.Need(amount<=remaining,"المبلغ يتجاوز الالتزام غير المسدد.");AccountingEvents.DemandAvailable(c,tx,vault,amount);
            var journal=FinancialCore.PostJournal(c,tx,"supplier_payment","سداد التزام خدمة مثبت",J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM"))),input.Required("sourceRef"),new JsonArray(
                FinancialCore.CreateJournalLine("2130",owner,amount,0,"سداد الالتزام وليس إنشاء دين جديد",debt.S("iqama"),debtId),FinancialCore.CreateJournalLine(vault,owner,0,amount,"دفع فعلي للمورد",debt.S("iqama"),debtId)),ctx.User.Identity!.Name!,debt.S("iqama"),debtId,owner,owner);
            debt["supplierPaidAmount"]=J.Money(debt.N("supplierPaidAmount")+amount);Store.Save(c,"debt",debt,tx);
            Store.Audit(c,tx,"supplier.paid",debtId,ctx.User.Identity!.Name!);return journal;
        }));
    }
}
