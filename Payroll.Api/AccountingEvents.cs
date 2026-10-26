using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

// Financial events are posted in the caller's serializable transaction. Previews never post.
public static class AccountingEvents
{
    public const int Version = 2;
    public static bool IsAdvance(string category)=>category is "Advance" or "CashAdvance" or "BankAdvance" or "CashCustody";
    public static string EntryMode(JsonObject input,string category)
    {
        J.Need(category!="NegativeSalaryCarryover","عجز الراتب يثبت في المسير أو افتتاح الرواتب؛ لا تنشئ ذمة ثانية لنفس العجز.");
        string mode=input.S("entryMode",IsAdvance(category)?"Disbursement":"Accrual");
        J.Need(mode is "Disbursement" or "Accrual" or "OpeningBalance","نوع الحدث المالي غير صالح.");
        J.Need(category is not ("PlatformTransferredDebt" or "NegativeSalaryCarryover")||mode!="Disbursement","الدين المرحل لا يثبت صرف نقد جديد.");
        J.Need(!IsAdvance(category)||mode!="Accrual","السلفة تحتاج صرفًا فعليًا أو رصيدًا افتتاحيًا موثقًا.");
        J.Need(category!="NegativeSalaryCarryover"||mode=="OpeningBalance","العجز الجديد يُرحّل من المسير؛ لا تنشئ دينًا ثانيًا لنفس عجز الراتب.");
        return mode;
    }
    public static string RecognitionAccount(JsonObject input,string category,string mode)
    {
        if(mode=="OpeningBalance")return "3101";
        if(mode=="Disbursement")return "";
        if(category=="PlatformTransferredDebt")return input.S("platform")=="Jahez"?"2140":"2141";
        string code=input.S("recognitionAccountCode","2130");
        J.Need(code is "2130" or "4103" or "5301","اختر مقابل إثبات معتمدًا: التزام مورد، أو إيراد خدمة للشركة، أو تسوية تكلفة راتب.");
        return code;
    }
    public static void DemandAvailable(DbConnection c,DbTransaction tx,string code,decimal amount)=>AccountingPolicy.RequireAvailable(AccountingPolicy.Balance(c,tx,code),amount);
    public static decimal Balance(DbConnection c,DbTransaction? tx,string account,string iqama,string? sponsor=null)=>J.Money(
        Store.List(c,"journal",tx).SelectMany(j=>(j["lines"] as JsonArray)?.OfType<JsonObject>()??[])
        .Where(l=>l.S("accountCode")==account&&l.S("iqama")==iqama&&(sponsor==null||l.S("sponsorId")==sponsor)).Sum(l=>l.N("debit")-l.N("credit")));
    public static decimal DebtBalance(DbConnection c,DbTransaction? tx,JsonObject debt,string sponsor)=>J.Money(Store.List(c,"journal",tx).SelectMany(j=>j["lines"]!.AsArray()).OfType<JsonObject>().Where(l=>l.S("debtId")==debt.S("id")&&l.S("sponsorId")==sponsor&&l.S("accountCode")==ReceivableCode(debt)).Sum(l=>l.N("debit")-l.N("credit")));
    public static bool HasDebtOpening(JsonObject debt)=>debt.S("journalEntryId")!=""||debt.S("openingReconciliationId")!="";
    public static string ReceivableCode(JsonObject debt)
    {
        if(debt.S("debtorType")=="Employee")return "1220";
        string code=debt.S("receivableAccountCode");if(code=="")code=FinancialCore.ResolveCategory(debt.S("category")).ReceivableAccountCode;
        J.Need(code is "1210" or "1220" or "1230","حساب الذمة غير صالح؛ راجع تصنيف الدين.");return code;
    }
    public static FinancialCore.SponsorDef PayrollSponsor(JsonObject rider,JsonObject input)
    {
        var owner=FinancialCore.FindSponsor(input.S("sponsorId"))??FinancialCore.FindSponsor(rider.S("financeSponsorId"))??FinancialCore.FindSponsor(rider.S("sponsorId"))??FinancialCore.FindSponsor(rider.S("sponsor"));
        return owner??throw new ApiError("حدد السجل المالي للمسير؛ كفالة المندوب لا تكفي لإسناد راتب إلى شركة افتراضية.");
    }
    public static void VerifyPayrollOpening(DbConnection c,DbTransaction tx,string iqama,string sponsor,decimal opening)
    {
        decimal gl=J.Money(-Balance(c,tx,"2110",iqama,sponsor)-Balance(c,tx,"1211",iqama,sponsor));
        J.Need(gl==opening,"رصيد الرواتب القديم غير مطابق للأستاذ لهذا السجل؛ راجع واعتمد رصيد افتتاحي موثق أولًا.");
        foreach(var other in FinancialCore.Sponsors.Where(s=>s.Id!=sponsor))
            J.Need(Balance(c,tx,"2110",iqama,other.Id)==0&&Balance(c,tx,"1211",iqama,other.Id)==0,"توجد مستحقات راتب على سجل آخر؛ يلزم تحويل موثق قبل تغيير السجل المالي للمسير.");
    }
    public static void VerifyPayrollClosing(DbConnection c,DbTransaction tx,PayrollResult result,string sponsor)
    {
        decimal balance=J.Money(-Balance(c,tx,"2110",result.Iqama,sponsor)-Balance(c,tx,"1211",result.Iqama,sponsor));
        J.Need(balance==result.ClosingBalance,"لم تتطابق قيود الاستحقاق والاستقطاع مع المسير؛ ألغيت المعاملة كاملة.");
    }
    public static JsonObject PostPayroll(DbConnection c,DbTransaction tx,PayrollResult result,JsonObject run,string sponsor,string actor)
    {
        VerifyPayrollOpening(c,tx,result.Iqama,sponsor,result.OpeningBalance+result.Lines.Where(l=>l.Category=="already_paid").Sum(l=>l.Amount));
        foreach(var deduction in result.Lines.Where(l=>l.Category=="debt_installment"))
        {
            var debt=Store.Get(c,deduction.Source,"debt",tx)??throw new ApiError("تغير الدين؛ أعد المعاينة.");
            FinancialCore.RequireDebtSponsor(debt);
            J.Need(HasDebtOpening(debt),"الدين القديم يحتاج إثبات رصيده الافتتاحي قبل المقاصة مع الراتب.");
            J.Need(DebtBalance(c,tx,debt,debt.S("creditorSponsorId"))>=-deduction.Amount,"رصيد الذمة لا يطابق المديونية؛ راجع المطابقة قبل الاستقطاع.");
        }
        var lines=new JsonArray();
        void Line(string account,decimal debit,decimal credit,string note)=>lines.Add(FinancialCore.CreateJournalLine(account,sponsor,debit,credit,note,result.Iqama));
        foreach(var line in result.Lines.Where(l=>l.Category is not ("debt_installment" or "already_paid")&&l.Amount!=0))
        {
            decimal amount=Math.Abs(line.Amount);
            // Commission is actual platform proceeds held for the rider, with the company fee separate.
            string platform=line.Platform!=""?line.Platform:(run["sourceRows"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(w=>w.S("sourceRef")==line.Source).S("platform")??"";
            string counterpart=line.Category=="platform_settlement"?(line.Amount>0?(platform=="Jahez"?"1240":"1241"):(platform=="Jahez"?"2140":"2141")):line.Category=="company_fee"?"4102":"5301";
            if(line.Amount>0){Line(counterpart,amount,0,line.Description);Line("2110",0,amount,line.Description);}
            else {Line("2110",amount,0,line.Description);Line(counterpart,0,amount,line.Description);}
        }
        decimal signedPayable=-Balance(c,tx,"2110",result.Iqama,sponsor)+lines.OfType<JsonObject>().Where(l=>l.S("accountCode")=="2110").Sum(l=>l.N("credit")-l.N("debit"));
        decimal priorDeficit=Balance(c,tx,"1211",result.Iqama,sponsor);
        decimal offset=J.Money(Math.Min(Math.Max(0,signedPayable),Math.Max(0,priorDeficit)));
        if(offset>0){Line("2110",offset,0,"مقاصة عجز راتب سابق موثق");Line("1211",0,offset,"إقفال العجز من الاستحقاق الجديد");signedPayable-=offset;}
        if(signedPayable<0){Line("1211",-signedPayable,0,"عجز راتب مثبت دون صرف نقد أو تكرار مديونية");Line("2110",0,-signedPayable,"فصل الذمة المدينة عن الرواتب الدائنة");}
        // A zero-pay period still has an immutable run; no zero or fictitious journal lines.
        var journal=lines.Count>0?FinancialCore.PostJournal(c,tx,"payroll_accrual","إثبات مسير معتمد ومقاصة العجز",result.Period,"PAYROLL:"+run.S("id"),lines,actor,result.Iqama,"",sponsor,sponsor):new JsonObject();
        run["accountingVersion"]=Version;run["sponsorId"]=sponsor;run["journalEntryId"]=journal.S("id");
        return journal;
    }
    public static JsonObject PostInstallment(DbConnection c,DbTransaction tx,JsonObject debt,PayrollResult result,string runId,decimal amount,string reference)
    {
        var run=Store.Get(c,runId,"run",tx);
        string creditor=FinancialCore.RequireDebtSponsor(debt).Id;
        string payer=run?.S("sponsorId",creditor)??creditor;
        if(run?.N("accountingVersion")==Version)J.Need(-Balance(c,tx,"2110",result.Iqama,payer)>=amount,"الرصيد الدائن المعتمد لا يكفي لاستقطاع القسط.");
        var lines=new JsonArray(FinancialCore.CreateJournalLine("2110",payer,amount,0,"استقطاع من راتب معتمد",result.Iqama,debt.S("id")),
            FinancialCore.CreateJournalLine(ReceivableCode(debt),creditor,0,amount,"تخفيض ذمة الدين بالقسط",result.Iqama,debt.S("id")));
        if(payer!=creditor){lines.Add(FinancialCore.CreateJournalLine("1310",payer,0,amount,"استقطاع لحساب سجل دائن آخر",result.Iqama,debt.S("id")));lines.Add(FinancialCore.CreateJournalLine("1310",creditor,amount,0,"مستحق لدى سجل الراتب",result.Iqama,debt.S("id")));}
        return FinancialCore.PostJournal(c,tx,"payroll_deduction","استقطاع قسط من المسير",result.Period,reference,lines,"payroll_engine",result.Iqama,debt.S("id"),payer,creditor);
    }
    public static JsonArray CompanyPaymentLines(string control,string owner,string vault,decimal amount,string iqama,string debtId,JsonObject evidence)
    {
        var account=FinancialCore.ResolveVault(vault);string payer=account.DefaultSponsorId!;
        // Jahez settlement money exists to pay riders' approved dues (2110) of the same company; nothing else.
        bool jahezPayroll=control=="2110"&&owner==payer&&account.VaultKind is "MainInternal" or "Bank"&&account.VaultPurpose is "JahezSettlement" or "GeneralAndSettlement";
        J.Need(jahezPayroll||account.VaultKind is "MainInternal" or "Bank"&&account.VaultPurpose is "DebtAndHr" or "Bank"&&account.AllowedDirections!="InOnly","الدفع من نقدية الشركة الحرة فقط؛ أموال تصفيات جاهز تصرف لرواتب نفس السجل فقط، والرسوم والمقاصة معزولة.");
        var lines=new JsonArray(FinancialCore.CreateJournalLine(control,owner,amount,0,"سداد استحقاق مثبت",iqama,debtId),FinancialCore.CreateJournalLine(vault,payer,0,amount,"دفع فعلي من البنك/الصندوق المالك",iqama,debtId));
        if(owner!=payer)
        {
            J.Need(account.VaultKind=="Bank"&&evidence.B("onBehalf")&&evidence.S("onBehalfNote").Trim()!="","الدفع من بنك شركة أخرى يحتاج اختيار الدفع بالنيابة وبيان المستند صراحة.");
            lines.Add(FinancialCore.CreateJournalLine("1310",payer,amount,0,"حق على السجل المستفيد: "+evidence.S("onBehalfNote"),iqama,debtId));
            lines.Add(FinancialCore.CreateJournalLine("1310",owner,0,amount,"التزام للسجل الدافع: "+evidence.S("onBehalfNote"),iqama,debtId));
        }
        evidence["payerSponsorId"]=payer;return lines;
    }
    public static JsonObject PostPayment(DbConnection c,DbTransaction tx,JsonObject payment,string sponsor,string vault,string actor)
    {
        var account=FinancialCore.ResolveVault(vault);

        decimal amount=-payment.N("amount");string iqama=payment.S("iqama");
        J.Need(-Balance(c,tx,"2110",iqama,sponsor)>=amount,"لا يوجد رصيد راتب دائن مطابق؛ يلزم تسوية الافتتاح أولًا.");
        DemandAvailable(c,tx,vault,amount);
        return FinancialCore.PostJournal(c,tx,"payroll_payment","صرف راتب من حساب الشركة",payment.S("period"),"PAYMENT:"+payment.S("sourceRef"),CompanyPaymentLines("2110",sponsor,vault,amount,iqama,"",payment),actor,iqama,"",account.DefaultSponsorId!,sponsor);
    }
}
